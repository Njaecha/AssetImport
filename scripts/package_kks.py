#!/usr/bin/env python3
"""Package KKS using the matching runtime set from the author's 4.0.1 Packed ZIP."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

from build_identity import package_identity, build_note
from package import ROOT, PLUGIN, NATIVE, version_output, write_zip, write_checksum, notes_files

UPSTREAM_SHA256 = "3c2743096c44a3b6d2d9a54198e7a5b865e105706ccc65b65c4e8ba571828e5d"
RUNTIMES = [
    PLUGIN + "AssimpNetter.dll",
    PLUGIN + "IndexRange.dll",
    NATIVE,
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--upstream-package", type=Path, default=ROOT / ".build-downloads/AssetImportv4.0.1Packed.zip",
                        help="Author's AssetImportv4.0.1Packed.zip (SHA-256 pinned)")
    parser.add_argument("--managed-runtime", type=Path, default=ROOT / ".build-downloads/kks-runtime",
                        help="Directory of pinned managed DLLs and license files from kks-runtime.json")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts")
    parser.add_argument("--configuration", choices=["Debug", "Release"], default="Release")
    parser.add_argument("--notes", type=Path, help="Explicit UTF-8 test notes to include as TEST-NOTES.txt")
    args = parser.parse_args()
    extra_notes = notes_files(args.notes)
    upstream = args.upstream_package.read_bytes()
    if hashlib.sha256(upstream).hexdigest() != UPSTREAM_SHA256:
        raise SystemExit("Upstream archive hash differs; do not mix unverified KKS runtimes.")
    project = ROOT / "src/AssetImport"
    build = project / "bin" / args.configuration / "net462"
    plugin = build / "KKS_AssetImport.dll"
    version = re.search(r'const string Version = "([^"]+)"',
                        (project / "AssetImport.cs").read_text(encoding="utf-8-sig")).group(1)
    sources = [p for p in project.rglob("*.cs") if "bin" not in p.parts and "obj" not in p.parts]
    sources += [project / "AssetImport.csproj", ROOT / "scripts/mmdd/item_morph_functions.py"]
    if not plugin.is_file() or max(p.stat().st_mtime for p in sources) > plugin.stat().st_mtime:
        raise SystemExit("Build the current KKS net462 target before packaging.")
    if not (version + ".0").encode("ascii") in plugin.read_bytes():
        raise SystemExit("KKS DLL version does not match source.")
    # Keep AssimpNetter, native Assimp and its helper assemblies together.
    # The compiled plugin's used Assimp API is checked against this exact wrapper.
    with zipfile.ZipFile(args.upstream_package) as archive:
        files = {name: archive.read(name) for name in RUNTIMES}
    files[PLUGIN + "KKS_AssetImport.dll"] = plugin.read_bytes()
    files[PLUGIN + "LitJSON.dll"] = (build / "LitJSON.dll").read_bytes()
    managed = json.loads((ROOT / "scripts/kks-runtime.json").read_text(encoding="utf-8"))
    for item in managed["files"]:
        data = (args.managed_runtime / item["path"]).read_bytes()
        if hashlib.sha256(data).hexdigest() != item["sha256"]:
            raise SystemExit("Managed runtime hash mismatch: " + item["path"])
        files[PLUGIN + item["target"]] = data

    for name in ["AssimpNetter.txt", "Assimp6.txt", "IndexRange.txt", "LitJson.txt"]:
        files[PLUGIN + "licenses/" + name] = (ROOT / "docs/licenses" / name).read_bytes()
    for output_name, source_name in [
        ("安装说明.txt", "INSTALL-KKS.txt"),
    ]:
        text = (ROOT / "docs" / source_name).read_text(encoding="utf-8-sig")
        files[output_name] = text.replace("\n", "\r\n").encode("utf-8-sig")
    identity = package_identity(plugin)
    build_id = identity["buildId"]
    files["BUILD.txt"] = build_note(identity, version)
    files.update(extra_notes)
    output = version_output(args.output, build_id)
    output.mkdir(parents=True, exist_ok=True)
    package = output / ("KKS_AssetImport_" + build_id + "_Packed.zip")
    write_zip(package, files)
    manifest = {
        "source": identity,
        "plugin": "KKS_AssetImport", "version": version,
        "target": "Koikatsu Sunshine / Unity 2019.4 / .NET 4.6.2 / Windows x64",
        "requiredPlugins": {"BepInEx": "5.4.22+ (5.x)", "KKSAPI": "1.43.0+",
                            "KKS_MaterialEditor": "3.13.5+", "LoadFileLimitedFix": "KKS build"},
        "runtimeSource": {"archive": args.upstream_package.name, "sha256": UPSTREAM_SHA256,
                          "members": RUNTIMES},
        "managedRuntimeSources": managed["packages"],
        "sha256": {name: hashlib.sha256(data).hexdigest() for name, data in sorted(files.items())},
    }
    package.with_suffix(".manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    write_checksum(package)
    print(package)


if __name__ == "__main__":
    main()
