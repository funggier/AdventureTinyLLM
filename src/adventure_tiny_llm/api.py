"""Loopback Tiny Transformer inference with explicit, one-time action credit.

An outcome may update weights only when it names an actual issued action ID.
IDs cannot be reused, cannot survive reset, and expire after a short window.
Random origin model, independent from Ollama and ML-Agents.
"""
from __future__ import annotations

from collections import OrderedDict, deque
from dataclasses import dataclass
from math import isfinite
import os
from pathlib import Path
from threading import Lock
from time import monotonic
from uuid import uuid4

import torch
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field, StrictBool

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
    if TRAINING_METADATA.get("metrics", {}).get("source") == "Unity observed outcomes":
        saved_payload = torch.load(CHECKPOINT_PATH, map_location="cpu", weights_only=True)
        ONLINE_OPTIMIZER.load_state_dict(saved_payload["optimizer_state"])
        PRIOR_ONLINE_UPDATES = int(TRAINING_METADATA["metrics"].get("online_updates", 0))
ONLINE_UPDATES = 0
OBSERVED_REWARDS: list[float] = []
# Text observations are explicitly NOT fed into the numeric policy or optimizer.
# Keep a bounded diagnostic record so Tiny can receive signs with zero reward.
TEXT_OBSERVATIONS: dict[str, deque[dict[str, str]]] = {}
HISTORIES: dict[str, deque[list[float]]] = {}
MAX_AGENTS = 64
MAX_DECISIONS_PER_AGENT = 48
ACTION_TTL_SECONDS = 20.0
LOCK = Lock()


@dataclass(frozen=True)
class Decision:
    history: torch.Tensor
    heads: tuple[int, int, int]
    timestamp: float
    move_mask: tuple[bool, ...] | None = None


DECISIONS: dict[str, OrderedDict[str, Decision]] = {}
app = FastAPI(title="AdventureTinyLLM", version="0.2.0", description="Tiny Transformer with explicit action attribution")


class ActRequest(BaseModel):
    agent_id: str = Field(min_length=1, max_length=100)
    observation: list[float]
    deterministic: bool = True
    # Optional physically available moves supplied by Unity at this location.
    # None preserves backward compatibility with older clients.
    valid_moves: list[StrictBool] | None = None


class ResetRequest(BaseModel):
    agent_id: str = Field(min_length=1, max_length=100)


class ObserveRequest(BaseModel):
    agent_id: str = Field(min_length=1, max_length=100)
    kind: str = Field(default="sign", pattern="^sign$")
    source: str = Field(min_length=1, max_length=120)
    text: str = Field(min_length=1, max_length=512)


class ExperienceRequest(BaseModel):
    agent_id: str = Field(min_length=1, max_length=100)
    action_id: str = Field(min_length=1, max_length=80)
    reward: float
    event: str = Field(default="game outcome", max_length=200)
    credit: str = Field(default="move", pattern="^(move|interact|answer)$")
    navigation_override: bool = False


def _prune(agent_id: str, now: float) -> None:
    pending = DECISIONS.setdefault(agent_id, OrderedDict())
    for key in tuple(pending):
        if now - pending[key].timestamp > ACTION_TTL_SECONDS:
            del pending[key]
    while len(pending) >= MAX_DECISIONS_PER_AGENT:
        pending.popitem(last=False)


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
        "text_observations": sum(len(records) for records in TEXT_OBSERVATIONS.values()),
        "checkpoint": CHECKPOINT_PATH or None,
        "action_protocol": "explicit-action-id-v2",
        "action_ttl_seconds": ACTION_TTL_SECONDS,
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
    # Validate the mask before mutating a temporal history or issuing an ID.
    mask = req.valid_moves
    if mask is not None and (
        len(mask) != CONFIG.num_move_actions
        or any(type(allowed) is not bool for allowed in mask)
        or not any(mask)
    ):
        raise HTTPException(status_code=422, detail="valid_moves must contain nine booleans and at least one True")
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
            if mask is not None:
                legal = torch.tensor(mask, dtype=torch.bool, device=move.device).unsqueeze(0)
                move = move.masked_fill(~legal, -1e9)

            def choose(logits: torch.Tensor) -> int:
                if req.deterministic:
                    return int(logits.argmax(-1).item())
                return int(torch.distributions.Categorical(logits=logits).sample().item())

            indices = choose(move), choose(interact), choose(answer)
            response = decode_action(*indices, value=float(value.item())).to_dict()
        action_id = uuid4().hex
        _prune(req.agent_id, monotonic())
        DECISIONS[req.agent_id][action_id] = Decision(x.detach().clone(), indices, monotonic(),
                                                       tuple(mask) if mask is not None else None)
    response["action_id"] = action_id
    response["model_trained"] = bool(TRAINING_METADATA or ONLINE_UPDATES > 0)
    return response


