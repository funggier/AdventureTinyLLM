"""Stable 51-feature observation contract shared with the Unity Tiny adapter.

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
# The first 32 features remain in their earlier positions. Eight new features
# encode observed finite travel, actual blocked/reached status and last visible
# object's type/location. This is a new model architecture; retrain weights.
FEATURES = (
    "x_norm", "z_norm", "y_norm", "health", "mp", "happiness", "boredom",
    "facing_x", "facing_z", "dialogue_open",
    "seen_herb", "seen_thorn", "seen_lore", "seen_npc",
    "nearest_bearing_x", "nearest_bearing_z", "nearest_distance_norm",
    "nearest_x_norm", "nearest_z_norm", "visible_count_norm",
    "observed_hp_delta", "observed_mp_delta", "observed_happiness_delta",
    "observed_boredom_delta", "west_edge", "east_edge", "south_edge", "north_edge",
    "zone_fatigue_norm", "discoveries_norm", "npc_facing_or_hostile", "constant_one",
    "last_requested_distance_norm", "last_travelled_distance_norm",
    "last_move_status", "last_stop_x_norm", "last_stop_z_norm",
    "last_seen_kind", "last_seen_x_norm", "last_seen_z_norm",
    "can_special", "can_arc", "can_heal",
    "in_combat", "low_hp", "low_mp", "nearest_monster_distance_norm",
    "monster_bearing_x", "monster_bearing_z", "monster_winding_up",
    "visible_thorn_distance_norm",
)
assert len(FEATURES) == 51

def pack(**kwargs: float) -> list[float]:
    unknown = set(kwargs).difference(FEATURES)
    if unknown:
        raise ValueError(f"unknown observation features: {sorted(unknown)}")
    return validate_observation([kwargs.get(name, 0.0) for name in FEATURES])
