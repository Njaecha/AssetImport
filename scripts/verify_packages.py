#!/usr/bin/env python3
"""Verify one complete KK/KKS/source package set before uploading it."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

from build_identity import ROOT, git


def verify(directory, commit):
    directory = Path(directory)
    build_id = "g" + commit[:12]
    stems = ["KK_AssetImport_" + build_id + "_Packed",
             "KKS_AssetImport_" + build_id + "_Packed",
             "AssetImport_" + build_id + "_source"]
    expected = {stem + suffix for stem in stems for suffix in (".zip", ".zip.sha256")}
    expected.update(stem + ".manifest.json" for stem in stems[:2])
    if {p.name for p in directory.iterdir()} != expected:
        raise ValueError("Package set has missing or unexpected files: " + str(directory))
    for stem in stems:
        path = directory / (stem + ".zip")
        checksum = path.with_suffix(".zip.sha256").read_text().split()
        if checksum != [hashlib.sha256(path.read_bytes()).hexdigest(), path.name]:
            raise ValueError("Package checksum mismatch: " + path.name)
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            if len(names) != len(set(names)) or archive.testzip() is not None:
                raise ValueError("Invalid ZIP contents: " + path.name)
            if any(".local" in Path(name).parts or name.endswith("TEST-NOTES.txt") for name in names):
                raise ValueError("Private files in package: " + path.name)
            source = stem == stems[2]
            note = archive.read("AssetImport/BUILD.txt" if source else "BUILD.txt").decode("utf-8-sig")
            if "Commit: " + commit not in note.splitlines() or "AssetImport build: " + build_id not in note.splitlines():
                raise ValueError("Build identity mismatch: " + path.name)
            if not source:
                manifest = json.loads(path.with_suffix(".manifest.json").read_text(encoding="utf-8"))
                identity = manifest["source"]
                if identity["commit"] != commit or identity["buildId"] != build_id or identity["dirty"] is not False:
                    raise ValueError("Manifest identity mismatch: " + path.name)
                hashes = manifest["sha256"]
                if set(hashes) != set(names):
                    raise ValueError("Manifest file list mismatch: " + path.name)
                for name, digest in hashes.items():
                    if hashlib.sha256(archive.read(name)).hexdigest() != digest:
                        raise ValueError("Manifest checksum mismatch: " + name)
    return expected


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, help="Defaults to artifacts/g<current commit>")
    args = parser.parse_args()
    commit = git("rev-parse", "HEAD")
    directory = args.directory or ROOT / "artifacts" / ("g" + commit[:12])
    files = verify(directory, commit)
    print("Verified {} release files for {}".format(len(files), commit))


if __name__ == "__main__":
    main()
