"""Run a one-step random inference sanity check after pip install -e ."""
from adventure_tiny_llm.config import TinyConfig
from adventure_tiny_llm.contracts import decode_action
from adventure_tiny_llm.model import AdventureTransformer
import torch


def main() -> None:
    cfg = TinyConfig()
    torch.manual_seed(cfg.seed)
    model = AdventureTransformer(cfg).eval()
    observation = torch.zeros(1, 4, cfg.observation_size)
    with torch.inference_mode():
        move, interact, answer, value = model(observation)
    result = decode_action(
        int(move.argmax(-1)), int(interact.argmax(-1)), int(answer.argmax(-1)), float(value.item())
    )
    print(f"parameters={model.parameter_count}")
    print(f"action={result.to_dict()}")
    print("training_available=false")
    print("pretrained_weights=false")
    print("quality_claim=false")


if __name__ == "__main__":
    main()
