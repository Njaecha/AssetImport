#!/usr/bin/env python3
"""Fetch pinned, hash-checked KK compile references from their official releases."""
import hashlib
import io
import json
from pathlib import Path
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def checked(data, expected, name):
    actual = hashlib.sha256(data).hexdigest()
    if actual != expected:
        raise ValueError("SHA-256 mismatch for {}: {}".format(name, actual))
    return data


def main():
    cache = ROOT / ".build-downloads"
    destination = ROOT / "src" / "Custom Dependencies" / "KK"
    cache.mkdir(exist_ok=True)
    destination.mkdir(parents=True, exist_ok=True)
    for release in json.loads((ROOT / "scripts" / "dependencies.json").read_text()):
        if release.get("files") and all((destination / entry["target"]).exists() and
               hashlib.sha256((destination / entry["target"]).read_bytes()).hexdigest() == entry["sha256"]
               for entry in release["files"]):
            print("Already verified:", release["archive"])
            continue
        archive = cache / release["archive"]
        if not archive.exists():
            print("Downloading:", release["url"], flush=True)
            request = urllib.request.Request(release["url"], headers={"User-Agent": "AssetImport-build"})
            with urllib.request.urlopen(request, timeout=120) as response:
                data = checked(response.read(), release["sha256"], archive.name)
            archive.write_bytes(data)
        if not release.get("files"):
            checked(archive.read_bytes(), release["sha256"], archive.name)
            print("Verified build package:", archive.name)
            continue
        with zipfile.ZipFile(io.BytesIO(checked(archive.read_bytes(), release["sha256"], archive.name))) as outer:
            for entry in release["files"]:
                if entry["inner_archive"]:
                    with zipfile.ZipFile(io.BytesIO(outer.read(entry["inner_archive"]))) as inner:
                        data = inner.read(entry["member"])
                else:
                    data = outer.read(entry["member"])
                (destination / entry["target"]).write_bytes(checked(data, entry["sha256"], entry["target"]))
                print("Verified:", entry["target"])


if __name__ == "__main__":
    main()
