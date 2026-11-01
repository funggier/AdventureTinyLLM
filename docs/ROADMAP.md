# Roadmap

This document distinguishes **implemented** work from **planned** experiments.

| Stage | Status | Scope / acceptance evidence |
| --- | --- | --- |
| AT-001 Repository foundation | COMPLETE | Public GitHub, MIT, rationale, config, numeric action contracts, tiny Transformer, untrained REST API, lightweight tests |
| AT-002 Unity Tiny Adapter | IMPLEMENTED (prototype) | 32 numeric features, /act->real Unity movement, Manual/Ollama/Tiny selector, 3rd-person camera, separate Ollama memory |
| AT-003 Native Trainer | PARTIAL | Native one-step actor-critic training in synthetic 2D arena, 200 steps, reward improvements on held-out synthetic seeds; full multi-step RL/episodes and real game performance evaluation pending |
| AT-004 Checkpoint & evaluation | PARTIAL | Model + optimizer + seed + schema v1 saved, safe reload, independent online Unity outcomes, held-out synthetic seeds; held-out Unity maps and exact optimizer trajectory equivalence pending |
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

Implemented: /health, /act with unique action_id, /reset, and gated /experience with replay/expiry rejection and one-time credit assignment (real post-action reward triggers gradient update and atomic save). Native CLI handles offline train/checkpoints. /train, /checkpoint/save, /checkpoint/load are not REST endpoints, and full PPO remains future work.

Training uses synthetic arena rewards or real observed Unity status deltas, never Ollama-generated labels. First measured outcomes: [2026-11-01 experiment](EXPERIMENTS-2026-11-01.md).
