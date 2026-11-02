# Fresh Unity training reset — 2026-11-02

## Scope

Reset AdventureTinyLLM training to randomly initialized neural weights so the revised Unity AIAdventureRPG environment can be trained afresh. This is NOT a model fine-tune or continuation of the previous learned policy. Historical experiment reports remain as archival evidence; the Unity game and separate Ollama memory are not deleted.

## Actual reset performed

1. Stopped the existing local Tiny API on `127.0.0.1:8765` (the previous live /health reported 15,642 training steps and 15,442 online updates).
2. With the service stopped, removed all three known weight files, including optimizer states:
   - `checkpoints/tiny-unity-online.pt`
   - `checkpoints/tiny-v1.pt`
   - `checkpoints/backups/tiny-before-boundary-mask-2026-11-02.pt`
3. Verified no trained model artifacts remained in the `checkpoints` tree.
4. Launched `scripts/start_api.ps1 -EnableOnline` with no checkpoint available: this initializes the 95,567-parameter model from the configured random seed.
5. Inspected live `GET /health` with `trained=false`, `training_steps=0`, `online_updates=0`, `checkpoint=null`, `training_available=true`.
6. Ran Python tests: 23/23 passed. Observed Unity editor EditMode tests: 32/32 passed.
7. Checked real Unity Tiny Evaluate requests against the fresh API: actions accepted with zero API failures and `modelTrained=false`. Evaluate does not update weights.

## Revised Unity learning environment

- **Boredom**: residence inside the same 16x16 broad zone for more than 35 seconds; no special idle penalty, and movement no longer directly reduces boredom. Meaningful-progress/Happiness and Tiny anti-stagnation reward remain separate mechanics.
- **Exploration**: Tiny action movement persists for 1.8 game seconds after receiving the response (formerly about 0.7). The same real-game one-time action attribution and legal move mask remain.
- **Item distribution**: two seeded herb patches and two rune patches (up to 12 per patch), two thorn patches (up to 8 per patch), up to 4 scattered herbs, and up to 24 scattered dangerous thorns. Dispersed thorns cover all 16 broad zones in the tested default map. Items use dry/flat voxel ground checks and minimum spacing.
- **Threats**: 12 hostile monsters requested, 12 actually spawned on the tested default seed.
- In the tested Unity scene, 115 total pickup sites, 13 distributed NPCs, 12 monsters, 24 scattered thorns (in all 16 broad zones); scene generation can vary when seed or placement constraints change.

## How to train afresh

Start the API when not already running:

```powershell
cd T:\Space\Projects\ProjectsAI\AdventureTinyLLM
.\scripts\start_api.ps1 -EnableOnline
```

Then in `T:\Space\Projects\Games\AIAdventureRPG`, open `Assets/AIAdventureRPG/Scenes/AdventureValley.unity`, press **Play**, press **TAB** twice from Manual to Tiny, then select **Train in Game** on the HUD. **Evaluate** is the default and freezes weights. During training, accepted real-game outcomes create a *new* `checkpoints/tiny-unity-online.pt`, which is the new independent lineage. To verify progress, `GET http://127.0.0.1:8765/health` and inspect `training_steps`, `online_updates`, `checkpoint`.

Never restart an existing API expecting it to forget weights while a checkpoint exists: the startup script automatically selects an online checkpoint first. Stop the API and intentionally remove or relocate the checkpoint first if another fresh reset is desired.

## Boundaries

- This reset removed trained neural model files/optimizer states, not source code, documentation, other AI projects, or historical reports.
- Python tests and Unity EditMode tests pass, but no claim is made that an untrained policy has learned navigation or combat competence.
- No GitHub release, tag change or remote push was performed.
