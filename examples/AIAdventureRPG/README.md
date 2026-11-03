# AIAdventureRPG Unity Integration Example — v0.3.1 Urgent Combat Reactive

**v0.3.1 update:** A live hostile's new attack windup inside the 9-unit ring now forces an immediate (deduplicated) AI reconsideration without waiting for the ordinary discovery cooldown or the hostile exiting the 18-unit ring. This does not provide a reward or guarantee a successful dodge within 0.55 game seconds. Tiny observation remains 56 features, so compatible trained weights are preserved.

**Release v0.3.0 baseline (2026-11-03):** This development snapshot adds `AdventurePerceptionMemory`, dual 9/18-unit perception hysteresis, persistent five-entry FIFO spatial map per coordinate, and reactive Tiny/Ollama re-planning when a genuinely new Monster/Herb/Thorn/resource/NPC enters the 9-unit range. Tiny now uses **56 numeric features** / `explicit-action-id-v6-spatial`; Ollama receives up to five coordinate snapshots for each prioritized visible or proximity-sensed coordinate at decision time. Successful normal hits earn +0.35 and skill hits +0.40 per target; the kill bonus remains +1.45. See `Docs/AIAdventureRPG/REACTIVE_PERCEPTION_AND_MAP_TH.md` and `docs/SPATIAL_MEMORY_TH.md` in the Tiny repository. The previous v0.2.0 release remains unchanged as a historical 51-feature snapshot; v0.3.0 is the 56-feature version.

This directory is an **actual source snapshot of the AIAdventureRPG module tested in the Unity game through 2026-11-03**, not a mockup. It is included so developers can inspect and modify the whole observation → action → world effect → reward → policy update flow.

**Not a standalone Unity project**: this directory deliberately excludes the rest of the user's shared CityBuilder/HMGF Unity project, its Editor/ProjectSettings, Unity engine, third-party packages, assets outside `Assets/AIAdventureRPG`, and trained checkpoint files. The source depends on three local assembly definitions **HMGF.Core**, **HMGF.World**, and **HMGF.Presentation**. Releasing their unrelated, unreviewed worktree here would be incorrect.

## Contents

- `Assets/AIAdventureRPG/Runtime/` — agent, control modes, 56-feature Tiny adapter with spatial FIFO memory, Ollama, combat state, signed reward policy, MP, attacks, skills, procedural particles, fog/light/ring VFX, world, NPCs and pickups.
- `Assets/AIAdventureRPG/Scenes/AdventureValley.unity` — world setup and serialized gameplay components.
- `Assets/AIAdventureRPG/Editor/AdventureSceneBuilder.cs` — rebuild/test the scene using Unity Editor menu **AI Adventure RPG → Build New Adventure Valley**. **WARNING:** rebuilding replaces the currently opened scene; save your own scene first.
- `Assets/AIAdventureRPG/Tests/EditMode/` — NUnit/EditMode tests for survival, reward policy, combat states, distance decisions and controller contract.
- `Assets/AIAdventureRPG/Materials/` and `Screenshots/` — Unity assets and screenshots with their Unity `.meta` files.
- `Docs/AIAdventureRPG/` — original design (Thai), magic combat, and tactical RL notes.

## Run with the existing HMGF Unity game (preferred)

1. Open the original Unity game in **Unity 6000.6.7f1** at `T:\Space\Projects\Games\AIAdventureRPG`. This working project already contains the HMGF foundation required by the example. No files need to be replaced to use the current project.
2. Open `Assets/AIAdventureRPG/Scenes/AdventureValley.unity`, press **Play**. Inspect `Adventurer` for `AdventureAgent`, `AdventureCombatState`, `AdventureStats`, `AdventureTinyController`, and `AdventureOllama`.
3. For Tiny AI, install dependencies in the separate AdventureTinyLLM source folder and run:
   ```powershell
   python -m venv .venv
   .\.venv\Scripts\python.exe -m pip install -e ".[api,dev]"
   powershell -ExecutionPolicy Bypass -File .\scripts\start_api.ps1 -EnableOnline
   ```
   The API must bind to **127.0.0.1:8765**, and `GET /health` must report `observation_size=56`, `action_protocol=explicit-action-id-v6-spatial` and `training_available=true`.
