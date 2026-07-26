#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import sys
import tempfile


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


class ContainerSmokeError(RuntimeError):
    pass


def docker(*arguments: str, timeout: int = 120) -> str:
    completed = subprocess.run(
        ["docker", *arguments],
        check=False,
        capture_output=True,
        text=True,
        timeout=timeout,
    )
    if completed.returncode != 0:
        output = "\n".join(value for value in (completed.stdout, completed.stderr) if value)
        raise ContainerSmokeError(f"docker {' '.join(arguments)} failed:\n{output}")
    return completed.stdout.strip()


def inspect(image: str) -> dict[str, object]:
    result = json.loads(docker("image", "inspect", image))
    if not isinstance(result, list) or len(result) != 1 or not isinstance(result[0], dict):
        raise ContainerSmokeError(f"could not inspect exactly one image: {image}")
    return result[0]


def smoke(image: str, kind: str, version: str, revision: str) -> None:
    if kind != "viset":
        raise ContainerSmokeError(f"unsupported container kind: {kind}")
    inspected = inspect(image)
    config = inspected.get("Config")
    if not isinstance(config, dict):
        raise ContainerSmokeError("image has no configuration")
    user = config.get("User")
    if not isinstance(user, str) or not user or user in {"0", "root", "0:0"}:
        raise ContainerSmokeError(f"image does not have a non-root user: {user!r}")
    labels = config.get("Labels")
    if not isinstance(labels, dict):
        raise ContainerSmokeError("image has no OCI labels")
    expected_labels = {
        "org.opencontainers.image.source": "https://github.com/getviset/Viset",
        "org.opencontainers.image.version": version,
        "org.opencontainers.image.revision": revision,
    }
    for name, value in expected_labels.items():
        if labels.get(name) != value:
            raise ContainerSmokeError(f"OCI label {name} is not {value!r}")

    entrypoint = config.get("Entrypoint")
    if (
        not isinstance(entrypoint, list)
        or len(entrypoint) != 1
        or not isinstance(entrypoint[0], str)
        or not entrypoint[0].endswith("/bin/viset")
    ):
        raise ContainerSmokeError(f"unexpected entrypoint: {entrypoint!r}")
    if config.get("WorkingDir") != "/work":
        raise ContainerSmokeError(f"unexpected working directory: {config.get('WorkingDir')!r}")
    actual_version = docker("run", "--rm", image, "--version")
    if actual_version != f"viset {version}":
        raise ContainerSmokeError(f"unexpected container version output: {actual_version!r}")

    with tempfile.TemporaryDirectory(prefix="viset-container-smoke-") as temporary:
        directory = Path(temporary)
        directory.chmod(0o777)
        script = directory / "smoke.lua"
        script.write_text(
            """--[[
# viset
version = 1
output = "smoke.png"

[devices.desktop]

[devices.desktop.viewport]
width = 160
height = 120
]]

viset.page.navigate("data:text/html,<h1 style='color:%23126'>Viset</h1>")
viset.page.wait_for("document.readyState === 'complete'", "10s")
viset.snapshot()
""",
            encoding="utf-8",
            newline="\n",
        )
        script.chmod(0o644)
        output = directory / "output"
        output.mkdir()
        output.chmod(0o777)
        docker(
            "run",
            "--rm",
            "--env",
            "HOME=/tmp",
            "--volume",
            f"{directory}:/work",
            image,
            "capture",
            "/work/smoke.lua",
            "--output",
            "/work/output",
        )
        capture = output / "smoke.png"
        if not capture.is_file() or not capture.read_bytes().startswith(PNG_SIGNATURE):
            raise ContainerSmokeError("minimal container capture did not produce a PNG")


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Inspect and capture-smoke a Viset Nix image.")
    parser.add_argument("--image", required=True)
    parser.add_argument("--kind", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--revision", required=True)
    args = parser.parse_args(arguments)
    try:
        smoke(args.image, args.kind, args.version, args.revision)
    except (ContainerSmokeError, OSError, subprocess.SubprocessError, UnicodeError) as error:
        print(f"container-smoke: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
