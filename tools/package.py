"""Build and verify source-derived Unity asset packages without a Unity license."""

import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import tarfile


def digest(data):
    return hashlib.sha256(data).hexdigest()


def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args], text=True).strip()


def collect_assets(root, paths):
    selected = set()
    for name in paths:
        path = root / name
        if not path.exists() or not name.startswith("Assets/") or ".." in Path(name).parts:
            raise ValueError(f"Invalid package path: {name}")
        selected.add(path)
        if path.is_dir():
            selected.update(p for p in path.rglob("*") if p.suffix != ".meta")
        selected.update(p for p in path.parents if p != root / "Assets" and (root / "Assets") in p.parents)
    assets = {}
    guids = {}
    for path in sorted(selected):
        name = path.relative_to(root).as_posix()
        if "/Tests" in name or "/Editor" in name or path.is_symlink():
            raise ValueError(f"Development-only or linked asset selected: {name}")
        meta = Path(str(path) + ".meta").read_bytes()
        match = re.search(rb"^guid: ([0-9a-f]{32})\s*$", meta, re.MULTILINE)
        if not match:
            raise ValueError(f"Missing GUID: {name}")
        guid = match[1].decode()
        if guid in guids:
            raise ValueError(f"Duplicate GUID: {name} and {guids[guid]}")
        guids[guid] = name
        asset = path.read_bytes() if path.is_file() else None
        if asset and asset.startswith(b"version https://git-lfs.github.com/spec/v1"):
            raise ValueError(f"LFS pointer instead of asset: {name}")
        assets[name] = {"guid": guid, "meta": meta, "asset": asset}
    return assets


def write_package(path, assets):
    with path.open("wb") as raw, gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as compressed:
        with tarfile.open(fileobj=compressed, mode="w|", format=tarfile.USTAR_FORMAT) as archive:
            for name, asset in sorted(assets.items()):
                entries = {"pathname": name.encode(), "asset.meta": asset["meta"]}
                if asset["asset"] is not None:
                    entries["asset"] = asset["asset"]
                for leaf, data in sorted(entries.items()):
                    info = tarfile.TarInfo(asset["guid"] + "/" + leaf)
                    info.size = len(data)
                    info.mode = 0o644
                    archive.addfile(info, io.BytesIO(data))


def read_package(path):
    records = {}
    with tarfile.open(path, "r|gz") as archive:
        for member in archive:
            parts = PurePosixPath(member.name).parts
            if not member.isfile() or len(parts) != 2 or not re.fullmatch("[0-9a-f]{32}", parts[0]):
                raise ValueError(f"Unexpected archive member: {member.name}")
            if parts[1] not in ("pathname", "asset.meta", "asset"):
                raise ValueError(f"Unexpected archive entry: {member.name}")
            record = records.setdefault(parts[0], {})
            if parts[1] in record:
                raise ValueError(f"Duplicate archive entry: {member.name}")
            record[parts[1]] = archive.extractfile(member).read()
    assets = {}
    for guid, record in records.items():
        name = record["pathname"].decode()
        if not name.startswith("Assets/") or ".." in PurePosixPath(name).parts or name in assets:
            raise ValueError(f"Invalid asset pathname: {name}")
        assets[name] = {"guid": guid, "meta": record["asset.meta"], "asset": record.get("asset")}
    return assets