@app.post("/observe")
def observe(req: ObserveRequest) -> dict:
    # Information intake != semantic analysis or positive training reward.
    # Does not consume action IDs, alter weights, or call the optimizer.
    with LOCK:
        if req.agent_id not in TEXT_OBSERVATIONS and len(TEXT_OBSERVATIONS) >= MAX_AGENTS:
            raise HTTPException(status_code=429, detail="agent observation capacity reached")
        records = TEXT_OBSERVATIONS.setdefault(req.agent_id, deque(maxlen=24))
        records.append({"kind": req.kind, "source": req.source, "text": req.text})
        return {"accepted": True, "kind": req.kind, "reward": 0.0,
                "model_updated": False, "observed_count": len(records)}


@app.post("/experience")
def experience(req: ExperienceRequest) -> dict:
    global ONLINE_UPDATES
    if not ONLINE_ENABLED or ONLINE_OPTIMIZER is None:
        raise HTTPException(status_code=403, detail="Online updates disabled; set ADVENTURE_TINY_ONLINE_TRAIN=1")
    if not isfinite(req.reward):
        raise HTTPException(status_code=422, detail="reward must be finite")
    with LOCK:
        pending = DECISIONS.get(req.agent_id)
        decision = pending.pop(req.action_id, None) if pending is not None else None
        if decision is None:
            return {"accepted": False, "reason": "unknown_or_consumed_action_id",
                    "online_updates": PRIOR_ONLINE_UPDATES + ONLINE_UPDATES}
        if monotonic() - decision.timestamp > ACTION_TTL_SECONDS:
            return {"accepted": False, "reason": "expired_action_id",
                    "online_updates": PRIOR_ONLINE_UPDATES + ONLINE_UPDATES}
        if req.navigation_override:
            return {"accepted": False, "reason": "navigation_intervention",
                    "online_updates": PRIOR_ONLINE_UPDATES + ONLINE_UPDATES}
        loss = update_from_outcome(MODEL, ONLINE_OPTIMIZER, decision.history,
                                   decision.heads, req.reward, credit=req.credit,
                                   move_mask=decision.move_mask)
        ONLINE_UPDATES += 1
        OBSERVED_REWARDS.append(req.reward)
        if len(OBSERVED_REWARDS) > 1000:
            del OBSERVED_REWARDS[:100]
        save(ONLINE_OUTPUT, MODEL, ONLINE_OPTIMIZER,
             steps=BASE_STEPS + ONLINE_UPDATES,
             seed=CONFIG.seed,
             metrics={"source": "Unity observed outcomes",
                      "online_updates": PRIOR_ONLINE_UPDATES + ONLINE_UPDATES,
                      "attribution": "explicit-action-id-v2",
                      "mean_recent_event_reward": sum(OBSERVED_REWARDS) / len(OBSERVED_REWARDS)})
        return {"accepted": True, "action_id": req.action_id,
                "online_updates": PRIOR_ONLINE_UPDATES + ONLINE_UPDATES,
                "loss": loss, "reward": req.reward}


@app.post("/reset")
def reset(req: ResetRequest) -> dict:
    with LOCK:
        existed = HISTORIES.pop(req.agent_id, None) is not None
        DECISIONS.pop(req.agent_id, None)
        TEXT_OBSERVATIONS.pop(req.agent_id, None)
    return {"agent_id": req.agent_id, "reset": existed}
