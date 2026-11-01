"""Tiny random-initialized temporal Transformer policy. No training in v0.1."""
from __future__ import annotations

import torch
from torch import nn
from torch.nn import functional as F

from .config import TinyConfig


class RMSNorm(nn.Module):
    def __init__(self, width: int, eps: float = 1e-6) -> None:
        super().__init__()
        self.eps = eps
        self.weight = nn.Parameter(torch.ones(width))

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return x * torch.rsqrt(x.pow(2).mean(dim=-1, keepdim=True) + self.eps) * self.weight


def apply_rope(x: torch.Tensor) -> torch.Tensor:
    # Shape [batch, heads, sequence, head_dim].
    length, width = x.shape[-2:]
    inv_freq = 1.0 / (10000.0 ** (
        torch.arange(0, width, 2, dtype=torch.float32, device=x.device) / width
    ))
    angles = torch.outer(torch.arange(length, dtype=torch.float32, device=x.device), inv_freq)
    cos = angles.cos().to(dtype=x.dtype)[None, None, :, :]
    sin = angles.sin().to(dtype=x.dtype)[None, None, :, :]
    even, odd = x[..., 0::2], x[..., 1::2]
    return torch.stack((even * cos - odd * sin, even * sin + odd * cos), dim=-1).flatten(-2)


class GroupedQueryAttention(nn.Module):
    def __init__(self, cfg: TinyConfig) -> None:
        super().__init__()
        self.heads = cfg.n_heads
        self.kv_heads = cfg.n_kv_heads
        self.head_dim = cfg.d_model // cfg.n_heads
        self.q = nn.Linear(cfg.d_model, cfg.n_heads * self.head_dim, bias=False)
        self.k = nn.Linear(cfg.d_model, cfg.n_kv_heads * self.head_dim, bias=False)
        self.v = nn.Linear(cfg.d_model, cfg.n_kv_heads * self.head_dim, bias=False)
        self.output = nn.Linear(cfg.d_model, cfg.d_model, bias=False)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        batch, length, _ = x.shape

        def reshape(y: torch.Tensor, heads: int) -> torch.Tensor:
            return y.view(batch, length, heads, self.head_dim).transpose(1, 2)

        q = apply_rope(reshape(self.q(x), self.heads))
        k = apply_rope(reshape(self.k(x), self.kv_heads))
        v = reshape(self.v(x), self.kv_heads)
        k = k.repeat_interleave(self.heads // self.kv_heads, dim=1)
        v = v.repeat_interleave(self.heads // self.kv_heads, dim=1)
        # Explicit causal masking so even the oldest observation cannot read future events.
        scores = (q @ k.transpose(-2, -1)) * (self.head_dim ** -0.5)
        mask = torch.ones((length, length), device=x.device, dtype=torch.bool).triu(1)
        scores = scores.masked_fill(mask[None, None, :, :], float("-inf"))
        weights = F.softmax(scores, dim=-1, dtype=torch.float32).to(q.dtype)
        out = (weights @ v).transpose(1, 2).contiguous().view(batch, length, -1)
        return self.output(out)


class SwiGLU(nn.Module):
    def __init__(self, cfg: TinyConfig) -> None:
        super().__init__()
        self.gate = nn.Linear(cfg.d_model, cfg.d_ff, bias=False)
        self.up = nn.Linear(cfg.d_model, cfg.d_ff, bias=False)
        self.down = nn.Linear(cfg.d_ff, cfg.d_model, bias=False)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.down(F.silu(self.gate(x)) * self.up(x))


class Block(nn.Module):
    def __init__(self, cfg: TinyConfig) -> None:
        super().__init__()
        self.norm_attn = RMSNorm(cfg.d_model)
        self.attention = GroupedQueryAttention(cfg)
        self.norm_ff = RMSNorm(cfg.d_model)
        self.ff = SwiGLU(cfg)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        x = x + self.attention(self.norm_attn(x))
        return x + self.ff(self.norm_ff(x))


class AdventureTransformer(nn.Module):
    """Input [batch,time,features]; output factorized logits + state-value [batch]."""

    def __init__(self, cfg: TinyConfig) -> None:
        super().__init__()
        self.config = cfg.validate()
        self.input = nn.Linear(cfg.observation_size, cfg.d_model)
        self.blocks = nn.ModuleList(Block(cfg) for _ in range(cfg.n_layers))
        self.norm = RMSNorm(cfg.d_model)
        self.move_head = nn.Linear(cfg.d_model, cfg.num_move_actions)
        self.interact_head = nn.Linear(cfg.d_model, cfg.num_interact_actions)
        self.answer_head = nn.Linear(cfg.d_model, cfg.num_answer_actions)
        self.value_head = nn.Linear(cfg.d_model, 1)
        self.apply(self._init_weights)

    def _init_weights(self, module: nn.Module) -> None:
        if isinstance(module, nn.Linear):
            nn.init.normal_(module.weight, mean=0, std=self.config.init_std)
            if module.bias is not None:
                nn.init.zeros_(module.bias)

    @property
    def parameter_count(self) -> int:
        return sum(p.numel() for p in self.parameters())

    def forward(self, observations: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor, torch.Tensor]:
        if observations.ndim != 3 or observations.size(-1) != self.config.observation_size:
            raise ValueError("expected [batch, time, observation_size]")
        if observations.size(1) < 1 or observations.size(1) > self.config.max_context:
            raise ValueError("invalid temporal context length")
        x = self.input(observations)
        for block in self.blocks:
            x = block(x)
        state = self.norm(x[:, -1, :])
        return (self.move_head(state), self.interact_head(state),
                self.answer_head(state), self.value_head(state).squeeze(-1))
