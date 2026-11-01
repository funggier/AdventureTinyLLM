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

Server returns a random unique action_id per decision and stores the observation plus action heads for up to 20 seconds (48 outstanding decisions per agent). The client MUST pass the exact action_id for the responsible physical action in /experience. A 32-step temporal context is held in process RAM. Return value is a model prediction, not proof of quality.

## POST /experience

Enabled **only** when ADVENTURE_TINY_ONLINE_TRAIN=1. JSON body: agent_id, action_id, reward (finite float), credit (move / interact / answer), navigation_override (bool), event (short description). Unity sends it only when an actual interaction updates game stats **and the HUD has Train in Game selected**. The server consumes exactly the matched decision once, computes an actor-critic gradient on the selected action head and checkpoints after each accepted update. It refuses unknown, expired, repeated IDs and navigation-assisted interventions.

Response: accepted, online_updates, loss, reward, or accepted=false with reason if no last decision exists. With training disabled returns HTTP 403. The Game's weighted reward follows its measured HP, Knowledge, Relationship, and Boredom deltas; secret game rules are NOT passed as observations.

**Current temporal limitation:** v2 action_id prevents arbitrary last-action matching, but Unity still attributes a collision to the most recently applied move, rather than a full physical action trajectory; outcomes delayed across several commands may still require multi-step trace/episode IDs. The protocol is a safety improvement, not proof of perfect causal assignment.

## POST /reset

Body agent_id. Clears that agent's short-term context and outstanding last decision. Does not reset the game or model weights.

## Not implemented

PPO, long-horizon rollout tracking, world-level save/load, model-based planning, ASCA episodic memory, ML-Agents, remote API authentication.
