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

Request: agent_id (string), observation (32 finite numbers following src/adventure_tiny_llm/observation.py), deterministic (boolean), and optional `valid_moves` (exactly nine booleans, at least one true). Response: move_x/move_z, face_x/face_z, interact (bool), answer (none/good/bad), value (float) and model_trained (bool). This is a **numeric policy**, not a language model.

When supplied, `valid_moves` masks the movement logits **before** deterministic argmax or stochastic sampling, so the model can never choose a physically unavailable movement. Indices must match `MOVEMENTS`: `[idle, north, south, west, east, northwest, northeast, southwest, southeast]`. The trainer stores that exact availability mask with its action ID and applies the SAME masked move-logit distribution to its policy-gradient loss. Old clients without masks remain compatible. In Unity, the optional safety filter uses the hard world clamp (2..width-2, 2..depth-2), excludes outward choices near an edge, and may exclude idle after prolonged stagnation when an inward route exists. **Persistent anti-stuck recovery:** after >12 game seconds with no meaningful progress within 8 units of the outer perimeter, the client temporarily masks movement to the inward axes of the triggering boundary until the avatar has reached 16 units inside that boundary (or boundaries at a corner). It may activate again if the agent returns to the same unproductive rim. Since this constrained movement is safety assistance rather than freely learned navigation, its move-head gameplay rewards and no-progress penalties are not credited to the learner; independently chosen interact/answer actions can still be credited. Delayed responses that become illegal at their time of application are discarded without action credit. **This is legal-action sampling, not an artificial navigation reward or an Ollama-controlled detour.** The rule is a hard-edge guard, not full terrain pathfinding.

Server returns a random unique action_id per decision and stores the observation plus action heads for up to 20 seconds (48 outstanding decisions per agent). The client MUST pass the exact action_id for the responsible physical action in /experience. A 32-step temporal context is held in process RAM. Return value is a model prediction, not proof of quality.

## Survival feature migration: Happiness and no-progress reward

Feature 5 now represents normalized **Happiness** (instead of Relationship); feature 22 represents the last observed Happiness delta. Feature 28 now conveys **zone fatigue normalized**: `clamp01(seconds_in_current_zone / (zoneGraceSeconds × 2))`. With default 16×16 world-unit zones, grace of 35 game seconds corresponds to 0.5. The map contains 4×4 such zones at 64×64 total size. This replaces global clock phase in the former index 28. Observation dimensionality remains 32, so old checkpoint weights can load but their previous feature meanings have changed and policies require new evaluation. The Unity environment now counts meaningful outcomes: useful pickups/eating, genuine NPC conversations or new knowledge, and monster hits/kills. After 12 game seconds without such progress, Happiness decays slowly even when the avatar wiggles or walks. `PassiveNoProgress` emits a small negative move-head `/experience` for an actual issued `action_id`; reading a sign remains zero-reward and skipped for this penalty. It never rewards mere movement.

## POST /observe — zero-reward information intake

Request: `{"agent_id":"tiny-unity","kind":"sign","source":"Camp safety board","text":"Duskberry is poisonous"}`. The server stores at most 24 sign observations per agent in bounded process memory, returning `{"accepted":true,"kind":"sign","reward":0,"model_updated":false,"observed_count":1}`.

**No neural learning from merely reading:** this endpoint does not consume action IDs, update policy weights, send an RL experience, or perform language understanding. The 95k-parameter numeric policy cannot reason about the natural-language sign text. Data is not sent to or from Ollama. This observation buffer resets on `/reset`.

## POST /experience

Enabled **only** when ADVENTURE_TINY_ONLINE_TRAIN=1. JSON body: agent_id, action_id, reward (finite float), credit (move / interact / answer), navigation_override (bool), event (short description). Unity sends it only when an actual interaction updates game stats **and the HUD has Train in Game selected**. The server consumes exactly the matched decision once, computes an actor-critic gradient on the selected action head and checkpoints after each accepted update. It refuses unknown, expired, repeated IDs and navigation-assisted interventions.

Response: accepted, online_updates, loss, reward, or accepted=false with reason if no last decision exists. With training disabled returns HTTP 403. The Game's weighted reward follows its measured HP, Knowledge, Happiness, and Boredom deltas; secret game rules are NOT passed as observations.

**Current temporal limitation:** v2 action_id prevents arbitrary last-action matching, but Unity still attributes a collision to the most recently applied move, rather than a full physical action trajectory; outcomes delayed across several commands may still require multi-step trace/episode IDs. The protocol is a safety improvement, not proof of perfect causal assignment.

## POST /reset

Body agent_id. Clears that agent's short-term context and outstanding last decision. Does not reset the game or model weights.

Survival extension: Unity supplies the nearest **visible monster within 12 units** as primary observation when present, with index 30 set to `-1` for a nearby hostile (otherwise `0` or `+1` for NPC facing). It retains the numeric 32-feature network size but introduces new semantics, so comparisons against the original synthetic trainer must be evaluated again. Kill reward, monster damage, HP/food effects and other scores are computed after interactions by Unity; only one chosen action head receives credit.

## Not implemented

PPO, long-horizon rollout tracking, world-level save/load, model-based planning, ASCA episodic memory, ML-Agents, remote API authentication.
