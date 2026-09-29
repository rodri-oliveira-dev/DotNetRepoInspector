#!/usr/bin/env python3
from __future__ import annotations

import pathlib
import subprocess
import tempfile
import textwrap


REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
HELPER = REPO_ROOT / ".github" / "scripts" / "invoke_mcp_publisher_validation.ps1"


def make_publisher(root: pathlib.Path, mode: str) -> tuple[pathlib.Path, pathlib.Path]:
    state = root / f"{mode}.count"
    publisher = root / f"{mode}-publisher.py"

    body = f"""#!/usr/bin/env python3
import pathlib
import sys

state = pathlib.Path({str(state)!r})
count = int(state.read_text() or "0") if state.exists() else 0
count += 1
state.write_text(str(count))

mode = {mode!r}
if mode == "transient-then-success":
    if count < 3:
        print(
            'Error: validation failed: error sending request: Post '
            '"https://registry.modelcontextprotocol.io/v0/validate": '
            'dial tcp 127.0.0.1:443: connect: connection refused',
            file=sys.stderr,
        )
        raise SystemExit(1)
    print("Validation successful.")
    raise SystemExit(0)

if mode == "persistent-transient":
    print(
        'Error: validation failed: error sending request: Post '
        '"https://registry.modelcontextprotocol.io/v0/validate": '
        'dial tcp 127.0.0.1:443: connect: connection refused',
        file=sys.stderr,
    )
    raise SystemExit(1)

if mode == "semantic-failure":
    print(
        "Error: validation failed: package identifier does not match the manifest schema",
        file=sys.stderr,
    )
    raise SystemExit(1)

raise SystemExit("unsupported fake publisher mode")
"""

    publisher.write_text(textwrap.dedent(body), encoding="utf-8")
    publisher.chmod(0o755)
    return publisher, state


def run_case(
    root: pathlib.Path,
    manifest: pathlib.Path,
    mode: str,
    expected_exit_code: int,
    expected_attempts: int,
) -> None:
    publisher, state = make_publisher(root, mode)

    completed = subprocess.run(
        [
            "pwsh",
            "-NoProfile",
            "-File",
            str(HELPER),
            "-PublisherPath",
            str(publisher),
            "-ManifestPath",
            str(manifest),
            "-MaxAttempts",
            "3",
            "-InitialDelaySeconds",
            "0",
            "-MaxDelaySeconds",
            "0",
        ],
        check=False,
    )

    if completed.returncode != expected_exit_code:
        raise SystemExit(
            f"{mode}: expected exit code {expected_exit_code}, actual {completed.returncode}"
        )

    actual_attempts = int(state.read_text(encoding="utf-8"))
    if actual_attempts != expected_attempts:
        raise SystemExit(
            f"{mode}: expected {expected_attempts} attempts, actual {actual_attempts}"
        )


def main() -> None:
    with tempfile.TemporaryDirectory(prefix="mcp-publisher-resilience-") as temp:
        root = pathlib.Path(temp)
        manifest = root / "server.json"
        manifest.write_text("{}", encoding="utf-8")

        run_case(root, manifest, "transient-then-success", 0, 3)
        run_case(root, manifest, "persistent-transient", 0, 3)
        run_case(root, manifest, "semantic-failure", 1, 1)

    print("MCP Registry resilience policy tests passed.")


if __name__ == "__main__":
    main()
