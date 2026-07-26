#!/usr/bin/env python3
from __future__ import annotations

import argparse
import gzip
import hashlib
import pathlib
import shutil
import stat
import subprocess
import tarfile
import tempfile
import tomllib
import zipfile


VERSION = "v0.1.0"
RID_LAYOUTS = {
    "linux-x64": ("viset", "linux", "tar.gz"),
    "linux-arm64": ("viset", "linux", "tar.gz"),
    "win-x64": ("viset.exe", "windows", "zip"),
    "win-arm64": ("viset.exe", "windows", "zip"),
    "osx-arm64": ("viset", "macos", "tar.gz"),
}
ROOT = pathlib.Path(__file__).resolve().parent.parent
SIDECAR_MANIFEST = ROOT / ".config" / "native-sidecars.toml"


def archive_name(rid: str) -> str:
    extension = RID_LAYOUTS[rid][2]
    return f"viset-{VERSION}-{rid}.{extension}"


def expected_names(rid: str) -> list[str]:
    executable, platform, _ = RID_LAYOUTS[rid]
    with SIDECAR_MANIFEST.open("rb") as stream:
        sidecars = tomllib.load(stream)["platforms"][platform]["files"]
    return [executable, *sidecars, "browser-lock.toml", "LICENSE"]


def archive_mode(name: str) -> int:
    return 0o755 if name in {"viset", "viset.exe"} else 0o644


def remove_macos_rpaths(path: pathlib.Path) -> None:
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


def make_macos_sidecars_relocatable(directory: pathlib.Path) -> None:
    for tool in ("otool", "install_name_tool"):
        if shutil.which(tool) is None:
            raise FileNotFoundError(f"required macOS packaging tool is unavailable: {tool}")

    replacements = {
        "@rpath/libsharpyuv.0.dylib": "@loader_path/libsharpyuv.dylib",
        "@rpath/libwebp.7.dylib": "@loader_path/libwebp.dylib",
        "@rpath/libwebpmux.3.dylib": "@loader_path/libwebpmux.dylib",
    }

    for name in expected_names("osx-arm64"):
        if not name.endswith(".dylib"):
            continue
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
            raise RuntimeError(f"unexpected macOS install ID in {name}: {identifiers[1:]}")

        dependencies = subprocess.run(
            ["otool", "-L", str(path)],
            check=True,
            capture_output=True,
            text=True,
        ).stdout
        if "@rpath/" in dependencies:
            raise RuntimeError(f"non-relocatable macOS dependency in {name}:\n{dependencies}")

        load_commands = subprocess.run(
            ["otool", "-l", str(path)],
            check=True,
            capture_output=True,
            text=True,
        ).stdout
        if "cmd LC_RPATH" in load_commands:
            raise RuntimeError(f"unexpected LC_RPATH remains in {name}")


def stage_payload(rid: str, publish_directory: pathlib.Path, destination: pathlib.Path) -> None:
    names = expected_names(rid)
    sources = {
        name: publish_directory / name
        for name in names
        if name not in {"browser-lock.toml", "LICENSE"}
    }
    sources["browser-lock.toml"] = ROOT / "browser-lock.toml"
    sources["LICENSE"] = ROOT / "LICENSE"

    missing = [str(path) for path in sources.values() if not path.is_file()]
    if missing:
        raise FileNotFoundError("missing release inputs: " + ", ".join(missing))

    destination.mkdir()
    for name in names:
        shutil.copyfile(sources[name], destination / name)
        (destination / name).chmod(archive_mode(name))

    if rid == "osx-arm64":
        make_macos_sidecars_relocatable(destination)


