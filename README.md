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

## Current state

**Native RL and Unity integration prototype (post-v0.1.0).** The Transformer now supports random-origin actor-critic training in a small synthetic 2D arena, safe checkpoints, a real Unity Tiny AI controller through REST, and optional weight updates from *observed* game outcomes. This is **not PPO**, not language pretraining, and not demonstrated full-game competence. The survival test world now includes hostile wildlife and fruit requiring an explicit eat decision; previous checkpoints must be re-evaluated under the extended observation semantics. Ollama remains fully independent. See [first measured experiment](docs/EXPERIMENTS-2026-11-01.md).

### Suggested tiny configuration

| Field | Initial value |
| --- | --- |
| Numeric observation features | 32 |
| Transformer layers | 2 |
| Model width / heads / KV heads | 64 / 4 / 2 |
| Feed-forward width | 176 (SwiGLU) |
| Maximum observation sequence | 32 steps |
| Output | Discrete movement + interact + answer, value estimate |
| Initial weights | Random (fixed seed) |

The action schema mirrors the Unity controller: `move_x`, `move_z`, `face_x`, `face_z`, `interact`, `answer`.

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

GET /health reports model training status and online updates; POST /act expects 32 numeric features and returns a unique action_id; POST /experience requires that exact issued action_id plus reward and credited action head, rejects replayed/expired IDs, and updates only from real observed effects when online training is explicitly enabled; POST /reset clears agent context. The Unity HUD has separate Train in Game / Evaluate buttons (evaluation is the default). See [API contract](docs/API_CONTRACT.md). The Unity scene AdventureValley already has the AdventureTinyController. Press TAB to cycle Manual → Ollama → Tiny; the camera always remains third-person.

**Important:** The offline training environment is a simplified 2D curriculum. Its improved score must not be confused with measured survival/learning in the full Unity game. This unauthenticated experimental API must run on 127.0.0.1 only.

## Documents

- [Origin and concept (Thai)](docs/ORIGIN_TH.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap and qualification](docs/ROADMAP.md)
- [REST API contract](docs/API_CONTRACT.md)
- [v0.1.0 release notes](docs/releases/v0.1.0.md)
- [First real training & Unity experiment](docs/EXPERIMENTS-2026-11-01.md)
- [How to train directly inside the Unity game (Thai)](docs/REAL_GAME_TRAINING_TH.md)
- [Survival combat, food choices, monster encounters and readable signs (Thai/English)](docs/SURVIVAL_COMBAT_SIGN_TH.md)
- [Survival combat, berries, and sign-reading reward rules](docs/SURVIVAL_COMBAT_SIGN_TH.md)

Release artifacts use the names `AdventureTinyLLM-V0.1.0-Release.zip`, `AdventureTinyLLM-V0.1.0-Document.zip`, and `SHA256SUMS.txt`. The `scripts/package_release.py` helper packages only files committed in Git, with reproducible ZIP metadata.

## License

[MIT](LICENSE). Copyright © 2026 funggier.
