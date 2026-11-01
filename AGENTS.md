# AdventureTinyLLM project instructions

- Repository: funggier/AdventureTinyLLM, location T:\Space\Projects\ProjectsAI\AdventureTinyLLM, MIT License.
- Keep historical v0.1.0 tag and release immutable. No automatic releases without user request.
- Random-origin Tiny Transformer ~95,567 parameters; no Ollama, imitation learning, ML-Agents or imported weights. Tiny and Ollama memory/training remain isolated.
- Native first-generation RL is one-step actor-critic, NOT PPO. Offline synthetic 2D arena outcomes differ from full Unity gameplay.
- Unity source: T:\Space\Projects\Games\AIAdventureRPG. Use existing AdventureValley scene, 3rd-person camera and AdventureTinyController. Preserve unrelated dirty worktree on feature/road-drag.
- Numeric observation v1 schema of 32 features is shared between src/adventure_tiny_llm/observation.py and Unity Encode().
- Only post-action real-game HP/Knowledge/Relationship/Boredom deltas cause online updates, never rules/labels from Ollama.
- Checkpoints ignored in Git: checkpoints/tiny-v1.pt for offline synthetic model, checkpoints/tiny-unity-online.pt for independent Unity outcome lineage and optimizer. Atomic checkpoint updates on accepted real-world events.
- REST API binds to 127.0.0.1:8765 only. No authentication; do not publicly expose.
- Tests: .venv\Scripts\python.exe -m pytest -q; verify /health trained, /act, /experience, checkpoint reload, Unity Play Mode real movement and Console.
- Next priorities: exact action attribution over delays, held-out Unity maps, navigation/realistic training arena, optional ASCA memory, optional ML-Agents.