def check_source(root, config):
    version = config["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-rc\.\d+)?", version):
        raise ValueError("Invalid release version")
    if f"v{version}" not in (root / "README.md").read_text(encoding="utf-8-sig"):
        raise ValueError("README version differs from release.json")
    if f"## [{version}]" not in (root / "CHANGELOG.md").read_text(encoding="utf-8-sig"):
        raise ValueError("Missing changelog entry")
    if "stream VMC" in (root / "Assets/Readme.asset").read_text(encoding="utf-8-sig"):
        raise ValueError("Outdated sample instructions")
    groups = {name: collect_assets(root, paths) for name, paths in config["packages"].items()}
    core = groups["Core"]
    if "Assets/MOVIN/Scripts/Core/MOVINStreamReceiver.Status.cs" not in core:
        raise ValueError("Missing Studio status receiver")
    if any("MotionStreamReceiver." in name for name in core):
        raise ValueError("Obsolete receiver source selected")
    all_assets = {name for group in groups.values() for name in group}
    guid_paths = {}
    for meta in (root / "Assets").rglob("*.meta"):
        match = re.search(r"^guid: ([0-9a-f]{32})", meta.read_text(encoding="utf-8-sig"), re.MULTILINE)
        if match:
            guid_paths[match[1]] = meta.relative_to(root).as_posix()[:-5]
    for name in all_assets:
        if Path(name).suffix in (".unity", ".prefab", ".mat", ".asset", ".tss"):
            for guid in re.findall(r"guid: ([0-9a-f]{32})", (root / name).read_text(encoding="utf-8-sig")):
                dependency = guid_paths.get(guid)
                if dependency and dependency not in all_assets:
                    raise ValueError(f"Unpackaged dependency: {name} -> {dependency}")
    return groups


def build(root, output, config):
    groups = check_source(root, config)
    output.mkdir(parents=True, exist_ok=True)
    manifest = {"version": config["version"], "commit": git(root, "rev-parse", "HEAD"),
                "dirty": bool(git(root, "status", "--porcelain", "--untracked-files=all")),
                "studio_min": config["studio_min"], "packages": {}}
    for group, assets in groups.items():
        name = f'MOVIN-Unity-Plugin-{group}-v{config["version"]}.unitypackage'
        path = output / name
        write_package(path, assets)
        manifest["packages"][group] = {"file": name, "sha256": digest(path.read_bytes()),
            "bytes": path.stat().st_size, "assets": {key: {"guid": value["guid"],
                "meta_sha256": digest(value["meta"]),
                "sha256": digest(value["asset"]) if value["asset"] is not None else None}
                for key, value in assets.items()}}
    (output / "release-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    checksums = [f'{p["sha256"]}  {p["file"]}' for p in manifest["packages"].values()]
    (output / "SHA256SUMS.txt").write_text("\n".join(checksums) + "\n", encoding="utf-8")
    verify(root, output, config)
    print(json.dumps({"version": manifest["version"], "commit": manifest["commit"], "dirty": manifest["dirty"],
                      "packages": {k: {x: p[x] for x in ("file", "bytes", "sha256")} for k, p in manifest["packages"].items()}}))


def verify(root, output, config):
    expected = check_source(root, config)
    manifest = json.loads((output / "release-manifest.json").read_text(encoding="utf-8"))
    if manifest["version"] != config["version"] or set(manifest["packages"]) != set(expected):
        raise ValueError("Package manifest does not match release configuration")
    for group, assets in expected.items():
        entry = manifest["packages"][group]
        path = output / entry["file"]
        if digest(path.read_bytes()) != entry["sha256"]:
            raise ValueError(f"Archive checksum mismatch: {group}")
        actual = read_package(path)
        if actual != assets:
            missing = sorted(assets.keys() - actual.keys())
            different = sorted(k for k in actual.keys() & assets.keys() if actual[k] != assets[k])
            raise ValueError(f"Package differs from source: {group}; missing={missing}; changed={different}")
        for name, asset in actual.items():
            recorded = entry["assets"][name]
            if recorded != {"guid": asset["guid"], "meta_sha256": digest(asset["meta"]),
                            "sha256": digest(asset["asset"]) if asset["asset"] is not None else None}:
                raise ValueError(f"Asset manifest mismatch: {name}")
    print("Core/Samples packages match source and metadata; no test scripts included.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("check", "build", "verify"))
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[1]
    settings = json.loads((repository / "release.json").read_text(encoding="utf-8"))
    if args.command == "check":
        check_source(repository, settings)
        print("Release configuration, GUIDs and local asset dependencies are valid.")
    else:
        if args.output is None:
            parser.error("--output is required")
        if args.command == "build":
            build(repository, args.output.resolve(), settings)
        else:
            verify(repository, args.output.resolve(), settings)
