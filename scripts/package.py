#!/usr/bin/env python3
"""Package the KK build with only its redistributable runtime dependencies."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import time
import zipfile

from build_identity import package_identity, build_note, source_archive_files

ROOT = Path(__file__).resolve().parents[1]
PLUGIN = "BepInEx/plugins/AssetImport/"
NATIVE = "runtimes/win-x64/native/assimp.dll"


def version_output(base, version):
    base = Path(base).resolve()
    return base if base.name == version else base / version


def write_zip(path, files):
    # BepInEx invalidates plugin metadata using only the DLL's modification time.
    # Never reuse a fixed timestamp across releases. Reproducible release builds
    # can set SOURCE_DATE_EPOCH to their own release/build timestamp.
    timestamp = time.gmtime(int(os.environ.get("SOURCE_DATE_EPOCH", time.time())))[:6]
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(files.items()):
            info = zipfile.ZipInfo(name, timestamp)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, data)


def write_checksum(path):
    # Keep checksum files compatible with sha256sum on Unix after a Windows build.
    path = Path(path)
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    path.with_suffix(path.suffix + ".sha256").write_bytes((digest + "  " + path.name + "\n").encode("utf-8"))


def notes_files(path):
    """Include only a notes file explicitly selected by the caller."""
    if path is None:
        return {}
    text = Path(path).read_text(encoding="utf-8-sig")
    return {"TEST-NOTES.txt": text.replace("\n", "\r\n").encode("utf-8-sig")}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts",
                        help="Artifact root; packages are placed in a Git build ID subdirectory")
    parser.add_argument("--configuration", default="Release", choices=["Debug", "Release"])
    parser.add_argument("--source", action="store_true", help="Also create a source archive (no generated build references)")
    parser.add_argument("--test-assets", action="store_true", help="Also create a separate optional test-assets archive")
    parser.add_argument("--dll-update", action="store_true", help="Also package only the plugin DLL for installs with matching runtime dependencies")
    parser.add_argument("--notes", type=Path, help="Explicit UTF-8 test notes to include as TEST-NOTES.txt")
    args = parser.parse_args()
    extra_notes = notes_files(args.notes)
    project = ROOT / "src" / "AssetImport"
    build = project / "bin" / args.configuration / "net35"
    version = re.search(r'const string Version = "([^"]+)"', (project / "AssetImport.cs").read_text(encoding="utf-8-sig")).group(1)
    names = ["KK_AssetImport.dll", "AssimpNet.dll", "LitJSON.dll", NATIVE]
    for name in names:
        if not (build / name).is_file():
            raise SystemExit("Missing {}. First run dotnet build src/AssetImport/AssetImport.csproj -c {}".format(name, args.configuration))
    source_times = [p.stat().st_mtime for p in project.rglob("*.cs") if "obj" not in p.parts and "bin" not in p.parts]
    source_times.append((project / "AssetImport.csproj").stat().st_mtime)
    source_times.append((ROOT / "scripts/mmdd/item_morph_functions.py").stat().st_mtime)
    if max(source_times) > (build / "KK_AssetImport.dll").stat().st_mtime:
        raise SystemExit("The KK DLL is older than its sources. Rebuild before packaging.")
    identity = package_identity(build / "KK_AssetImport.dll")
    build_id = identity["buildId"]
    output = version_output(args.output, build_id)
    output.mkdir(parents=True, exist_ok=True)
    files = {(name if name == NATIVE else PLUGIN + name): (build / name).read_bytes() for name in names}
    for name in ["AssimpNet.txt", "Assimp.txt", "LitJson.txt"]:
        files[PLUGIN + "licenses/" + name] = (ROOT / "docs" / "licenses" / name).read_bytes()
    # Encode installation instructions for Windows Notepad.
    instructions = (ROOT / "docs" / "INSTALL-KK.txt").read_text(encoding="utf-8-sig")
    files["安装说明.txt"] = instructions.replace("\n", "\r\n").encode("utf-8-sig")
    files["BUILD.txt"] = build_note(identity, version)
    files.update(extra_notes)
    manifest = {
        "source": identity,
        "plugin": "KK_AssetImport", "version": version,
        "target": "Koikatsu / Unity 5.6 / .NET 3.5 / Windows x64",
        "dependencies": {"AssimpNet": "5.0.0-beta1", "native Assimp": "5.0.1", "LitJSON": "0.19.0"},
        "requiredPlugins": {"BepInEx": "5.4.22+", "KKAPI": "1.45.1+", "KK_MaterialEditor": "4.0.3+", "LoadFileLimitedFix": "KK build"},
        "sha256": {name: hashlib.sha256(data).hexdigest() for name, data in sorted(files.items())}
    }
    package = output / ("KK_AssetImport_" + build_id + "_Packed.zip")
    write_zip(package, files)
    package.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    created = [package]
    if args.dll_update:
        update_instructions = (
            "KK AssetImport " + build_id + " — Plugin DLL update / 插件更新\n\n"
            "Close the game and Studio. Copy BepInEx into the game root.\n"
            "退出游戏和 Studio，将 BepInEx 复制到游戏根目录并覆盖。\n"
            "This archive replaces only KK_AssetImport.dll. It requires an existing\n"
            "installation with matching runtime libraries. Otherwise use Packed.\n"
            "本包仅替换插件 DLL；首次安装或运行库变更时请使用完整 Packed 包。\n"
            "Move obsolete DLL copies out of BepInEx/plugins to avoid duplicate loading.\n"
            "将多余旧 DLL 移出 BepInEx/plugins，避免重复加载。\n"
            "Build details / 构建信息：BUILD.txt\n"
        )
        update = output / ("KK_AssetImport_" + build_id + "_DLL_Update.zip")
        write_zip(update, {PLUGIN + "KK_AssetImport.dll": files[PLUGIN + "KK_AssetImport.dll"],
                           "BUILD.txt": files["BUILD.txt"],
                           "更新说明.txt": update_instructions.replace("\n", "\r\n").encode("utf-8-sig"),
                           **extra_notes})
        created.append(update)
    if args.test_assets:
        fixtures = {"README.txt": b"Synthetic OBJ and texture fixtures for AssetImport.\nImport textured-cube.obj with its companion MTL and PNG files.\n"}
        for fixture in sorted((ROOT / "tests" / "fixtures").rglob("*")):
            if fixture.is_file() and not fixture.name.startswith("."):
                fixtures["TestAssets/" + fixture.relative_to(ROOT / "tests" / "fixtures").as_posix()] = fixture.read_bytes()
        test_zip = output / ("KK_AssetImport_" + build_id + "_TestAssets.zip")
        write_zip(test_zip, fixtures)
        created.append(test_zip)
    if args.source:
        sources = {"AssetImport/" + name: data for name, data in source_archive_files()}
        sources["AssetImport/BUILD.txt"] = files["BUILD.txt"]
        source_zip = output / ("AssetImport_" + build_id + "_source.zip")
        write_zip(source_zip, sources)
        created.append(source_zip)
    for path in created:
        write_checksum(path)
        print(path)


if __name__ == "__main__":
    main()
