"""Legal-action masks prevent Unity wall traps without policy-gradient credit drift."""
import pytest
torch = pytest.importorskip("torch")
from fastapi.testclient import TestClient
from adventure_tiny_llm import api

def test_corner_deterministic_and_stochastic_actions_never_cross_world_bounds():
    client = TestClient(api.app)
    # Unity at (62,2): legal directions are north, west, north-west.
    corner = [False, True, False, True, False, True, False, False, False]
    for deterministic in (True, False):
        for _ in range(16):
            req = {"agent_id": "corner-mask", "observation": [0.0] * 32,
                   "deterministic": deterministic, "valid_moves": corner}
            response = client.post("/act", json=req)
            assert response.status_code == 200, response.text
            data = response.json()
            assert (data["move_x"], data["move_z"]) in {
                (0.0, 1.0), (-1.0, 0.0), (-0.7071, 0.7071)}
            decision = api.DECISIONS["corner-mask"][data["action_id"]]
            assert decision.move_mask == tuple(corner)
            assert corner[decision.heads[0]]
    client.post("/reset", json={"agent_id":"corner-mask"})

def test_invalid_mask_is_rejected_without_creating_history():
    client = TestClient(api.app)
    req = {"agent_id":"bad-mask", "observation":[0.0]*32}
    for mask in ([], [False]*9, [True]*8, [True]*10, [1]*9):
        response = client.post("/act", json={**req, "valid_moves":mask})
        assert response.status_code == 422
    assert "bad-mask" not in api.HISTORIES

def test_masked_move_receives_on_policy_gradient_and_checkpoint(tmp_path, monkeypatch):
    client = TestClient(api.app)
    monkeypatch.setattr(api, "ONLINE_ENABLED", True)
    monkeypatch.setattr(api, "ONLINE_OPTIMIZER", torch.optim.AdamW(api.MODEL.parameters(), lr=1e-5))
    monkeypatch.setattr(api, "ONLINE_OUTPUT", str(tmp_path / "masked.pt"))
    corner = [False, True, False, True, False, True, False, False, False]
    act = client.post("/act", json={"agent_id":"masked-credit",
        "observation":[.1]*32,"valid_moves":corner,"deterministic":False}).json()
    idx = api.DECISIONS["masked-credit"][act["action_id"]].heads[0]
    assert corner[idx]
    reward = client.post("/experience", json={"agent_id":"masked-credit",
        "action_id":act["action_id"],"credit":"move","reward":-.03,
        "event":"source=Self action=PassiveNoProgress"})
    assert reward.status_code==200, reward.text
    assert reward.json()["accepted"] is True
    assert (tmp_path / "masked.pt").exists()
    replay = client.post("/experience",json={"agent_id":"masked-credit",
        "action_id":act["action_id"],"credit":"move","reward":1})
    assert replay.json()["reason"] == "unknown_or_consumed_action_id"
    client.post("/reset", json={"agent_id":"masked-credit"})

def test_old_clients_remain_compatible_without_mask():
    client = TestClient(api.app)
    resp=client.post("/act",json={"agent_id":"legacy-moves","observation":[0.0]*32})
    assert resp.status_code==200
    assert api.DECISIONS["legacy-moves"][resp.json()["action_id"]].move_mask is None
    client.post("/reset",json={"agent_id":"legacy-moves"})