def write_tar_gz(source: pathlib.Path, names: list[str], destination: pathlib.Path) -> None:
    with destination.open("wb") as output:
        with gzip.GzipFile(filename="", mode="wb", fileobj=output, mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w", format=tarfile.USTAR_FORMAT) as archive:
                for name in names:
                    path = source / name
                    info = tarfile.TarInfo(name)
                    info.size = path.stat().st_size
                    info.mode = archive_mode(name)
                    info.mtime = 0
                    info.uid = 0
                    info.gid = 0
                    info.uname = ""
                    info.gname = ""
                    with path.open("rb") as stream:
                        archive.addfile(info, stream)


def write_zip(source: pathlib.Path, names: list[str], destination: pathlib.Path) -> None:
    with zipfile.ZipFile(
        destination,
        mode="w",
        compression=zipfile.ZIP_DEFLATED,
        compresslevel=9,
        strict_timestamps=True,
    ) as archive:
        for name in names:
            path = source / name
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = (stat.S_IFREG | archive_mode(name)) << 16
            archive.writestr(info, path.read_bytes(), compresslevel=9)


def package(rid: str, publish_directory: pathlib.Path, output_directory: pathlib.Path) -> pathlib.Path:
    publish_directory = publish_directory.resolve()
    output_directory.mkdir(parents=True, exist_ok=True)
    destination = output_directory / archive_name(rid)
    if destination.exists():
        raise FileExistsError(f"refusing to overwrite release archive: {destination}")

    names = expected_names(rid)
    with tempfile.TemporaryDirectory(prefix=f"viset-{rid}-") as temporary:
        staged = pathlib.Path(temporary) / "payload"
        stage_payload(rid, publish_directory, staged)
        if RID_LAYOUTS[rid][2] == "zip":
            write_zip(staged, names, destination)
        else:
            write_tar_gz(staged, names, destination)

    inspect_archive(destination, rid)
    print(f"packaged {rid}: {destination}")
    return destination


def read_archive(path: pathlib.Path) -> dict[str, tuple[int, bytes]]:
    entries: dict[str, tuple[int, bytes]] = {}
    if path.name.endswith(".tar.gz"):
        with tarfile.open(path, mode="r:gz") as archive:
            for member in archive.getmembers():
                if not member.isfile():
                    raise RuntimeError(f"non-file archive entry: {member.name}")
                stream = archive.extractfile(member)
                assert stream is not None
                entries[member.name] = (member.mode, stream.read())
    elif path.suffix == ".zip":
        with zipfile.ZipFile(path) as archive:
            for info in archive.infolist():
                if info.is_dir():
                    raise RuntimeError(f"directory archive entry: {info.filename}")
                entries[info.filename] = (info.external_attr >> 16, archive.read(info))
    else:
        raise ValueError(f"unsupported release archive: {path}")
    return entries


def inspect_archive(path: pathlib.Path, rid: str) -> None:
    if path.name != archive_name(rid):
        raise RuntimeError(f"unexpected archive name for {rid}: {path.name}")

    entries = read_archive(path)
    names = expected_names(rid)
    if list(entries) != names:
        raise RuntimeError(
            f"archive inventory mismatch for {rid}: actual={list(entries)} expected={names}"
        )

    executable = RID_LAYOUTS[rid][0]
    for name, (mode, _) in entries.items():
        expected_mode = 0o755 if name == executable else 0o644
        if mode & 0o777 != expected_mode:
            raise RuntimeError(
                f"unexpected mode for {name}: {mode & 0o777:o}; expected {expected_mode:o}"
            )

    for name in ("browser-lock.toml", "LICENSE"):
        if entries[name][1] != (ROOT / name).read_bytes():
            raise RuntimeError(f"archive contains an unexpected {name}")

    print(f"inspected {rid}: {', '.join(entries)}")


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_checksums(directory: pathlib.Path) -> pathlib.Path:
    directory = directory.resolve()
    paths = [directory / archive_name(rid) for rid in RID_LAYOUTS]
    missing = [path.name for path in paths if not path.is_file()]
    if missing:
        raise FileNotFoundError("missing release archives: " + ", ".join(missing))

    destination = directory / "checksums.toml"
    if destination.exists():
        raise FileExistsError(f"refusing to overwrite checksums: {destination}")

    lines = ['version = 1', 'algorithm = "sha256"', ""]
    for path in sorted(paths, key=lambda item: item.name):
        lines.extend(
            [
                "[[assets]]",
                f'name = "{path.name}"',
                f'sha256 = "{sha256(path)}"',
                "",
            ]
        )
    destination.write_text("\n".join(lines), encoding="utf-8", newline="\n")
    verify_candidate(directory)
    print(f"checksums: {destination}")
    return destination


def verify_candidate(directory: pathlib.Path) -> None:
    directory = directory.resolve()
    checksums_path = directory / "checksums.toml"
    with checksums_path.open("rb") as stream:
        manifest = tomllib.load(stream)
    if manifest.get("version") != 1 or manifest.get("algorithm") != "sha256":
        raise RuntimeError("unexpected checksums.toml contract")

    assets = manifest.get("assets")
    if not isinstance(assets, list):
        raise RuntimeError("checksums.toml assets must be an array")

    expected = sorted(archive_name(rid) for rid in RID_LAYOUTS)
    actual = [asset.get("name") for asset in assets]
    if actual != expected or "checksums.toml" in actual:
        raise RuntimeError(f"checksum inventory mismatch: actual={actual} expected={expected}")

    rid_by_archive = {archive_name(rid): rid for rid in RID_LAYOUTS}
    for asset in assets:
        path = directory / asset["name"]
        inspect_archive(path, rid_by_archive[path.name])
        digest = sha256(path)
        if asset.get("sha256") != digest:
            raise RuntimeError(f"checksum mismatch for {path.name}")

    print("candidate verified: five archives and checksums.toml")


def main() -> None:
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)

    package_parser = commands.add_parser("package")
    package_parser.add_argument("--rid", choices=RID_LAYOUTS, required=True)
    package_parser.add_argument("--publish-directory", type=pathlib.Path, required=True)
    package_parser.add_argument("--output-directory", type=pathlib.Path, required=True)

    inspect_parser = commands.add_parser("inspect")
    inspect_parser.add_argument("--rid", choices=RID_LAYOUTS, required=True)
    inspect_parser.add_argument("archive", type=pathlib.Path)

    checksums_parser = commands.add_parser("checksums")
    checksums_parser.add_argument("directory", type=pathlib.Path)

    verify_parser = commands.add_parser("verify")
    verify_parser.add_argument("directory", type=pathlib.Path)

    arguments = parser.parse_args()
    if arguments.command == "package":
        package(arguments.rid, arguments.publish_directory, arguments.output_directory)
    elif arguments.command == "inspect":
        inspect_archive(arguments.archive, arguments.rid)
    elif arguments.command == "checksums":
        write_checksums(arguments.directory)
    else:
        verify_candidate(arguments.directory)


if __name__ == "__main__":
    main()
