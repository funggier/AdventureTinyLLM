import pytest

torch = pytest.importorskip("torch")
from adventure_tiny_llm.config import TinyConfig
from adventure_tiny_llm.model import AdventureTransformer


def test_forward_heads_and_value():
    cfg = TinyConfig()
    torch.manual_seed(cfg.seed)
    model = AdventureTransformer(cfg)
    move, interact, answer, value = model(torch.randn(2, 5, cfg.observation_size))
    assert move.shape == (2, 9)
    assert interact.shape == (2, 2)
    assert answer.shape == (2, 3)
    assert value.shape == (2,)
    assert torch.isfinite(move).all()


def test_deterministic_reinitialization_and_gradient():
    cfg = TinyConfig()
    torch.manual_seed(cfg.seed)
    a = AdventureTransformer(cfg)
    torch.manual_seed(cfg.seed)
    b = AdventureTransformer(cfg)
    assert all(torch.equal(x, y) for x, y in zip(a.parameters(), b.parameters()))
    first = a(torch.randn(1, 3, cfg.observation_size))[0]
    first.sum().backward()
    assert a.input.weight.grad is not None
    assert torch.isfinite(a.input.weight.grad).all()
