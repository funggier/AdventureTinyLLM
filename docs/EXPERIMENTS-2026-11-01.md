# First RL + Unity Experiment — 2026-11-01

Status: Native PyTorch one-step actor-critic RL, tiny Transformer deployed to real Unity, first actual game-outcome gradient updates verified.

## Initial offline experiment (synthetic 2D navigation curriculum)

- Architecture: 95,567 parameters, 2 causal Transformer layers, 64 dimensions, 32 numeric observations.
- Independent random initialization seed 1234. No Ollama, imitation, or ML-Agents.
- 200 optimizer updates, batch size 32, one-step advantage actor-critic (NOT PPO).
- Fixed synthetic test seed 101234 (256 samples): mean reward -0.0173452754 before, +0.9090339774 after.
- New synthetic test seed 888811 (512 samples): mean reward +0.9039304500.
- Max absolute input-layer weight change: 0.04643898.
- Offline saved checkpoint: checkpoints/tiny-v1.pt (local only, gitignored).
- **This is NOT evidence of full Unity navigation, NPC or survival competence.**

## Actual Unity test

Scene: Assets/AIAdventureRPG/Scenes/AdventureValley.unity in the user's Unity project. Avatar has a separate AdventureTinyController, using local HTTP API port 8765. Manual / Ollama / Tiny selectable with TAB, one persistent third-person camera.

- Neural policy issued 100+ successful movement actions to the REAL Unity avatar, without observed API failures.
- In autonomous play, Tiny independently encountered and collected 2 herbs. These were NOT forced pickups. Each generated a real post-action HP feedback and neural gradient update.
- Three additional game outcomes (Rune, Thorn, Herb) were explicitly triggered using Unity editor scripting to test the event-training interface. **These 3 are manually forced tests, NOT autonomous discoveries.**
- At 5 outcomes the API saved checkpoints/tiny-unity-online.pt with model weights + optimizer state.
- The service was restarted from this online checkpoint; 5 prior online updates and optimizer state were recovered. Two additional naturally encountered herbs produced two more online updates.
- After restart the reloaded checkpoint reported 207 optimizer steps total, 7 game-outcome updates and valid trained model. Autosave is now performed after each observed outcome.
- Unity Console during successful tests: zero errors/warnings.
- The small number of game events does NOT establish that online learning improved Unity gameplay performance.

## How it works

Shared numeric v1 observation contract: position, facing, HP, Knowledge, Relationship, Boredom, nearest visible object type/direction/distance, observed previous event deltas. No hidden reward table, no future information. The simulated arena supplies reward AFTER the chosen action. In Unity, an event is reported only after game interaction.

API /act returns movement/interaction policy and stores its last decision; /experience applies a one-step actor-critic update from actual weighted stat change, saves a separate online checkpoint atomically and never modifies the pretrained-by-simulation checkpoint. /reset clears only the agent temporal context.

Training is independent from Ollama. The model is not trained as a language model and has no language understanding.

## Reproducibility

From the AdventureTinyLLM project root on Windows:

1. Run .venv\Scripts\python.exe -m pytest -q
2. Run .venv\Scripts\python.exe -m adventure_tiny_llm.training --steps 200 --batch-size 32 --seed 1234 --output checkpoints/tiny-v1.pt
3. Set environment ADVENTURE_TINY_CHECKPOINT to an absolute path to checkpoints\tiny-v1.pt, ADVENTURE_TINY_ONLINE_TRAIN=1, ADVENTURE_TINY_ONLINE_OUTPUT to an absolute path to checkpoints\tiny-unity-online.pt.
4. Run .venv\Scripts\python.exe -m uvicorn adventure_tiny_llm.api:app --host 127.0.0.1 --port 8765
5. Unity Play -> TAB twice from Manual to Tiny; leave the existing third-person camera.
6. On subsequent runs set ADVENTURE_TINY_CHECKPOINT to the saved tiny-unity-online.pt so the trained weights and optimizer resume.

If online training is disabled (default), /experience returns 403; the existing checkpoint can still serve inference.

## Known limitations

Temporal credit assignment currently pairs a delayed outcome with the latest /act rather than an immutable action ID. The field-of-view sensor and movement controller do not yet implement full world pathfinding, skills, quests or persistent physical world save/load. Training arena only approximates object-directed movement, so generalization to novel Unity maps requires separate held-out evaluations. ML-Agents remains optional and unimplemented.
