"""Safe checkpoints contain separately identifiable random-origin neural weights."""
from __future__ import annotations
from dataclasses import asdict
from pathlib import Path

import torch
from .config import TinyConfig
from .model import AdventureTransformer

FORMAT_VERSION = 1

def save(path: str | Path, model: AdventureTransformer, optimizer: torch.optim.Optimizer, *, steps: int, seed: int, metrics: dict) -> None:
    if steps < 1:
        raise ValueError("trained checkpoints need at least one optimizer update")
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "metadata": {
            "format_version": FORMAT_VERSION,
            "architecture": "adventure-tiny-transformer",
            "initialization": "random",
            "seed": seed,
            "optimizer_steps": steps,
            "trained": True,
            "observation_schema": "adventure-observation-v1",
            "config": model.config.to_dict(),
            "metrics": metrics,
        },
        "model_state": model.state_dict(),
        "optimizer_state": optimizer.state_dict(),
    }
    temporary = target.with_suffix(target.suffix + ".tmp")
    torch.save(payload, temporary)
    temporary.replace(target)

def load(path: str | Path) -> tuple[AdventureTransformer, dict]:
    payload = torch.load(Path(path), map_location="cpu", weights_only=True)
    meta = payload["metadata"]
    if (meta.get("format_version") != FORMAT_VERSION or
        meta.get("architecture") != "adventure-tiny-transformer" or
        meta.get("initialization") != "random" or
        meta.get("observation_schema") != "adventure-observation-v1" or
        not meta.get("trained") or int(meta.get("optimizer_steps", 0)) < 1):
        raise ValueError("invalid or incompatible checkpoint provenance")
    cfg = TinyConfig.from_dict(meta["config"])
    model = AdventureTransformer(cfg)
    model.load_state_dict(payload["model_state"], strict=True)
    model.eval()
    return model, meta
