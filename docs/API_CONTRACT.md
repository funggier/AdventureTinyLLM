# AdventureTinyLLM REST API — experimental v0.2-dev contract

The service runs only on loopback at http://127.0.0.1:8765. It has **no authentication**; never expose it to a LAN or the internet.

## Environment

- ADVENTURE_TINY_CHECKPOINT: path to the trained .pt checkpoint; if omitted model weights are freshly random.
- ADVENTURE_TINY_ONLINE_TRAIN=1: explicitly enable live neural updates from post-action reward.
- ADVENTURE_TINY_ONLINE_OUTPUT: separate path for atomic online checkpoint saves after every accepted event (default checkpoints/tiny-unity-online.pt).
- When restarting from an online checkpoint, set CHECKPOINT to the online file. Saved model and optimizer states both resume.

## GET /health

Reports service, trained, training_available, parameters, training_steps, online_updates, seed, max_context and checkpoint. For a loaded checkpoint, trained=true. training_available is only true with the explicit online-training environment flag.

## POST /act

Request: agent_id (string), observation (32 finite numbers following src/adventure_tiny_llm/observation.py), deterministic (boolean). Response: move_x/move_z, face_x/face_z, interact (bool), answer (none/good/bad), value (float) and model_trained (bool). This is a **numeric policy**, not a language model.

Server remembers the latest action and its observation independently for each agent, for optional outcome-based updates. A 32-step temporal context is held in process RAM. Return value is a model prediction, not proof of quality.

## POST /experience

Enabled **only** when ADVENTURE_TINY_ONLINE_TRAIN=1. JSON body: agent_id, reward (finite float), event (short description). Unity sends it only when an actual interaction updates game stats. The server consumes the agent's last decision once, computes an actor-critic gradient from the observed reward and checkpoints after each accepted update.

Response: accepted, online_updates, loss, reward, or accepted=false with reason if no last decision exists. With training disabled returns HTTP 403. The Game's weighted reward follows its measured HP, Knowledge, Relationship, and Boredom deltas; secret game rules are NOT passed as observations.

**Current temporal limitation:** The latest /act is used for credit assignment. When outcomes are delayed, that may not be the actual responsible action. Next phase should carry action_id and trajectory/episode identity.

## POST /reset

Body agent_id. Clears that agent's short-term context and outstanding last decision. Does not reset the game or model weights.

## Not implemented

PPO, long-horizon rollout tracking, world-level save/load, model-based planning, ASCA episodic memory, ML-Agents, remote API authentication.
