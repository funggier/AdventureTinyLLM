import math
import pytest

from adventure_tiny_llm.contracts import MOVEMENTS, decode_action, validate_observation


def test_numeric_observation():
    assert len(validate_observation([0.0]*51)) == 51
    with pytest.raises(ValueError):
        validate_observation([0.0]*31)
    with pytest.raises(ValueError):
        validate_observation([0.0]*31 + [math.inf])
    with pytest.raises(ValueError):
        validate_observation([False] + [0.0]*31)


def test_actions_match_unity_shape():
    action = decode_action(1, 1, 2).to_dict()
    assert action["move_z"] == 1.0
    assert action["face_z"] == 1.0
    assert action["interact"] is True
    assert action["answer"] == "bad"
    assert action["model_trained"] is False
    assert action["move_distance"] == 1.0
    assert len(MOVEMENTS) == 9
