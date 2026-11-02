"""Magic skills: legal MP masking and independent trainable ability decisions."""
import pytest
import torch
from fastapi.testclient import TestClient
from adventure_tiny_llm.api import app
from adventure_tiny_llm.config import TinyConfig
from adventure_tiny_llm.contracts import ABILITIES, decode_action
from adventure_tiny_llm.model import AdventureTransformer
from adventure_tiny_llm.training import update_from_outcome

def test_ability_contract():
    assert ABILITIES == ("none", "special", "arc", "heal")
    assert decode_action(0, 0, 0, ability=1).ability == "special"
    assert decode_action(0, 0, 0, ability=2).ability == "arc"
    assert decode_action(0, 0, 0, ability=3).ability == "heal"
    with pytest.raises(ValueError):
        decode_action(0, 0, 0, ability=4)

def test_no_mp_forces_no_ability_even_for_stochastic_sampling():
    client=TestClient(app)
    for _ in range(12):
        result=client.post("/act",json={"agent_id":"no-mp-test",
            "observation":[0.0]*51,"deterministic":False})
        assert result.status_code==200
        assert result.json()["ability"]=="none"
    client.post("/reset",json={"agent_id":"no-mp-test"})

def test_ability_head_receives_actual_experience_gradient():
    torch.manual_seed(101)
    model=AdventureTransformer(TinyConfig())
    optim=torch.optim.AdamW(model.parameters(),lr=.0005)
    before=model.ability_head.weight.detach().clone()
    obs=torch.zeros((1,2,51))
    obs[...,31]=1
    obs[...,40:43]=1
    loss=update_from_outcome(model,optim,obs,(0,0,0,0,1),.4,
                             credit="ability",ability_mask=(True,True,True,True))
    assert isinstance(loss,float)
    assert not torch.equal(before,model.ability_head.weight)
    with pytest.raises(ValueError):
        update_from_outcome(model,optim,obs,(0,0,0,0,1),.4,
                            credit="ability",ability_mask=(True,False,False,False))
