"""Validated, reproducible small-policy configuration (stdlib only)."""
from __future__ import annotations

from dataclasses import asdict, dataclass
import json
from pathlib import Path


@dataclass(frozen=True, slots=True)
class TinyConfig:
    schema_version: int = 5
    seed: int = 1234
    observation_size: int = 56
    max_context: int = 32
    d_model: int = 64
    n_layers: int = 2
    n_heads: int = 4
    n_kv_heads: int = 2
    d_ff: int = 176
    num_move_actions: int = 9
    num_distance_actions: int = 5
    num_interact_actions: int = 2
    num_ability_actions: int = 4
    num_answer_actions: int = 3
    init_std: float = 0.02

    def validate(self) -> "TinyConfig":
        if self.schema_version != 5:
            raise ValueError("unsupported config schema version")
        for key in ("observation_size", "max_context", "d_model", "n_layers",
                    "n_heads", "n_kv_heads", "d_ff", "num_move_actions",
                    "num_distance_actions", "num_interact_actions", "num_ability_actions", "num_answer_actions"):
            if getattr(self, key) <= 0:
                raise ValueError(f"{key} must be positive")
        if self.n_heads % self.n_kv_heads or self.d_model % self.n_heads:
            raise ValueError("invalid grouped-query attention head geometry")
        if (self.d_model // self.n_heads) % 2:
            raise ValueError("RoPE requires even attention head dimension")
        if self.seed < 0 or self.init_std <= 0:
            raise ValueError("invalid random initialization parameters")
        if (self.num_move_actions, self.num_distance_actions, self.num_interact_actions,
            self.num_answer_actions, self.num_ability_actions) != (9, 5, 2, 3, 4):
            raise ValueError("v5 action encoding requires (9, 5, 2, 3, 4) output heads")
        return self

    def to_dict(self) -> dict:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict) -> "TinyConfig":
        return cls(**data).validate()

    @classmethod
    def from_json(cls, file: str | Path) -> "TinyConfig":
        return cls.from_dict(json.loads(Path(file).read_text(encoding="utf-8")))
