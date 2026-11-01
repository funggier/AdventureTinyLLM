"""Package an exact committed snapshot into versioned Release and Document ZIPs.

Requires only Python 3.11+ stdlib and Git; never includes .venv or train outputs.
Run after committing. Archive bytes are retrieved from the exact HEAD tree.
"""
from __future__ import annotations

import argparse
from hashlib import sha256
from pathlib import Path
import subprocess
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

VERSION = "V0.1.0"
ZIP_TIMESTAMP = (2026, 11, 1, 0, 0, 0)


def git(root: Path, *args: str) -> bytes:
    return subprocess.run(
        ["git", "-C", str(root), *args],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    ).stdout


def make_zip(root: Path, destination: Path, members: list[str]) -> None:
    prefix = f"AdventureTinyLLM-{VERSION}/"
    with ZipFile(destination, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
        for name in members:
            raw = git(root, "show", f"HEAD:{name}")
            entry = ZipInfo(prefix + name, ZIP_TIMESTAMP)
            entry.compress_type = ZIP_DEFLATED
            entry.external_attr = (0o100644 << 16)
            archive.writestr(entry, raw, compress_type=ZIP_DEFLATED, compresslevel=9)


def package(root: Path, output: Path) -> list[Path]:
    if git(root, "status", "--porcelain").strip():
        raise RuntimeError("worktree must be clean; commit changes before packaging")
    paths = [
        name.decode("utf-8") for name in git(root, "ls-tree", "-r", "-z", "--name-only", "HEAD").split(b"\0") if name
    ]
    assert {"LICENSE", "README.md", "pyproject.toml"}.issubset(paths)
    docs = [p for p in paths if p in {"LICENSE", "README.md"} or p.startswith("docs/")]
    output.mkdir(parents=True, exist_ok=True)
    release = output / f"AdventureTinyLLM-{VERSION}-Release.zip"
    document = output / f"AdventureTinyLLM-{VERSION}-Document.zip"
    make_zip(root, release, paths)
    make_zip(root, document, docs)
    artifacts = [release, document]
    checksums = output / "SHA256SUMS.txt"
    checksums.write_text(
        "".join(f"{sha256(file.read_bytes()).hexdigest()}  {file.name}\n" for file in artifacts),
        encoding="ascii",
    )
    return [*artifacts, checksums]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="dist")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    output = Path(args.output)
    if not output.is_absolute():
        output = root / output
    for file in package(root, output):
        print(f"{file.name} ({file.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
