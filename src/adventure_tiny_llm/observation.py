"""Stable 32-feature observation contract shared with the Unity Tiny adapter.

Only self-state and visible geometry enter the observation. Outcome deltas are
from previous *observed* interactions; no game reward-rule lookup is supplied.
"""
from __future__ import annotations
from .contracts import validate_observation

# Numeric schema extension: 30=-1 for a visible hostile within 12 units,
# 0 for no interaction target, +1 for correctly facing an NPC. For berries,
# feature 10=.5 and feature 11=observed red-minus-blue material tint;
# feature 13=.5 means a visible sign (not its semantic meaning).
# Feature 28 now represents the current zone fatigue (0.5 at the grace threshold)
# rather than global clock phase; 0..1 uses 2x zone grace as its scale.
# Feature 5/22 now mean Happiness, not Relationship. Old checkpoints can
# load but MUST be re-evaluated since these inputs changed meaning.
# Retains the 32-dimensional model architecture but not identical semantics
# to earlier gameplay; re-evaluate/retrain policies after enabling monsters.
FEATURES = (
    "x_norm", "z_norm", "y_norm", "health", "knowledge", "happiness", "boredom",
    "facing_x", "facing_z", "dialogue_open",
    "seen_herb", "seen_thorn", "seen_lore", "seen_npc",
    "nearest_bearing_x", "nearest_bearing_z", "nearest_distance_norm",
    "nearest_x_norm", "nearest_z_norm", "visible_count_norm",
    "observed_hp_delta", "observed_knowledge_delta", "observed_happiness_delta",
    "observed_boredom_delta", "west_edge", "east_edge", "south_edge", "north_edge",
    "zone_fatigue_norm", "discoveries_norm", "npc_facing_or_hostile", "constant_one",
)
assert len(FEATURES) == 32

def pack(**kwargs: float) -> list[float]:
    unknown = set(kwargs).difference(FEATURES)
    if unknown:
        raise ValueError(f"unknown observation features: {sorted(unknown)}")
    return validate_observation([kwargs.get(name, 0.0) for name in FEATURES])
