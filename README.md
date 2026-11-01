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

**Initial repository / engineering prototype.** Contains the design, typed contracts, a tiny random-initialized Transformer, and an optional REST inference API. **PPO/online learning, replay, and model training are planned, not implemented.** An untrained model's actions are random/meaningless; a successful API response does **not** demonstrate learning.

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

## Try the untrained inference API

Requires Python 3.11+ and an installed, compatible PyTorch wheel:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -e ".[api,dev]"
python -m pytest
python -m uvicorn adventure_tiny_llm.api:app --host 127.0.0.1 --port 8765
```

`GET http://127.0.0.1:8765/health` reports whether this is an **untrained** random model. `POST /act` expects an `observation` array of 32 numeric features. `POST /reset` clears one agent's observation history. See [API contract](docs/API_CONTRACT.md). **No training endpoint is advertised until it trains real weights.** Never expose this unauthenticated prototype service to public networks.

The existing Unity Ollama adapter uses a different prompt/endpoint. A dedicated Unity Tiny adapter and shared observation encoder are upcoming steps, **not yet connected**.

## Documents

- [Origin and concept (Thai)](docs/ORIGIN_TH.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap and qualification](docs/ROADMAP.md)
- [REST API contract](docs/API_CONTRACT.md)

## License

[MIT](LICENSE). Copyright © 2026 funggier.
