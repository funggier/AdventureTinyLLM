# Roadmap

This document distinguishes **implemented** work from **planned** experiments.

| Stage | Status | Scope / acceptance evidence |
| --- | --- | --- |
| AT-001 Repository foundation | COMPLETE | Public GitHub, MIT, rationale, config, numeric action contracts, tiny Transformer, untrained REST API, lightweight tests |
| AT-002 Unity Tiny Adapter | PLANNED | Unity structured observations -> numeric 32-feature encoder; /act -> actual movement, manual/Ollama/Tiny selector; no shared memory |
| AT-003 Native Trainer | PLANNED | Actual RL updates to Tiny weights, bounded rollouts, reward and done semantics, deterministic seed, numeric train metrics |
| AT-004 Checkpoint & evaluation | PLANNED | Model + optimizer + seed + schema version saved; exact reload; held-out maps; untrained vs trained comparison |
| AT-005 ASCA-inspired memory | PLANNED | Familiarity / Surprise / Episodic memory as optional ablation switches, bounded cost |
| AT-006 Accelerated Training Arena | PLANNED | Resettable simulations, reproducible procedural map seeds, faster-than-visualized training |
| AT-007 Optional ML-Agents | PLANNED | Separate adapter without runtime hard dependency, matched observations/actions/rewards |
| AT-008 Temporal alternatives | PLANNED | Tiny Transformer vs MLP vs memory-augmented and larger-context variants at matched budgets |

## Early measurable tasks

- AT-002: Show one actual action from Tiny API moving the Unity actor; retain same third-person camera across modes.
- AT-003: Demonstrate nonzero changes in model weight tensors **and** improving held-out reward over a randomized baseline; do not call an optimizer smoke update learning success.
- AT-004: Verify save/load preserves deterministic inference and that resumed training matches its own seed/optimizer state.
- AT-005: A/B tests of memory modules under equal environmental experience counts.

## API boundaries

Only `/health`, `/act`, and `/reset` are implemented at foundation stage. Proposed `/experience`, `/train`, `/checkpoint/save`, `/checkpoint/load` must be backed by real data/model state before exposing them as functional endpoints.

Training uses game rewards and observed outcomes, never Ollama-generated labels.