4. Use HUD mode **MANUAL** to play, **TRAIN TINY** to enable actual RL updates, **USE TRAINED** for deterministic inference with frozen weights, or **OLLAMA** to delegate to a local Ollama endpoint `127.0.0.1:11434/api/chat`. These are distinct modes; simply reading a sign does not train Tiny.
5. Change `AdventureCombatState`: detection radius = 9 world units, combat-exit grace = 1.5 s, low HP = 20%, low MP = 20%. All four are configurable in the Inspector; reward calculation reads the thresholds from each event's pre-action snapshot.
6. Watch the game HUD and Unity Console. In Manual mode use WASD, Shift sprint, **E normal attack or interact**, **F special attack (12 MP, 32 damage)**, **Q Arc Pulse (20 MP, 26 damage in a 5.3-unit radius)**, **R Heal (25 MP, +30 HP)**. Normal attack deals 14 damage and shows only small dust/sparks; skills use colored particles, temporary lights, and magic rings.
7. In Ollama mode, **RESET OLLAMA CONTEXT** clears old action/prompt history, cancels pending calls and starts fresh while keeping game HP/MP and Tiny weights intact.
8. Run Unity **EditMode → AIAdventureRPG.Tests**. Current qualification: **81/81 passed**. Python project runs `python -m pytest`.

## Integrate into another Unity project

1. Create/open a Unity 6 project using a compatible render pipeline (this sample was developed using URP).
2. **First supply the compatible HMGF.Core, HMGF.World and HMGF.Presentation assembly dependencies** from a properly licensed/source-controlled HMGF foundation; these are required by `AIAdventureRPG.Runtime.asmdef`, `AIAdventureRPG.Editor.asmdef`, and `AdventureWorld.cs`. Merely copying this example into an empty Unity project will not compile.
3. Copy `Assets/AIAdventureRPG` **including all .meta files** into `Assets/AIAdventureRPG`. Do not copy another Unity project's Library, Temp, Logs or ProjectSettings blindly.
4. Add and configure your own compatible test framework and Input Manager conventions if needed, then run Unity compilation, sample scene, and tests.
5. Point `AdventureTinyController.apiEndpoint` at your **local** Tiny API. Its policy observation protocol in this v0.3.0 snapshot is **56 numeric features**, `explicit-action-id-v6-spatial`; a 43-feature checkpoint is not compatible.

For a complete standalone demo you must separately supply the missing HMGF modules and Unity project settings; this archive makes **no claim** to be a turnkey Unity game. See [detailed rewards and formulas](../../docs/REWARD_POLICY_TH.md) and [API contract](../../docs/API_CONTRACT.md).

## Explicit train-versus-play behavior

- Training: `TRAIN TINY` -> `POST /act` (non-deterministic sampling) -> Unity resolves movement/pickup/combat -> `AdventureStats` records deltas -> `AdventureRewardPolicy` calculates reward -> `POST /experience` with an exact one-time ID -> PyTorch optimizer updates model & saves checkpoint.
- Playing: `USE TRAINED` -> deterministic inference only, and `/experience` updates disabled.
- Observation-only: reading signs via `/observe` stores data but produces **no neural update**.
- Training evaluation: distinguish API success from strategic skill; compare completed episodes, damage, deaths, kills and returns against baseline agents before drawing conclusions.

## Packaging / provenance

The release ZIP is generated from a committed Git snapshot. Snapshot was copied from the live `T:\Space\Projects\Games\AIAdventureRPG\Assets\AIAdventureRPG` and matching `Docs/AIAdventureRPG` at release preparation. No City Builder framework changes, private trained weights, Ollama logs or user configuration secrets are included.

**Unity example files are provided as an integration reference in this repository under the project's MIT license, subject to any independent license obligations for external HMGF and Unity components, which are not bundled.**
