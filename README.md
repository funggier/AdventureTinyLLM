# AdventureTinyLLM

**A tiny, randomly initialized Transformer agent for learning through adventure.**

AdventureTinyLLM is a separate, experimental AI project for the local Unity game **AIAdventureRPG**. It aims to let a **very small Transformer** learn from its own actions and consequences in a Unity 3D world: navigating, finding items, avoiding hazards, maintaining health, and interacting with NPCs.

This project is **for exploration and fun**, not a claim of general intelligence or consciousness. It starts **without pretrained weights**. Its experiments must be evaluated against simple baselines.

## Why it exists

Three lines of work inspired the design:

- **[FlyWireLLM](https://github.com/funggier/FlyWireLLM)** — small decoder-only Transformer engineering: random initialization, RMSNorm, RoPE, grouped-query attention, SwiGLU, explicit configurations, reproducible tests, and checkpoint provenance.
- **[ProjectASCA](https://github.com/funggier/ProjectASCA)** — ideas for familiarity, episodic/procedural memory, surprise/mismatch detection, selective recall, and a bounded perceive–act–check loop.
- **AIAdventureRPG (Unity)** — a shared environment providing observed coordinates, visible objects, actions, NPC conversations, health, and actual before/after outcomes.

This is **not a fork** of those projects and does not import their weights, corpora, or private data. Shared ideas are reimplemented independently and measured.

## Principles

1. **Independent learning:** Ollama and AdventureTinyLLM are separate agents. No Ollama teacher, imitation traces, shared memory, or injected answers for Tiny training.
2. **Same game rules:** Human, Ollama, and Tiny agents should use a compatible observation/action interface; the Unity camera remains third-person.
3. **No hidden rewards in observations:** The agent sees status, position, and visible entities. It learns consequences only *after* acting.
4. **Tiny by default:** Compact numeric-input Transformer instead of an enormous text vocabulary; model can later be scaled or tested with text.
5. **Reproducibility:** Explicit random seeds, clean training/evaluation partitions, checkpoint provenance, and objective metrics.
6. **Swappable training:** Native PyTorch reinforcement learning first. ML-Agents will be an **optional adapter**, not a mandatory dependency.

## v0.3.1 — Urgent monster attack awareness (2026-11-03)

A monster already within the 9-unit combat ring now issues a distinct **AttackWindup** interrupt once per committed strike, even if it never exits the 18-unit re-arm ring. These urgent interrupts bypass ordinary new-object discovery cooldown, terminate old AI travel, cancel stale model requests and preserve no-reward observation semantics. Tiny avoids transmitting a stale /act if detection fires while building the current observation. The protocol remains **56 numeric features / explicit-action-id-v6-spatial**; the existing trained checkpoint stays compatible and does **not** need retraining. See [v0.3.1 release notes](docs/releases/v0.3.1.md).

## v0.3.0 — 56-feature reactive spatial memory (2026-11-03)

Version v0.3.0 supports two-tier 9/18-unit perception hysteresis, early re-planning on a newly discovered Monster/Herb/Thorn/resource/NPC, and a persisted spatial coordinate map with five FIFO observations per cell. Ollama receives full history text for up to four visible or combat-sensed coordinates each decision; Tiny receives five additional numeric spatial-memory features, making 56 inputs and schema v5 / action protocol `explicit-action-id-v6-spatial`. The trained 51-input policy (17,469 updates) was archived separately before retraining the new model. Ordinary hit reward increased to +0.35 and skill hit to +0.40. See [Spatial Memory & Reactive Perception design](docs/SPATIAL_MEMORY_TH.md). The earlier **v0.2.0** release is preserved with its original 51-feature protocol; do not mix its Unity example and Python service with v0.3.0.

## v0.2.0 — Tactical policy and Unity gameplay example (2026-11-02)

**Schema v4, 51 inputs, protocol `explicit-action-id-v5-tactical`.** Eight new input channels describe proximity-based combat, low HP, low MP, sensed hostile distance/bearing, telegraphed enemy windup and distance to an actually visible thorn. The reward policy is now state-dependent: killing a monster, successful skill hits and genuine avoidance are positive; misses, damage (especially HP below 20%), wasted healing/MP and death are negative. Unity keeps manual controls independent and Ollama has a persistent-context reset button. The previous 43-input online checkpoint is archived unchanged at `checkpoints/legacy/tiny-unity-pre-tactical-v3-43features-2026-11-02.pt` and a new 51-input checkpoint is trained online from random initialization. See the included [full Reward Scoring Guide (Thai)](docs/REWARD_POLICY_TH.md), [REST API contract](docs/API_CONTRACT.md), and [Unity gameplay example](examples/AIAdventureRPG/README.md).

## Current state

**Native RL and Unity integration prototype (post-v0.1.0).** The Transformer now supports random-origin actor-critic training in a small synthetic 2D arena, safe checkpoints, a real Unity Tiny AI controller through REST, and optional weight updates from *observed* game outcomes. The Unity client now optionally supplies nine legal movement actions to `/act` so a trained model cannot keep commanding movement beyond hard map edges. The API samples/argmaxes only legal logits and stores that same mask for correct move-policy gradient credit. The current model uses **56 observation features** (including eight tactical context inputs and five spatial-memory summaries, with independently trainable ability and distance heads); legacy 32-feature checkpoints are **not directly reusable** and are preserved as archival backups; this does not guarantee learned navigation beyond boundaries. This is **not PPO**, not language pretraining, and not demonstrated full-game competence. The survival test world now includes hostile wildlife and fruit requiring an explicit eat decision; previous checkpoints must be re-evaluated under the extended observation semantics. Ollama remains fully independent. See [first measured experiment](docs/EXPERIMENTS-2026-11-01.md).

### Suggested tiny configuration

| Field | Initial value |
| --- | --- |
| Numeric observation features | 56 (32 original + 8 motion/encounter + 3 MP/HP availability + 8 tactical context + 5 spatial memory) |
| Transformer layers | 2 |
| Model width / heads / KV heads | 64 / 4 / 2 |
| Feed-forward width | 176 (SwiGLU) |
| Maximum observation sequence | 32 steps |
| Output | 9 directions + 5 distances + interact + answer + 4 abilities (none/special/arc/heal), value estimate |
| Initial weights | Random (fixed seed) |

The action schema mirrors the Unity controller: `move_x`, `move_z`, **`move_distance`**, `face_x`, `face_z`, `interact`, `answer`, and **`ability`** (none/special/arc/heal). Distance execution is finite and measured in game-world XZ units; MANUAL movement is unchanged.

## Current checkpoint compatibility and historical training resets

**v0.3.0 (2026-11-03):** The previous 51-feature policy with 17,469 optimizer updates has been preserved intact at `checkpoints/legacy/tiny-unity-pre-spatial-v4-51features-2026-11-03.pt`. Since the new 56-feature observation projection changes the model shape, v0.3.0 starts with fresh weights; compatible new weights are saved to `checkpoints/tiny-unity-online.pt` as the user trains in Unity. Checkpoints are local and excluded from release ZIP files. Do not rename or load 51-input checkpoints into the 56-input service.

**Previous resets (2026-11-02):**

The v0.2.0 release preserves legacy checkpoint formats under `checkpoints/legacy` in the local training workspace rather than distributing weights. The new 51-feature model has **66 accepted online updates during QA**, but the trained file is intentionally excluded from source archives. The earlier model reset deliberately removed the former trained files. A later session generated 490 online steps; for the finite-distance v2 architecture, that checkpoint is **backed up unchanged at** `checkpoints/legacy/tiny-unity-pre-finite-distance-2026-11-02.pt`. The new 51-feature tactical model starts with fresh random weights; the prior 43-feature magic model is archived unchanged in `checkpoints/legacy/tiny-unity-pre-tactical-v3-43features-2026-11-02.pt`; the later 40-feature v2 checkpoint was also archived in `checkpoints/legacy/tiny-unity-pre-magic-v2-final-2026-11-02.pt`. The previously trained online, synthetic and backup checkpoints were deliberately deleted to start again on the revised game environment. The live local API starts with random weights if no compatible 51-feature checkpoint exists; online training is explicitly opt-in from Unity. See [Fresh Unity training reset and verification](docs/TRAINING-RESET-2026-11-02.md). Historical experiment numbers below describe earlier checkpoints, **not** a trained current model.

## Run Tiny AI, train and integrate with Unity

Requires Python 3.11+ and an installed, compatible PyTorch wheel:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -e ".[api,dev]"
python -m pytest
python -m uvicorn adventure_tiny_llm.api:app --host 127.0.0.1 --port 8765
```

For Windows, after installing the Python dependencies, the convenience scripts are:

    powershell -ExecutionPolicy Bypass -File .\scripts\train.ps1
    powershell -ExecutionPolicy Bypass -File .\scripts\start_api.ps1 -EnableOnline

The API starter selects the saved online checkpoint first, otherwise the offline checkpoint. It binds only to 127.0.0.1. Omit -EnableOnline to run inference without changing weights. Keep this PowerShell window open while playing Unity.

To train a numeric navigation policy from scratch manually, run this in the local virtual environment:

    python -m adventure_tiny_llm.training --steps 200 --batch-size 32 --seed 1234 --output checkpoints/tiny-v1.pt

Point ADVENTURE_TINY_CHECKPOINT at checkpoints/tiny-v1.pt to load learned weights. To enable updates from Unity's *actual post-action stat deltas*, set ADVENTURE_TINY_ONLINE_TRAIN=1 and ADVENTURE_TINY_ONLINE_OUTPUT to checkpoints/tiny-unity-online.pt (prefer absolute paths). Start the REST API with uvicorn. Every accepted Unity outcome updates the model and saves an independent online checkpoint; future runs can load that checkpoint to resume weights and optimizer state.

GET /health reports model training status and online updates; POST /act expects 56 numeric features and returns a finite `move_distance` and a selected `ability` and returns a unique action_id; POST /experience requires that exact issued action_id plus reward and credited action head, rejects replayed/expired IDs, and updates only from real observed effects when online training is explicitly enabled; POST /reset clears agent context. The Unity HUD has a dedicated four-mode controller: MANUAL / TRAIN TINY / USE TRAINED / OLLAMA. USE TRAINED is disabled until the running API verifies trained weights; MANUAL is the default, and TAB never enables training. See [API contract](docs/API_CONTRACT.md). The Unity scene AdventureValley already has the AdventureTinyController. TAB safely cycles Manual → Ollama → trained Tiny (if available) → Manual without enabling training; the camera remains third-person.

**Important:** The offline training environment is a simplified 2D curriculum. Its improved score must not be confused with measured survival/learning in the full Unity game. This unauthenticated experimental API must run on 127.0.0.1 only.

## Documents

- [Complete Reward Scoring Criteria & Worked Examples (Thai)](docs/REWARD_POLICY_TH.md)
- [Unity gameplay integration reference with scene and tests](examples/AIAdventureRPG/README.md)
- [v0.3.1 release notes](docs/releases/v0.3.1.md)
- [v0.3.0 release notes](docs/releases/v0.3.0.md)
- [Spatial Memory and Reactive Perception (Thai)](docs/SPATIAL_MEMORY_TH.md)
- [v0.2.0 release notes](docs/releases/v0.2.0.md)
- [Tactical RL v4, 51-feature context and rewards (Thai)](docs/TACTICAL_POLICY_TH.md)
- [Previous magic combat and Tiny AI v3, 43-feature protocol (Thai)](docs/MAGIC_POLICY_TH.md)
- [Finite-distance movement, encounters and new Tiny neural action/observation contract (Thai)](docs/FINITE_DISTANCE_ACTIONS_TH.md)
- [Origin and concept (Thai)](docs/ORIGIN_TH.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap and qualification](docs/ROADMAP.md)
- [REST API contract](docs/API_CONTRACT.md)
- [v0.1.0 release notes](docs/releases/v0.1.0.md)
- [First real training & Unity experiment](docs/EXPERIMENTS-2026-11-01.md)
- [Fresh training reset and revised environment (2026-11-02)](docs/TRAINING-RESET-2026-11-02.md)
- [How to train directly inside the Unity game (Thai)](docs/REAL_GAME_TRAINING_TH.md) — now includes Happiness (replacing Relationship) and anti-wiggle no-progress penalties.
- [Survival combat, food choices, monster encounters and readable signs (Thai/English)](docs/SURVIVAL_COMBAT_SIGN_TH.md)
- [Survival combat, berries, and sign-reading reward rules](docs/SURVIVAL_COMBAT_SIGN_TH.md)

v0.3.1 release assets: `AdventureTinyLLM-V0.3.1-Release.zip`, `AdventureTinyLLM-V0.3.1-Document.zip`, `AdventureTinyLLM-V0.3.1-Unity-Example.zip`, plus `SHA256SUMS.txt`. Earlier release assets remain unchanged. The original v0.2.0 archives and release remain unchanged. The `scripts/package_release.py` helper packages only the exact committed Git tree with deterministic ZIP member order/timestamps; trained weights, Windows settings, Unity Library, and unrelated City Builder files are excluded.

## License

[MIT](LICENSE). Copyright © 2026 funggier.
