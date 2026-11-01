"""Native policy-gradient learning against observed outcomes in a tiny arena.

A deliberately *simplified* 2D arena is used for high-throughput discovery:
random nearest object (Herb/Thorn), random relative bearing and agent location.
The policy sees only the 32-feature observation; reward comes from the
environment *after* the action. No Ollama, demonstrations, or ML-Agents.
This is a curriculum/proof-of-learning, not a claim of Unity-level competence.
"""
from __future__ import annotations
import argparse
import json
import math
from pathlib import Path
import random
import torch
from torch.distributions import Categorical

from .config import TinyConfig
from .model import AdventureTransformer
from .observation import pack
from .checkpoint import save, load
from .contracts import MOVEMENTS


def sample_scenario(rng: random.Random) -> tuple[list[float], float, float, bool]:
    angle = rng.uniform(-math.pi, math.pi)
    dx, dz = math.cos(angle), math.sin(angle)
    herb = rng.random() < 0.5
    x, z = rng.uniform(.15, .85), rng.uniform(.15, .85)
    state = pack(
        x_norm=x, z_norm=z, y_norm=0.12, health=rng.uniform(.25, .95),
        facing_x=dx, facing_z=dz, seen_herb=float(herb), seen_thorn=float(not herb),
        nearest_bearing_x=dx, nearest_bearing_z=dz, nearest_distance_norm=.1,
        nearest_x_norm=max(0, min(1, x + dx * .1)),
        nearest_z_norm=max(0, min(1, z + dz * .1)), visible_count_norm=.1,
        constant_one=1,
    )
    return state, dx, dz, herb


def reward_for_action(action: int, bearing_x: float, bearing_z: float, herb: bool) -> float:
    move_x, move_z = MOVEMENTS[action]
    toward = move_x * bearing_x + move_z * bearing_z
    # Outcome-based shaping: net benefit from approaching herb / avoiding harm.
    # Reward is never included in the *input* observation.
    return (toward if herb else -toward) - .08 * (action == 0)


@torch.inference_mode()
def evaluate(model: AdventureTransformer, *, seed: int, samples: int = 256) -> dict:
    rng = random.Random(seed)
    model.eval()
    rewards = []
    for _ in range(samples):
        obs, dx, dz, herb = sample_scenario(rng)
        tensor = torch.tensor(obs, dtype=torch.float32).view(1, 1, -1)
        logits = model(tensor)[0]
        choice = int(logits.argmax(-1).item())
        rewards.append(reward_for_action(choice, dx, dz, herb))
    return {"mean_reward": sum(rewards) / len(rewards), "samples": samples, "seed": seed}


def update_from_outcome(
    model: AdventureTransformer,
    optimizer: torch.optim.Optimizer,
    observation_history: torch.Tensor,
    chosen_heads: tuple[int, int, int],
    reward: float,
    *,
    credit: str = "move",
) -> float:
    """One genuine on-policy-ish actor-critic update from an observed external reward.

    The environment reports the reward only after an action. We retain the
    last action's observation and action indices; no hidden rule table, Ollama
    labels or demonstration data are sent to the model.
    """
    if not math.isfinite(reward):
        raise ValueError("reward must be finite")
    model.train()
    move, interact, answer, value = model(observation_history)
    if credit not in ("move", "interact", "answer"):
        raise ValueError("unknown action head for credit")
    index = {"move": 0, "interact": 1, "answer": 2}[credit]
    logits = (move, interact, answer)[index]
    log_prob = Categorical(logits=logits).log_prob(
        torch.tensor([chosen_heads[index]], dtype=torch.long))
    target = torch.tensor([float(max(-2, min(2, reward)))], dtype=torch.float32)
    advantage = target - value.detach()
    loss = -(log_prob * advantage).mean() + .5 * (value - target).square().mean()
    optimizer.zero_grad(set_to_none=True)
    loss.backward()
    torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
    optimizer.step()
    model.eval()
    return float(loss.detach())


def train(*, steps: int = 180, batch_size: int = 32, seed: int = 1234, learning_rate: float = 0.001,
          output: str = "checkpoints/tiny-v1.pt", threads: int = 1) -> dict:
    if steps < 1 or batch_size < 1:
        raise ValueError("steps and batch_size must be positive")
    torch.set_num_threads(max(1, threads))
    rng = random.Random(seed)
    torch.manual_seed(seed)
    cfg = TinyConfig(seed=seed)
    model = AdventureTransformer(cfg)
    optimizer = torch.optim.AdamW(model.parameters(), lr=learning_rate, weight_decay=0.0)
    before = evaluate(model, seed=seed + 100000)
    weights_before = model.input.weight.detach().clone()
    model.train()
    for step in range(steps):
        scenarios = [sample_scenario(rng) for _ in range(batch_size)]
        inputs = torch.tensor([x[0] for x in scenarios], dtype=torch.float32).unsqueeze(1)
        move_logits, _, _, predicted_values = model(inputs)
        dist = Categorical(logits=move_logits)
        chosen = dist.sample()
        rewards = torch.tensor([
            reward_for_action(int(chosen[i].item()), s[1], s[2], s[3])
            for i, s in enumerate(scenarios)], dtype=torch.float32)
        # One-step advantage actor-critic: REINFORCE with learned state-value baseline.
        advantage = rewards - predicted_values.detach()
        policy_loss = -(dist.log_prob(chosen) * advantage).mean()
        value_loss = .5 * (predicted_values - rewards).square().mean()
        loss = policy_loss + .5 * value_loss - .005 * dist.entropy().mean()
        optimizer.zero_grad(set_to_none=True)
        loss.backward()
        torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
        optimizer.step()
    after = evaluate(model, seed=seed + 100000)
    max_delta = float((model.input.weight.detach() - weights_before).abs().max())
    metrics = {"eval_before": before, "eval_after": after, "weight_delta_max": max_delta,
               "steps": steps, "batch_size": batch_size, "algorithm": "one-step advantage actor-critic"}
    save(output, model, optimizer, steps=steps, seed=seed, metrics=metrics)
    return metrics


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--steps", type=int, default=180)
    parser.add_argument("--batch-size", type=int, default=32)
    parser.add_argument("--seed", type=int, default=1234)
    parser.add_argument("--output", default="checkpoints/tiny-v1.pt")
    args = parser.parse_args()
    print(json.dumps(train(steps=args.steps, batch_size=args.batch_size, seed=args.seed, output=args.output), indent=2))


if __name__ == "__main__":
    main()
