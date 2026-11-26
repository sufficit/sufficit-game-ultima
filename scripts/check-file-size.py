#!/usr/bin/env python3
"""Enforce the physical line limit for repository text files."""

import json
from pathlib import Path
import subprocess
import sys

LINE_LIMIT = 400
EXCEPTIONS = Path("scripts/file-size-exceptions.json")


def main() -> int:
    root = Path(subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip())
    files = subprocess.check_output(
        ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"], cwd=root
    ).split(b"\0")
    exceptions = json.loads((root / EXCEPTIONS).read_text(encoding="utf-8"))
    if not isinstance(exceptions, dict):
        raise ValueError("Expected an object keyed by repository-relative file paths.")
    for name, entry in exceptions.items():
        if (
            not isinstance(entry, dict)
            or type(entry.get("maxLines")) is not int
            or entry["maxLines"] <= LINE_LIMIT
            or not isinstance(entry.get("reason"), str)
            or not entry["reason"].strip()
        ):
            raise ValueError(f"{name} needs a maxLines integer above {LINE_LIMIT} and a reason.")

    failures = []
    active = set()
    for raw_name in sorted(set(files) - {b""}):
        name = raw_name.decode("utf-8", errors="surrogateescape")
        path = root / name
        # A deletion in the worktree, gitlink, or symlink has no file content to check.
        if path.is_symlink() or not path.is_file():
            continue
        data = path.read_bytes()
        # Git's binary heuristic uses NUL bytes. Scan the entire file, not a prefix.
        if b"\0" in data:
            continue
        lines = data.count(b"\n") + int(bool(data) and not data.endswith(b"\n"))
        if lines > LINE_LIMIT:
            active.add(name)
            ceiling = exceptions[name]["maxLines"] if name in exceptions else LINE_LIMIT
            if lines > ceiling:
                failures.append(f"{name}: {lines} lines exceed the limit of {ceiling}.")

    for name in sorted(exceptions.keys() - active):
        failures.append(f"{name}: remove the unused file-size exception.")
    if failures:
        print("\n".join(failures), file=sys.stderr)
        return 1

    print(f"File size check passed. {len(active)} explicit exceptions to the {LINE_LIMIT}-line limit.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        sys.exit(f"File size check could not run: {error}")
