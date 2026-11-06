#!/usr/bin/env python3
"""Restore the pinned KKS runtime set for packaging on Windows/macOS/Linux."""
import argparse
import io
import json
from pathlib import Path
import urllib.request
import zipfile

from prepare_dependencies import ROOT, checked
from package_kks import UPSTREAM_SHA256

UPSTREAM_URL = "https://github.com/Njaecha/AssetImport/releases/download/v4.0.1/AssetImportv4.0.1Packed.zip"


def fetch(url, digest, path):
    if path.is_file():
        return checked(path.read_bytes(), digest, str(path))
    print("Downloading:", url, flush=True)
    request = urllib.request.Request(url, headers={"User-Agent": "AssetImport-build"})
    with urllib.request.urlopen(request, timeout=60) as response:
        data = checked(response.read(), digest, url)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return data


def restore(cache, destination):
    fetch(UPSTREAM_URL, UPSTREAM_SHA256, cache / "AssetImportv4.0.1Packed.zip")
    manifest = json.loads((ROOT / "scripts/kks-runtime.json").read_text(encoding="utf-8"))
    for package in manifest["packages"]:
        data = fetch(package["url"], package["package_sha256"],
                     cache / (package["id"] + "." + package["version"] + ".nupkg"))
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            target = destination / "lib" / package["target"]
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(checked(archive.read(package["member"]), package["dll_sha256"], str(target)))
            for license in package["licenses"]:
                target = destination / license["path"]
                target.parent.mkdir(parents=True, exist_ok=True)
                if "source_member" in license:
                    target.write_bytes(checked(archive.read(license["source_member"]), license["sha256"], str(target)))
                else:
                    fetch(license["url"], license["sha256"], target)
    for entry in manifest["files"]:
        checked((destination / entry["path"]).read_bytes(), entry["sha256"], entry["path"])
    print("Verified KKS runtime files:", len(manifest["files"]))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=ROOT / ".build-downloads")
    parser.add_argument("--destination", type=Path, default=ROOT / ".build-downloads/kks-runtime")
    args = parser.parse_args()
    restore(args.cache, args.destination)


if __name__ == "__main__":
    main()
