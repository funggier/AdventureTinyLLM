"""Text observations may be received but never give learned reward or model updates."""
import pytest
torch = pytest.importorskip("torch")
from fastapi.testclient import TestClient
from adventure_tiny_llm import api


def test_sign_received_for_zero_reward_without_optimizer_change():
    client=TestClient(api.app)
    updates_before=api.ONLINE_UPDATES
    before=api.MODEL.input.weight.detach().clone()
    response=client.post("/observe",json={
        "agent_id":"reader-01","kind":"sign","source":"Camp safety board",
        "text":"Duskberry is poisonous. Amberberry restores health."
    })
    assert response.status_code==200
    payload=response.json()
    assert payload["accepted"] and payload["reward"]==0
    assert payload["model_updated"] is False
    assert payload["observed_count"]>=1
    assert api.ONLINE_UPDATES==updates_before
    assert torch.equal(api.MODEL.input.weight.detach(),before)
    assert api.TEXT_OBSERVATIONS["reader-01"][-1]["source"]=="Camp safety board"
    assert client.post("/reset",json={"agent_id":"reader-01"}).status_code==200
    assert "reader-01" not in api.TEXT_OBSERVATIONS

def test_observe_rejects_excessive_input_and_unknown_kind():
    client=TestClient(api.app)
    assert client.post("/observe",json={
        "agent_id":"reader-02","kind":"sign","source":"Long board","text":"x"*700
    }).status_code==422
    assert client.post("/observe",json={
        "agent_id":"reader-02","kind":"hidden_rule","source":"x","text":"poison"
    }).status_code==422
