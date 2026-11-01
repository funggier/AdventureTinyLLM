"""Local prototype inference API. This model is intentionally UNTRAINED."""
from __future__ import annotations

from collections import deque
from math import isfinite
from threading import Lock

import torch
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from .config import TinyConfig
from .contracts import decode_action, validate_observation
from .model import AdventureTransformer

CONFIG = TinyConfig().validate()
torch.manual_seed(CONFIG.seed)
MODEL = AdventureTransformer(CONFIG).eval()
HISTORIES: dict[str, deque[list[float]]] = {}
MAX_AGENTS = 64
LOCK = Lock()
app = FastAPI(title="AdventureTinyLLM", version="0.1.0", description="Untrained tiny Transformer action API")


class ActRequest(BaseModel):
    agent_id: str = Field(min_length=1, max_length=100)
    observation: list[float]
    deterministic: bool = True


class ResetRequest(BaseModel):
    agent_id: str = Field(min_length=1, max_length=100)


@app.get("/health")
def health() -> dict:
    return {
        "service": "AdventureTinyLLM",
        "status": "ready",
        "model": "Tiny-64-Transformer",
        "parameters": MODEL.parameter_count,
        "trained": False,
        "training_available": False,
        "seed": CONFIG.seed,
        "observation_size": CONFIG.observation_size,
        "max_context": CONFIG.max_context,
    }


@app.post("/act")
def act(req: ActRequest) -> dict:
    try:
        observation = validate_observation(req.observation, CONFIG.observation_size)
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    # Lock guards per-agent memory and deterministic model sampling across requests.
    with LOCK:
        if req.agent_id not in HISTORIES:
            if len(HISTORIES) >= MAX_AGENTS:
                raise HTTPException(status_code=429, detail="agent context capacity reached")
            HISTORIES[req.agent_id] = deque(maxlen=CONFIG.max_context)
        history = HISTORIES[req.agent_id]
        history.append(observation)
        x = torch.tensor(list(history), dtype=torch.float32).unsqueeze(0)
        with torch.inference_mode():
            move, interact, answer, value = MODEL(x)

            def choose(logits: torch.Tensor) -> int:
                if req.deterministic:
                    return int(logits.argmax(-1).item())
                return int(torch.distributions.Categorical(logits=logits).sample().item())

            action = decode_action(choose(move), choose(interact), choose(answer), float(value.item()))
    return action.to_dict()


@app.post("/reset")
def reset(req: ResetRequest) -> dict:
    with LOCK:
        existed = HISTORIES.pop(req.agent_id, None) is not None
    return {"agent_id": req.agent_id, "reset": existed}
