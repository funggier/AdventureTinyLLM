"""Finite-distance action selection must be a genuine trainable neural head."""
import pytest

torch = pytest.importorskip("torch")

from fastapi.testclient import TestClient
from adventure_tiny_llm.api import app
from adventure_tiny_llm.config import TinyConfig
from adventure_tiny_llm.contracts import DISTANCES, decode_action
from adventure_tiny_llm.model import AdventureTransformer
from adventure_tiny_llm.training import update_from_outcome


def test_distance_actions_are_finite_and_idle_is_zero():
    assert DISTANCES == (1.0, 2.0, 4.0, 6.0, 10.0)
    for i, d in enumerate(DISTANCES):
        assert decode_action(4, 0, 0, i).move_distance == d
    assert decode_action(0, 0, 0, 4).move_distance == 0.0
    with pytest.raises(ValueError):
        decode_action(4, 0, 0, 5)


def test_act_returns_finite_move_without_training():
    client = TestClient(app)
    state = client.get("/health").json()
    assert state["observation_size"] == 56
    assert state["action_protocol"] == "explicit-action-id-v6-spatial"
    response = client.post("/act", json={
        "agent_id": "test-distance", "observation": [0.0] * 56})
    assert response.status_code == 200
    action = response.json()
    assert action["move_distance"] in (0.0,) + DISTANCES
    assert (action["move_x"], action["move_z"]) != (0.0, 0.0) or action["move_distance"] == 0.0
    client.post("/reset", json={"agent_id": "test-distance"})


def test_real_move_reward_updates_direction_and_distance_head():
    torch.manual_seed(20261102)
    model = AdventureTransformer(TinyConfig())
    optimizer = torch.optim.AdamW(model.parameters(), lr=0.0005)
    before_direction = model.move_head.weight.detach().clone()
    before_distance = model.distance_head.weight.detach().clone()
    observation = torch.zeros((1, 2, 56))
    observation[..., 31] = 1.0
    loss = update_from_outcome(
        model, optimizer, observation, (1, 0, 0, 3, 0), -0.8, credit="move")
    assert isinstance(loss, float)
    assert not torch.equal(before_direction, model.move_head.weight)
    assert not torch.equal(before_distance, model.distance_head.weight)
