"""Stable numerical observation and game-action boundary (no hidden rewards)."""
from __future__ import annotations

from dataclasses import asdict, dataclass
from math import isfinite
from typing import Sequence

OBSERVATION_SIZE = 56
MOVEMENTS: tuple[tuple[float, float], ...] = (
    (0.0, 0.0),  # idle
    (0.0, 1.0),  # north
    (0.0, -1.0), # south
    (-1.0, 0.0), # west
    (1.0, 0.0),  # east
    (-0.7071, 0.7071),
    (0.7071, 0.7071),
    (-0.7071, -0.7071),
    (0.7071, -0.7071),
)
ANSWERS = ("none", "good", "bad")
# Discrete world-unit move lengths. Every length is a genuine policy action,
# trained alongside direction from the same real movement outcome.
DISTANCES = (1.0, 2.0, 4.0, 6.0, 10.0)
ABILITIES = ("none", "special", "arc", "heal")


def validate_observation(values: Sequence[float], size: int = OBSERVATION_SIZE) -> list[float]:
    if len(values) != size:
        raise ValueError(f"observation must have exactly {size} features")
    if any(isinstance(x, bool) or not isinstance(x, (int, float)) or not isfinite(x) for x in values):
        raise ValueError("observation values must be finite numbers, not bools")
    return [float(x) for x in values]


@dataclass(frozen=True, slots=True)
class AdventureAction:
    move_x: float
    move_z: float
    face_x: float
    face_z: float
    interact: bool
    answer: str
    value: float = 0.0
    model_trained: bool = False
    move_distance: float = 0.0
    ability: str = "none"

    def to_dict(self) -> dict:
        return asdict(self)


def decode_action(move: int, interact: int, answer: int, distance: int = 0,
                  value: float = 0.0, ability: int = 0) -> AdventureAction:
    if not (0 <= move < len(MOVEMENTS) and 0 <= distance < len(DISTANCES)
            and 0 <= interact < 2 and 0 <= answer < len(ANSWERS)
            and 0 <= ability < len(ABILITIES)):
        raise ValueError("action indices out of bounds")
    x, z = MOVEMENTS[move]
    return AdventureAction(x, z, x, z, bool(interact), ANSWERS[answer],
                           float(value), False, 0.0 if move == 0 else DISTANCES[distance],
                           ABILITIES[ability])
