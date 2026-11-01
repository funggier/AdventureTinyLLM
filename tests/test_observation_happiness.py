"""The 32-feature Unity policy input migrates from Relationship to Happiness."""
from adventure_tiny_llm.observation import FEATURES, pack

def test_happiness_slots_preserve_32_numeric_feature_contract():
    assert len(FEATURES)==32
    assert FEATURES[5]=="happiness"
    assert FEATURES[22]=="observed_happiness_delta"
    obs=pack(happiness=0.65, observed_happiness_delta=-0.03)
    assert len(obs)==32
    assert obs[5]==0.65
    assert obs[22]==-0.03
