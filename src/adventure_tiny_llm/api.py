"""Local Transformer inference, optionally updated by *actual* game outcomes.

ADVENTURE_TINY_CHECKPOINT loads a trained policy. Online updates are disabled
unless ADVENTURE_TINY_ONLINE_TRAIN=1; separate output file avoids modifying the
qualified offline checkpoint. No Ollama or ML-Agents dependencies.
"""
from __future__ import annotations

from collections import deque
from math import isfinite
import os
from pathlib import Path
from threading import Lock

import torch
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from .config import TinyConfig
from .contracts import decode_action, validate_observation
from .model import AdventureTransformer
from .checkpoint import load, save
from .training import update_from_outcome

CONFIG = TinyConfig().validate()
torch.manual_seed(CONFIG.seed)
MODEL = AdventureTransformer(CONFIG).eval()
TRAINING_METADATA: dict | None = None
CHECKPOINT_PATH = os.environ.get("ADVENTURE_TINY_CHECKPOINT", "")
if CHECKPOINT_PATH:
    MODEL, TRAINING_METADATA = load(CHECKPOINT_PATH)
    CONFIG = MODEL.config
ONLINE_ENABLED = os.environ.get("ADVENTURE_TINY_ONLINE_TRAIN") == "1"
ONLINE_OUTPUT = os.environ.get("ADVENTURE_TINY_ONLINE_OUTPUT", "checkpoints/tiny-unity-online.pt")
ONLINE_OPTIMIZER = torch.optim.AdamW(MODEL.parameters(), lr=2e-5, weight_decay=0) if ONLINE_ENABLED else None
BASE_STEPS = int(TRAINING_METADATA["optimizer_steps"]) if TRAINING_METADATA else 0
PRIOR_ONLINE_UPDATES = 0
if ONLINE_OPTIMIZER is not None and TRAINING_METADATA is not None:
    # Resume the exact optimizer momentum only from a previously saved Unity
    # online checkpoint. The synthetic-arena optimizer is intentionally separate.
    if TRAINING_METADATA.get("metrics", {}).get("source") == "Unity observed outcomes":
        saved_payload = torch.load(CHECKPOINT_PATH, map_location="cpu", weights_only=True)
        ONLINE_OPTIMIZER.load_state_dict(saved_payload["optimizer_state"])
        PRIOR_ONLINE_UPDATES = int(TRAINING_METADATA["metrics"].get("online_updates", 0))
ONLINE_UPDATES = 0
OBSERVED_REWARDS: list[float] = []
HISTORIES: dict[str, deque[list[float]]] = {}
DECISIONS: dict[str, tuple[torch.Tensor, tuple[int, int, int]]] = {}
MAX_AGENTS = 64
LOCK = Lock()
app = FastAPI(title="AdventureTinyLLM", version="0.2.0", description="Independent Tiny Transformer AI")


class ActRequest(BaseModel):
    agent_id: str = Field(min_length=1, max_length=100)
    observation: list[float]
    deterministic: bool = True


class ResetRequest(BaseModel):
    agent_id: str = Field(min_length=1, max_length=100)


class ExperienceRequest(BaseModel):
    agent_id: str = Field(min_length=1, max_length=100)
    reward: float
    event: str = Field(default="game outcome", max_length=200)


@app.get("/health")
def health() -> dict:
    return {
        "service": "AdventureTinyLLM",
        "status": "ready",
        "model": "Tiny-64-Transformer",
        "parameters": MODEL.parameter_count,
        "trained": bool(TRAINING_METADATA or ONLINE_UPDATES > 0),
        "training_available": ONLINE_ENABLED,
        "training_steps": BASE_STEPS + ONLINE_UPDATES,
        "online_updates": PRIOR_ONLINE_UPDATES + ONLINE_UPDATES,
        "observed_events": len(OBSERVED_REWARDS),
        "checkpoint": CHECKPOINT_PATH or None,
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
            indices = choose(move), choose(interact), choose(answer)
            response = decode_action(*indices, value=float(value.item())).to_dict()
        DECISIONS[req.agent_id] = (x.detach().clone(), indices)
    response["model_trained"] = bool(TRAINING_METADATA or ONLINE_UPDATES > 0)
    return response


@app.post("/experience")
def experience(req: ExperienceRequest) -> dict:
    global ONLINE_UPDATES
    if not ONLINE_ENABLED or ONLINE_OPTIMIZER is None:
        raise HTTPException(status_code=403, detail="Online updates disabled; set ADVENTURE_TINY_ONLINE_TRAIN=1")
    if not isfinite(req.reward):
        raise HTTPException(status_code=422, detail="reward must be finite")
    with LOCK:
        decision = DECISIONS.pop(req.agent_id, None)
        if decision is None:
            return {"accepted": False, "reason": "no prior action", "online_updates": PRIOR_ONLINE_UPDATES + ONLINE_UPDATES}
        x, chosen = decision
        loss = update_from_outcome(MODEL, ONLINE_OPTIMIZER, x, chosen, req.reward)
        ONLINE_UPDATES += 1
        OBSERVED_REWARDS.append(req.reward)
        if len(OBSERVED_REWARDS) > 1000:
            del OBSERVED_REWARDS[:100]
        # Save *every* observed Unity event, atomically, so closing the game
        # does not silently discard the last few learned updates.
        save(ONLINE_OUTPUT, MODEL, ONLINE_OPTIMIZER,
             steps=BASE_STEPS + ONLINE_UPDATES,
             seed=CONFIG.seed,
             metrics={"source": "Unity observed outcomes",
                      "online_updates": PRIOR_ONLINE_UPDATES + ONLINE_UPDATES,
                      "mean_recent_event_reward": sum(OBSERVED_REWARDS) / len(OBSERVED_REWARDS)})
        return {"accepted": True, "online_updates": PRIOR_ONLINE_UPDATES + ONLINE_UPDATES,
                "loss": loss, "reward": req.reward}


@app.post("/reset")
def reset(req: ResetRequest) -> dict:
    with LOCK:
        existed = HISTORIES.pop(req.agent_id, None) is not None
        DECISIONS.pop(req.agent_id, None)
    return {"agent_id": req.agent_id, "reset": existed}
