# Architecture

```text
Unity AIAdventureRPG (shared world)
    | observations: position, self-stats, visible objects, current event
    | action results and post-event deltas
    |
    +-- Human controller                   (independent)
    +-- Ollama controller via Ollama API    (independent)
    +-- Tiny controller via REST /act      (separate future Unity adapter)
             |
             v
      AdventureTinyLLM service
             |
     observation contract
             |
     numeric feature encoder (Unity-side planned)
             |
     random-init tiny causal Transformer
       RMSNorm + RoPE + GQA + SwiGLU
             |
       policy heads + value head
             |
         action JSON
             |
         Unity actor
             |
         real observed result
             |
      Native RL trainer (PLANNED)
        policy gradient / PPO experiments
        checkpoint & evaluation
```

## Module boundaries

- `config.py`: deterministic, validated model shape/seed.
- `contracts.py`: typed numeric observation and action contract.
- `model.py`: compact decoder-only temporal Transformer with factorized policy heads and scalar value head.
- `api.py`: loopback REST inference; per-agent short context buffers, no training claims.
- `trainer/` (planned): rewards, rollout store, PPO/other trainer, checkpoint provenance.
- `memory/` (planned): ASCA-inspired episodic/familiarity/surprise modules; no access to Ollama's memory.
- `adapters/` (planned): optional Unity/ML-Agents integration.

## Non-goals of the initial commit

- No language pretraining, pretrained weights, or integrated tokenizer.
- No autonomous learning until a real trainer is implemented and verified.
- No dependency on ML-Agents.
- No embedding reuse from FlyWireLLM or knowledge leakage from ProjectASCA.
- No claim that a Transformer itself implies intelligence.

## Research safeguards

Keep deterministic seeds, frozen train/evaluation maps, independent evaluation runs without weight updates, separate per-agent histories, explicit logs showing actions and *observed* outcomes, and matched-step baseline comparisons. Do not use test maps for gradient updates.
