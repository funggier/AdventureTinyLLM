"""API contract checks with the actual random-initialized Torch model."""
import pytest

pytest.importorskip("torch")
pytest.importorskip("fastapi")
from fastapi.testclient import TestClient

from adventure_tiny_llm.api import app


def test_health_untrained_contract():
    client = TestClient(app)
    payload = client.get("/health").json()
    assert payload["status"] == "ready"
    assert payload["trained"] is False
    assert payload["training_available"] is False
    assert payload["parameters"] == 95567


def test_act_and_reset_and_invalid_observation():
    client = TestClient(app)
    observation = [0.0] * 32
    response = client.post("/act", json={"agent_id": "test-agent", "observation": observation})
    assert response.status_code == 200
    output = response.json()
    assert set(output) == {"move_x", "move_z", "face_x", "face_z", "interact", "answer", "value", "model_trained"}
    assert output["model_trained"] is False
    assert output["answer"] in {"none", "good", "bad"}
    assert client.post("/reset", json={"agent_id": "test-agent"}).json()["reset"] is True
    assert client.post("/act", json={"agent_id": "bad-test", "observation": [0.0] * 31}).status_code == 422
