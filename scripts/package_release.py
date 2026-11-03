"""Package exact committed AdventureTinyLLM v0.3.0 source, docs, Unity example.

Source is extracted from HEAD, not from potentially unsaved/dirty files.
Never includes checkpoints, Unity Library/Temp, Python virtual env, or logs.
Output files are ZIP archives with deterministic member ordering and timestamp.
"""
from __future__ import annotations

import argparse
from hashlib import sha256
from pathlib import Path
import subprocess
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

VERSION = "V0.3.0"
ZIP_TIMESTAMP = (2026, 11, 3, 0, 0, 0)


def git(root: Path, *args: str) -> bytes:
    return subprocess.run(
        ["git", "-C", str(root), *args],
        check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE
    ).stdout


def make_zip(root: Path, destination: Path, members: list[str]) -> None:
    prefix = f"AdventureTinyLLM-{VERSION}/"
    with ZipFile(destination, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
        for name in sorted(set(members)):
            data = git(root, "show", f"HEAD:{name}")
            info = ZipInfo(prefix + name, ZIP_TIMESTAMP)
            info.compress_type = ZIP_DEFLATED
            info.external_attr = (0o100644 << 16)
            archive.writestr(info, data, compress_type=ZIP_DEFLATED, compresslevel=9)


def package(root: Path, output: Path) -> list[Path]:
    if git(root, "status", "--porcelain").strip():
        raise RuntimeError("Worktree must be clean; commit scoped changes first. Never auto-reset.")
    paths = [
        v.decode("utf-8")
        for v in git(root, "ls-tree", "-r", "-z", "--name-only", "HEAD").split(b"\0")
        if v
    ]
    assert {"LICENSE", "README.md", "pyproject.toml",
            "docs/REWARD_POLICY_TH.md", "docs/SPATIAL_MEMORY_TH.md",
            "docs/releases/v0.3.0.md",
            "examples/AIAdventureRPG/README.md"}.issubset(paths)
    example = [p for p in paths if p.startswith("examples/AIAdventureRPG/")]
    unity_sources = [p for p in example if p.startswith(
        "examples/AIAdventureRPG/Assets/AIAdventureRPG/")]
    if len(unity_sources) < 90:
        raise RuntimeError("Unity example must include the complete module and Unity .meta files")
    docs = [p for p in paths if p in ("README.md", "LICENSE") or
            p.startswith("docs/")]
    docs += ["examples/AIAdventureRPG/README.md"]
    main = [p for p in paths if not p.startswith("examples/")]
    output.mkdir(parents=True, exist_ok=True)
    names_and_members = [
        (f"AdventureTinyLLM-{VERSION}-Release.zip", main),
        (f"AdventureTinyLLM-{VERSION}-Document.zip", docs),
        (f"AdventureTinyLLM-{VERSION}-Unity-Example.zip", example),
    ]
    artifacts: list[Path] = []
    for filename, members in names_and_members:
        target = output / filename
        make_zip(root, target, members)
        artifacts.append(target)
    checksums = output / "SHA256SUMS.txt"
    checksums.write_text(
        "".join(f"{sha256(file.read_bytes()).hexdigest()}  {file.name}\n"
                for file in artifacts),
        encoding="ascii")
    return [*artifacts, checksums]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="dist")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    output = Path(args.output)
    if not output.is_absolute():
        output = root / output
    for path in package(root, output):
        print(f"{path.name} ({path.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
