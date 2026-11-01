import pytest
torch = pytest.importorskip("torch")
from adventure_tiny_llm.observation import FEATURES, pack
from adventure_tiny_llm.training import reward_for_action, train, evaluate, update_from_outcome
from adventure_tiny_llm.checkpoint import load

def test_observation_schema():
    assert len(FEATURES) == 32
    values = pack(seen_herb=1, constant_one=1)
    assert values[10] == 1
    assert values[31] == 1

def test_rewards_are_post_action_and_signed():
    assert reward_for_action(1, 0, 1, True) > 0
    assert reward_for_action(1, 0, 1, False) < 0

def test_train_changes_weights_and_roundtrip(tmp_path):
    path = tmp_path / "mini.pt"
    metrics = train(steps=3, batch_size=4, output=str(path), threads=1)
    assert metrics["weight_delta_max"] > 0
    model, meta = load(path)
    assert meta["trained"] is True and meta["optimizer_steps"] == 3
    assert meta["initialization"] == "random"
    assert evaluate(model, seed=42, samples=8)["samples"] == 8


def test_live_external_reward_changes_weights():
    from adventure_tiny_llm.model import AdventureTransformer
    from adventure_tiny_llm.config import TinyConfig
    torch.manual_seed(22)
    net=AdventureTransformer(TinyConfig())
    optim=torch.optim.AdamW(net.parameters(),lr=.0001)
    before=net.input.weight.detach().clone()
    x=torch.zeros((1,2,32));x[...,31]=1
    loss=update_from_outcome(net,optim,x,(1,0,0),.26)
    assert torch.isfinite(torch.tensor(loss))
    assert not torch.equal(before,net.input.weight.detach())
