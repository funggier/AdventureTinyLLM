import pytest

from adventure_tiny_llm.config import TinyConfig


def test_tiny_config_roundtrip():
    cfg = TinyConfig().validate()
    assert TinyConfig.from_dict(cfg.to_dict()) == cfg


def test_invalid_geometry():
    with pytest.raises(ValueError):
        TinyConfig(n_heads=3).validate()
    with pytest.raises(ValueError):
        TinyConfig(schema_version=1).validate()


def test_action_schema_frozen():
    with pytest.raises(ValueError):
        TinyConfig(num_answer_actions=4).validate()
