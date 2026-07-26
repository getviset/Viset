#!/usr/bin/env python3

from __future__ import annotations

import argparse
from pathlib import Path
import shutil
import subprocess
import sys
import tomllib


ROOT = Path(__file__).resolve().parents[2]
RID_LAYOUTS = {
    "linux-x64": ("viset", "linux"),
    "linux-arm64": ("viset", "linux"),
    "osx-arm64": ("viset", "macos"),
    "win-x64": ("viset.exe", "windows"),
    "win-arm64": ("viset.exe", "windows"),
}


class PrepareError(RuntimeError):
    pass


def canonical_text_bytes(path: Path) -> bytes:
    text = path.read_bytes().decode("utf-8")
    return text.replace("\r\n", "\n").replace("\r", "\n").encode("utf-8")


def remove_macos_rpaths(path: Path) -> None:
    output = subprocess.run(
        ["otool", "-l", str(path)],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.splitlines()
    rpaths: list[str] = []
    for index, line in enumerate(output):
        if line.strip() != "cmd LC_RPATH":
            continue
        for candidate in output[index + 1 : index + 5]:
            value = candidate.strip()
            if value.startswith("path "):
                rpaths.append(value.removeprefix("path ").split(" (offset ", 1)[0])
                break
    for rpath in rpaths:
        subprocess.run(
            ["install_name_tool", "-delete_rpath", rpath, str(path)],
            check=True,
        )


def relocate_macos_sidecars(directory: Path, names: list[str]) -> None:
    for tool in ("otool", "install_name_tool"):
        if shutil.which(tool) is None:
            raise PrepareError(f"required macOS packaging tool is unavailable: {tool}")

    replacements = {
        "@rpath/libsharpyuv.0.dylib": "@loader_path/libsharpyuv.dylib",
        "@rpath/libwebp.7.dylib": "@loader_path/libwebp.dylib",
        "@rpath/libwebpmux.3.dylib": "@loader_path/libwebpmux.dylib",
    }
    for name in names:
        path = directory / name
        dependencies = subprocess.run(
            ["otool", "-L", str(path)],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.splitlines()
        dependencies = {
            line.strip().split(" (compatibility ", 1)[0]
            for line in dependencies[2:]
            if line.strip()
        }
        for source, destination in replacements.items():
            if source in dependencies:
                subprocess.run(
                    ["install_name_tool", "-change", source, destination, str(path)],
                    check=True,
                )
        subprocess.run(
            ["install_name_tool", "-id", f"@loader_path/{name}", str(path)],
            check=True,
        )
        remove_macos_rpaths(path)

        identifiers = subprocess.run(
            ["otool", "-D", str(path)],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.splitlines()
        if identifiers[1:] != [f"@loader_path/{name}"]:
            raise PrepareError(f"unexpected macOS install ID in {name}: {identifiers[1:]}")
        dependencies = subprocess.run(
            ["otool", "-L", str(path)],
            check=True,
            capture_output=True,
            text=True,
        ).stdout
        if "@rpath/" in dependencies:
            raise PrepareError(f"non-relocatable macOS dependency in {name}:\n{dependencies}")
        load_commands = subprocess.run(
            ["otool", "-l", str(path)],
            check=True,
            capture_output=True,
            text=True,
        ).stdout
        if "cmd LC_RPATH" in load_commands:
            raise PrepareError(f"unexpected LC_RPATH remains in {name}")


def prepare(rid: str, publish_directory: Path) -> None:
    executable, platform = RID_LAYOUTS[rid]
    publish_directory = publish_directory.resolve()
    if not publish_directory.is_dir():
        raise PrepareError(f"publish directory does not exist: {publish_directory}")

    with (ROOT / ".config" / "native-sidecars.toml").open("rb") as stream:
        sidecars = tomllib.load(stream)["platforms"][platform]["files"]
    expected = {executable, *sidecars, "browser-lock.toml"}
    missing = sorted(name for name in expected if not (publish_directory / name).is_file())
    if missing:
        raise PrepareError(f"missing {rid} publish files: {', '.join(missing)}")

    lock = publish_directory / "browser-lock.toml"
    lock.write_bytes(canonical_text_bytes(lock))
    for path in publish_directory.iterdir():
        if path.name in expected and path.is_file():
            continue
        if path.is_dir():
            shutil.rmtree(path)
        else:
            path.unlink()

    if platform == "macos":
        relocate_macos_sidecars(publish_directory, sidecars)

    actual = {path.name for path in publish_directory.iterdir() if path.is_file()}
    if actual != expected or any(path.is_dir() for path in publish_directory.iterdir()):
        raise PrepareError(
            f"prepared {rid} inventory mismatch: actual={sorted(actual)} expected={sorted(expected)}"
        )
    if b"\r" in lock.read_bytes():
        raise PrepareError("browser-lock.toml was not canonicalized to LF")
    print(f"prepared {rid}: {', '.join(sorted(actual))}")


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Prepare a Viset Native AOT publish directory.")
    parser.add_argument("--rid", required=True, choices=RID_LAYOUTS)
    parser.add_argument("--publish-directory", required=True, type=Path)
    args = parser.parse_args(arguments)
    try:
        prepare(args.rid, args.publish_directory)
    except (OSError, PrepareError, subprocess.SubprocessError, UnicodeError) as error:
        print(f"publish-prepare: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
