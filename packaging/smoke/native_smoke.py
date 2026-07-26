#!/usr/bin/env python3

from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


SIDECARS = {
    "linux": ("libsharpyuv.so", "libwebp.so", "libwebpmux.so"),
    "darwin": ("libsharpyuv.dylib", "libwebp.dylib", "libwebpmux.dylib"),
    "win32": ("libsharpyuv.dll", "libwebp.dll", "libwebpmux.dll"),
}
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
ROOT = Path(__file__).resolve().parents[2]


class NativeSmokeError(RuntimeError):
    pass


def browser_selection() -> tuple[Path | None, str] | None:
    configured = os.environ.get("VISET_BROWSER")
    if configured is not None and configured.strip():
        return None, f"VISET_BROWSER={configured}"

    if sys.platform.startswith("linux"):
        names = (
            "google-chrome",
            "google-chrome-stable",
            "chromium",
            "chromium-browser",
            "microsoft-edge",
            "microsoft-edge-stable",
        )
        paths: tuple[Path, ...] = ()
    elif sys.platform == "darwin":
        names = ("google-chrome", "chromium")
        paths = (
            Path("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"),
            Path(
                "/Applications/Google Chrome for Testing.app/Contents/MacOS/"
                "Google Chrome for Testing"
            ),
            Path("/Applications/Chromium.app/Contents/MacOS/Chromium"),
            Path("/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"),
        )
    elif sys.platform == "win32":
        names = ("chrome.exe", "chromium.exe", "msedge.exe")
        candidates: list[Path] = []
        for variable, segments in (
            ("PROGRAMFILES", ("Google", "Chrome", "Application", "chrome.exe")),
            ("PROGRAMFILES(X86)", ("Google", "Chrome", "Application", "chrome.exe")),
            ("LOCALAPPDATA", ("Google", "Chrome", "Application", "chrome.exe")),
            ("PROGRAMFILES", ("Chromium", "Application", "chrome.exe")),
            ("PROGRAMFILES(X86)", ("Microsoft", "Edge", "Application", "msedge.exe")),
            ("PROGRAMFILES", ("Microsoft", "Edge", "Application", "msedge.exe")),
        ):
            root = os.environ.get(variable)
            if root:
                candidates.append(Path(root, *segments))
        paths = tuple(candidates)
    else:
        return None

    for name in names:
        discovered = shutil.which(name)
        if discovered:
            browser = Path(discovered).resolve(strict=True)
            return browser, f"system browser {browser}"
    for path in paths:
        if path.is_file():
            return path, f"system browser {path}"
    return None


def smoke_workspace() -> Path:
    workspace = ROOT / ".agent-workspace"
    workspace.mkdir(exist_ok=True)
    canonical = workspace.resolve(strict=True)
    if canonical.parent != ROOT:
        raise NativeSmokeError(f"native smoke workspace is not within the checkout: {canonical}")
    return canonical


def run(executable: Path, *arguments: str, working_directory: Path | None = None) -> str:
    completed = subprocess.run(
        [str(executable), *arguments],
        cwd=working_directory,
        check=False,
        capture_output=True,
        text=True,
        timeout=120,
    )
    if completed.returncode != 0:
        output = "\n".join(value for value in (completed.stdout, completed.stderr) if value)
        raise NativeSmokeError(
            f"{' '.join(arguments)} failed with exit code {completed.returncode}:\n{output}"
        )
    return completed.stdout


def payload_executable(command: Path) -> Path:
    resolved = command.resolve()
    if sys.platform != "win32":
        return resolved
    shim = command.with_suffix(".shim")
    if not shim.is_file():
        return resolved
    match = re.search(r'^path\s*=\s*"(?P<path>.+)"\s*$', shim.read_text(), re.MULTILINE)
    if match is None:
        raise NativeSmokeError(f"could not read Scoop payload path from {shim}")
    return Path(match.group("path")).resolve()


def smoke(executable: Path, version: str) -> None:
    executable = executable.resolve()
    if not executable.is_file():
        raise NativeSmokeError(f"published executable does not exist: {executable}")
    payload = payload_executable(executable)
    if not payload.is_file():
        raise NativeSmokeError(f"published payload does not exist: {payload}")
    platform = "linux" if sys.platform.startswith("linux") else sys.platform
    if platform not in SIDECARS:
        raise NativeSmokeError(f"unsupported smoke platform: {sys.platform}")

    expected = {payload.name, *SIDECARS[platform], "browser-lock.toml"}
    missing = sorted(name for name in expected if not (payload.parent / name).is_file())
    if missing:
        raise NativeSmokeError(f"missing adjacent release assets: {', '.join(missing)}")
    lock = (payload.parent / "browser-lock.toml").read_bytes()
    if not lock or b"\r" in lock:
        raise NativeSmokeError("browser-lock.toml is empty or is not canonical LF text")

    actual_version = run(executable, "--version").strip()
    expected_version = f"viset {version}"
    if actual_version != expected_version:
        raise NativeSmokeError(
            f"unexpected version output: {actual_version!r}; expected {expected_version!r}"
        )
    if "viset capture CAPTURE.lua" not in run(executable, "--help"):
        raise NativeSmokeError("help output does not contain the capture command")

    browser = browser_selection()
    if browser is None:
        print("native-smoke: capture skipped: no configured or discoverable browser")
        return
    explicit_browser, browser_description = browser
    print(f"native-smoke: capture enabled: {browser_description}")

    with tempfile.TemporaryDirectory(
        prefix="viset-native-smoke-", dir=smoke_workspace()
    ) as temporary:
        directory = Path(temporary).resolve(strict=True)
        script = directory / "smoke.lua"
        browser_arguments = 'browser_arguments = ["--no-sandbox"]\n' if platform == "linux" else ""
        script.write_text(
            f"""--[[
# viset
version = 1
output = "smoke.png"
{browser_arguments}

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
        output = directory / "output"
        capture_arguments = ["capture", str(script), "--output", str(output)]
        if explicit_browser is not None:
            capture_arguments.extend(("--browser", str(explicit_browser)))
        run(executable, *capture_arguments, working_directory=directory)
        capture = output / "smoke.png"
        if not capture.is_file() or not capture.read_bytes().startswith(PNG_SIGNATURE):
            raise NativeSmokeError("minimal capture did not produce a PNG")


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Smoke a native Viset publish directory.")
    parser.add_argument("executable", type=Path)
    parser.add_argument("--version", required=True)
    args = parser.parse_args(arguments)
    try:
        smoke(args.executable, args.version)
    except (NativeSmokeError, OSError, subprocess.SubprocessError, UnicodeError) as error:
        print(f"native-smoke: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
