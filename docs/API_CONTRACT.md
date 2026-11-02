# AdventureTinyLLM REST API — v0.2.0 Tactical

The service listens on **127.0.0.1:8765** with no authentication. **Do not expose it over LAN/WAN**. The integration example uses Unity 6000.6.7f1 and is in `examples/AIAdventureRPG`.

## Configuration

| Environment | Meaning |
|---|---|
| `ADVENTURE_TINY_CHECKPOINT` | Optional compatible v4/51-input `.pt` checkpoint to resume |
| `ADVENTURE_TINY_ONLINE_TRAIN=1` | Explicit opt-in to neural weight updates from Unity |
| `ADVENTURE_TINY_ONLINE_OUTPUT` | Destination for atomic online checkpoints (default `checkpoints/tiny-unity-online.pt`) |

Use `powershell -ExecutionPolicy Bypass -File .\scripts\start_api.ps1 -EnableOnline` in the Tiny source folder. The launcher checks for existing trained checkpoints and otherwise initializes a random policy.

## GET /health

Returns `status="ready"`, `observation_size=51`, `action_protocol="explicit-action-id-v5-tactical"`, `training_available`, `trained`, `training_steps`, `online_updates`, `parameters`, `ability_options`, `distance_options=[1,2,4,6,10]`, and limits. **A fresh model can be ready but untrained**.

## POST /act

Request:
```json
{
  "agent_id": "tiny-unity",
  "observation": [0.0, 0.0],
  "deterministic": false,
  "valid_moves": [true, true, true, true, true, true, true, true, true]
}
```
**Note:** The `observation` shown above is truncated for readability; a real request **must contain 51 finite float values**, ordered as in `src/adventure_tiny_llm/observation.py`. `valid_moves` is optional; if provided, it must be nine booleans with at least one `true`.

Response fields include `action_id` (unique, one-use ID), `move_x`, `move_z`, `move_distance` (0 if idle, otherwise 1/2/4/6/10 world units), `face_x`, `face_z`, `interact`, `answer` (none/good/bad), `ability` (none/special/arc/heal), `value`, and `model_trained`. The model samples its learned categorical distributions when `deterministic=false` and uses argmax when true.

Availability masks:
- Movement: optional nine-boolean mask is applied before sampling/argmax and stored with each action ID for consistent policy gradients.
- Ability: always derived from observation features 40, 41, 42 (can special, can arc, can heal); if an MP/HP condition fails, that ability is masked.
- Additional tactical observation features 43..50: in combat, low HP, low MP, closest sensed monster distance and bearing X/Z, monster winding up, distance to actually visible thorn.
- The model never receives hidden world results as observation features. The Unity `AdventureCombatState` determines the configurable 9-unit default detection ring and HP/MP thresholds.

## POST /experience

Request:
```json
{
  "agent_id": "tiny-unity",
  "action_id": "<actual_id_from_act>",
  "reward": 0.35,
  "credit": "move",
  "navigation_override": false,
  "event": "source=Self action=DodgeMonster Wild Boar"
}
```

`reward` must be finite. `credit` is one of `move | interact | answer | ability`. Server requires online updates enabled, a real unconsumed action ID issued to the same agent within **20 real seconds**, and no `navigation_override`. Each ID is consumed on processing; duplicate/expired/unknown IDs are rejected. At most 48 pending actions per agent; at most 64 agents have in-process context.

On acceptance, server runs one policy/value optimization step and saves the checkpoint. `credit=move` trains **both movement direction and movement distance heads** using the exact stored legal-action mask. `credit=ability` trains the separate ability head with its original MP/HP mask. `credit=interact` and `credit=answer` train their corresponding categorical heads. Loss combines actor-critic advantage and state-value regression; training is **not PPO** and does **not** prove learned combat competence.

The **reward itself comes from Unity** `AdventureRewardPolicy.FromOutcome`. See [complete Thai reward criteria and formulas](REWARD_POLICY_TH.md) and [tactical overview](TACTICAL_POLICY_TH.md). Reward amounts, critical 20% thresholds (configurable in Unity), examples, and edge cases are documented there.

`TRAIN TINY` sends experiences; `USE TRAINED` does inference only. Unity excludes ineligible/stale results and forced safety navigation from policy reward credit. Local Unity correlation uses a more restrictive **3 game second** bound for recently applied actions; this is not full multi-step causal credit assignment.

## POST /observe

Send informational observations that **do not train the neural policy**:
```json
{"agent_id":"tiny-unity","kind":"sign","source":"Safety board","text":"Beware wild boars"}
```
Returns `accepted=true`, `reward=0`, `model_updated=false`. The text is stored in a bounded observation buffer (24 records/agent) and is **not interpreted by the numeric Transformer**.

**Important:** `reward=0` displayed in this information response does **not** imply `/experience` was called. By contrast, an actual `/experience` with `reward=0` can still update model weights.

## POST /reset

`{"agent_id":"tiny-unity"}` drops that agent's in-memory observation history, sign buffer and outstanding action IDs; does **not** erase trained checkpoint weights or game stats.

The **RESET OLLAMA CONTEXT** button in the Unity HUD is **separate**: it clears the Ollama gameplay memory and stale prompt history while keeping Tiny model state untouched.

## Compatibility

This v4 model accepts **51 input features** and is not weight-compatible with earlier 32/40/43-feature checkpoints. The 43-feature checkpoint is archived separately and is **not shipped in this source/example release**. Use `GET /health` to verify schema/version before selecting a Unity AI mode.

## Limits and safety

Single-user local experimentation. No remote API authentication, no full MDP trajectory credit, no recurrent cross-episode RL policy, no checkpoint of Unity world state, and no independent Unity game bundle in the example archive. The Unity example requires the HMGF assemblies detailed in `examples/AIAdventureRPG/README.md`.
