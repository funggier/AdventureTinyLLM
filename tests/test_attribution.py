"""Ensure only the actual, unconsumed model action gets credit."""
import pytest

torch = pytest.importorskip("torch")
from fastapi.testclient import TestClient
from adventure_tiny_llm import api

def test_explicit_action_id_replay_reset_and_agent_isolation(tmp_path, monkeypatch):
    client = TestClient(api.app)
    monkeypatch.setattr(api, "ONLINE_ENABLED", True)
    monkeypatch.setattr(api, "ONLINE_OPTIMIZER", torch.optim.AdamW(api.MODEL.parameters(), lr=1e-5))
    monkeypatch.setattr(api, "ONLINE_OUTPUT", str(tmp_path / "online.pt"))
    req = {"agent_id": "credit-01", "observation": [0.0]*51}
    first = client.post("/act", json=req).json()
    second = client.post("/act", json=req).json()
    assert first["action_id"] != second["action_id"]
    assert len(first["action_id"]) == 32
    wrong = client.post("/experience", json={"agent_id":"other", "action_id":first["action_id"],"reward":1})
    assert not wrong.json()["accepted"]
    result = client.post("/experience",json={
        "agent_id":"credit-01","action_id":first["action_id"],"reward":0.26,
        "credit":"move","event":"Collect herb"})
    assert result.status_code == 200 and result.json()["accepted"]
    assert (tmp_path / "online.pt").exists()
    assert client.post("/experience",json={
        "agent_id":"credit-01","action_id":first["action_id"],"reward":0.26
    }).json()["reason"] == "unknown_or_consumed_action_id"
    assert client.post("/experience",json={
        "agent_id":"credit-01","action_id":second["action_id"],"reward":.8,
        "navigation_override":True
    }).json()["reason"] == "navigation_intervention"
    third = client.post("/act", json=req).json()
    assert client.post("/reset",json={"agent_id":"credit-01"}).status_code == 200
    assert client.post("/experience",json={
        "agent_id":"credit-01","action_id":third["action_id"],"reward":1
    }).json()["reason"] == "unknown_or_consumed_action_id"

def test_negative_idle_boredom_reward_updates_only_issued_move_action(tmp_path, monkeypatch):
    """Stalling is a negative move outcome, not an invented or repeated credit."""
    client = TestClient(api.app)
    monkeypatch.setattr(api, "ONLINE_ENABLED", True)
    monkeypatch.setattr(api, "ONLINE_OPTIMIZER", torch.optim.AdamW(api.MODEL.parameters(), lr=1e-5))
    monkeypatch.setattr(api, "ONLINE_OUTPUT", str(tmp_path / "idle.pt"))
    request = {"agent_id": "idle-qa", "observation": [0.0] * 51, "deterministic": False}
    action = client.post("/act", json=request).json()
    payload = {"agent_id": "idle-qa", "action_id": action["action_id"],
               "credit": "move", "reward": -0.008,
               "event": "source=Self action=PassiveBoredom at applied_action_end"}
    result = client.post("/experience", json=payload)
    assert result.status_code == 200
    assert result.json()["accepted"] is True
    assert result.json()["reward"] < 0
    assert (tmp_path / "idle.pt").exists()
    replay = client.post("/experience", json=payload).json()
    assert replay["accepted"] is False
    assert replay["reason"] == "unknown_or_consumed_action_id"
    client.post("/reset", json={"agent_id": "idle-qa"})


def test_action_expiry_and_credit_validation(monkeypatch):
    client = TestClient(api.app)
    monkeypatch.setattr(api, "ONLINE_ENABLED", True)
    monkeypatch.setattr(api, "ONLINE_OPTIMIZER", torch.optim.AdamW(api.MODEL.parameters(), lr=1e-5))
    output = client.post("/act",json={
        "agent_id":"expire-02","observation":[0.0]*51
    }).json()
    action_id=output["action_id"]
    prior=api.DECISIONS["expire-02"][action_id]
    api.DECISIONS["expire-02"][action_id]=api.Decision(prior.history,prior.heads,api.monotonic()-100)
    assert client.post("/experience",json={
        "agent_id":"expire-02","action_id":action_id,"reward":1
    }).json()["reason"] == "expired_action_id"
    assert client.post("/experience",json={
        "agent_id":"expire-02","action_id":"fabricated","reward":1,"credit":"unknown"
    }).status_code == 422
