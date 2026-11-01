# World Training Modes + Explicit Action Attribution — 2026-11-01

## Scope

Implemented optional direct training from gameplay in the separate Unity project AIAdventureRPG. Native fast synthetic trainer remains available. Tiny and Ollama are completely independent.

- Unity HUD: **Train in Game** versus **Evaluate (freeze)**; Evaluate is the default.
- Train in Game uses real post-interaction deltas (HP, Knowledge, Relationship, Boredom) as rewards. Raw Tiny policy controls movement without the navigation assistant during training.
- Evaluate runs the model without sending rewards; a separate terrain-aware obstacle avoidance layer can steer around local obstructions.
- REST v2: `/act` returns unique `action_id`. `/experience` requires the exact ID and action head (move/interact/answer), rejects reused, forged, expired, cross-agent, or navigation-overridden events.
- Unity checks that an outcome source is physically near the avatar. Checkpoint is saved atomically after each accepted update and contains model weights + optimizer; previously released v0.1.0 tag is unchanged.
- No ML-Agents, Ollama-teacher data, or pretrained language weights.

## Observed qualification

Python pytest: **15/15 PASS**, including action ID matching/replay expiry and policy gradient tests. Unity script validation: no compiler errors. Unity Console: no error/warning entries in the tested session.

Unity real play, Train in Game selected: first inspection after 14 successful /act requests showed 2 accepted in-game events and 0 rejected; model trained checkpoint advanced to **212 optimizer steps / 12 lifetime online outcome updates**, from previously loaded 207/7. During Evaluate mode, after many more requests, model step count remained 212 — consistent with no online updates while evaluation was selected.

These data demonstrate functioning runtime wiring, not improved full-game survival. Reward assignment is still local/recent and no long-horizon physical trajectory ledger exists. The game currently relies on simplified kinematic motion, and the world-training results are not a trained general navigation model.

## Reproduction

From project root: `powershell -ExecutionPolicy Bypass -File scripts/start_api.ps1 -EnableOnline`.
Open Unity AdventureValley, Play, press TAB twice to Tiny, then press Train in Game on the HUD. For evaluation, press Evaluate (freeze), and check `GET http://127.0.0.1:8765/health` for stable training_steps despite movement.
