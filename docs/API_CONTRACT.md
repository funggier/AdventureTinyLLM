# REST API (initial untrained inference)

Default: `http://127.0.0.1:8765`. This development server is not authenticated; **bind to loopback only**.

## GET /health

Returns service/architecture metadata, `trained: false`, `training_available: false`, and current parameter count if Torch has loaded.

## POST /act

Request:
```json
{
  "agent_id": "tiny-001",
  "observation": [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
  "deterministic": true
}
```

Response:
```json
{
  "move_x": 0.0,
  "move_z": 1.0,
  "face_x": 0.0,
  "face_z": 1.0,
  "interact": false,
  "answer": "none",
  "value": 0.0,
  "model_trained": false
}
```

Action fields are aligned with AIAdventureRPG's existing Ollama Action schema, but observation encoding is separate. This is a **random-initialized model**. Observations must be finite numeric values with exactly 32 entries. A rolling 32-step context is stored per `agent_id` in process memory only.

## POST /reset

Request: `{"agent_id":"tiny-001"}`. Clears that agent's temporal context. This is not a game reset or training operation.

## Planned, not exposed yet

`/experience`, `/train`, `/training/status`, `/checkpoint/save`, `/checkpoint/load`.

A reward observation must be a post-action event; no hidden world rule may be sent as a training label. API request versioning and authentication are required before network exposure or production use.
