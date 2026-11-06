#!/usr/bin/env python3
"""Build the offline KK/MaterialEditor A/B test kit from verified inputs."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import urllib.request
import zipfile

from package import ROOT, NATIVE, PLUGIN, write_zip, version_output
from prepare_dependencies import checked


def fetch(entry, cache):
    path = cache / entry["archive"]
    if path.exists():
        return checked(path.read_bytes(), entry["sha256"], path.name)
    print("Downloading:", entry["url"], flush=True)
    request = urllib.request.Request(entry["url"], headers={"User-Agent": "AssetImport-testkit"})
    with urllib.request.urlopen(request, timeout=120) as response:
        data = checked(response.read(), entry["sha256"], path.name)
    path.write_bytes(data)
    return data


def read_zip(data):
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        return {name: archive.read(name) for name in archive.namelist() if not name.endswith("/")}


def zip_bytes(files):
    stream = io.BytesIO()
    write_zip(stream, files)
    return stream.getvalue()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plugin-package", type=Path, required=True,
                        help="KK_AssetImport_gHASH_Packed.zip produced by scripts/package.py")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts",
                        help="Artifact root; the test kit goes into its version subdirectory")
    parser.add_argument("--cache", type=Path, default=ROOT / ".build-downloads")
    parser.add_argument("--kit-root", type=Path, default=ROOT / ".local/testkit",
                        help="Local test kit inputs, including models.json and reference images")
    args = parser.parse_args()
    if not (args.kit_root / "models.json").is_file():
        parser.error("Test kit inputs are missing; use --kit-root to select a local kit directory")
    args.cache.mkdir(parents=True, exist_ok=True)
    package_manifest = json.loads(args.plugin_package.with_suffix(".manifest.json").read_text())
    version = package_manifest["version"]
    build_id = package_manifest["source"]["buildId"]
    args.output = version_output(args.output, build_id)
    args.output.mkdir(parents=True, exist_ok=True)
    plugin = read_zip(args.plugin_package.read_bytes())
    if set(plugin) != set(package_manifest["sha256"]):
        raise ValueError("Plugin package and manifest have different contents")
    for name, digest in package_manifest["sha256"].items():
        checked(plugin[name], digest, name)
    # Only ship maintained kit files, never runtime reports or local hidden files.
    files = {}
    for path in sorted(args.kit_root.rglob("*")):
        rel = path.relative_to(args.kit_root)
        if (not path.is_file() or any(part.startswith(".") for part in rel.parts)
                or rel.parts[0] in {"Reports", "CollectedLogs", "Diagnostic", "Installers"}):
            continue
        data = path.read_bytes()
        if path.suffix in {".cmd", ".ps1"}:
            data = data.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
        files[rel.as_posix()] = data
    model_manifest = json.loads(files["models.json"])
    if model_manifest["plugin"] != version:
        raise ValueError("Update the test-kit instructions and model manifest for this plugin version before packaging")
    demos = model_manifest["demos"]
    if len(demos) != 19 or len({demo["format"] for demo in demos}) != 18:
        raise ValueError("Expected 19 demos covering 18 UI extensions")
    for demo in demos:
        for required in [demo["entry"], "Reference/" + demo["id"] + ".png"]:
            if required not in files:
                raise ValueError("Missing demo input/reference: " + required)
    dependencies = json.loads((ROOT / "scripts/dependencies.json").read_text())
    profiles = json.loads((ROOT / "scripts/testkit/me-sources.json").read_text())
    for profile in profiles:
        release = next(item for item in dependencies if item["archive"] == "KK_Plugins_" + profile["tag"] + ".zip")
        inner_name = "KK_MaterialEditor_v" + profile["version"] + ".zip"
        release_data = read_zip(fetch(release, args.cache))
        me_files = read_zip(release_data[inner_name])
        reference = next(item for item in release["files"] if item.get("inner_archive") == inner_name)
        checked(me_files[reference["member"]], reference["sha256"], inner_name)
        source = fetch(profile, args.cache)
        with zipfile.ZipFile(io.BytesIO(source)) as archive:
            license_name = next(name for name in archive.namelist() if name.endswith("/LICENSE") and name.count("/") == 1)
            license_data = archive.read(license_name)
        install = dict(plugin)
        install.update(me_files)
        install["Licenses/MaterialEditor/LICENSE.txt"] = license_data
        install["Licenses/MaterialEditor/" + profile["archive"]] = source
        install["Licenses/MaterialEditor/SOURCE.txt"] = (
            "Unmodified official KK MaterialEditor " + profile["version"] + ".\n"
            "License: GPL-3.0. Corresponding source is included beside this file.\n"
            + profile["url"] + "\nSHA256 " + profile["sha256"] + "\n"
            "Binary release: " + release["url"] + "\n").encode("utf-8")
        instructions = "AssetImport {version} + MaterialEditor {me}\nClose the game and Studio. Copy BepInEx and runtimes into the KK game root.\n".format(version=version, me=profile["version"])
        install["安装说明.txt"] = instructions.replace("\n", "\r\n").encode("utf-8-sig")
        installer_name = "KK_AssetImport_" + build_id + "_" + profile["profile"] + "_Packed.zip"
        files["Installers/" + installer_name] = zip_bytes(install)
        profile["installer"] = "Installers/" + installer_name
        profile["material_editor_sha256"] = reference["sha256"]
    checker = ROOT / "tools/AssetImport.DemoCheck/bin/Release/net462"
    checker_sources = ROOT / "tools/AssetImport.DemoCheck"
    for name in ["Program.cs", "App.config", "AssetImport.DemoCheck.csproj"]:
        if (checker_sources / name).stat().st_mtime > (checker / "AssetImport.DemoCheck.exe").stat().st_mtime:
            raise ValueError("Rebuild the model checker before packaging: " + name + " has changed")
    for name in ["AssetImport.DemoCheck.exe", "AssetImport.DemoCheck.exe.config", "AssimpNet.dll", "LitJSON.dll"]:
        files["Diagnostic/" + name] = (checker / name).read_bytes()
    for name in ["AssimpNet.dll", "LitJSON.dll"]:
        if files["Diagnostic/" + name] != plugin[PLUGIN + name]:
            raise ValueError("Checker uses a different managed dependency: " + name)
    files["Diagnostic/" + NATIVE] = plugin[NATIVE]
    for name in ["AssimpNet.txt", "Assimp.txt", "LitJson.txt"]:
        files["Diagnostic/licenses/" + name] = plugin[PLUGIN + "licenses/" + name]
    manifest = {
        "plugin_version": version,
        "source": package_manifest["source"],
        "profiles": profiles,
        "same_assetimport_binary_in_both_profiles": True,
        "assetimport_sha256": hashlib.sha256(plugin[PLUGIN + "KK_AssetImport.dll"]).hexdigest(),
        "scope": "19 static demos / 18 UI extensions; not all historical format features",
        "known_failures": [{"demo": "lws", "mode": "plugin-io", "reason": "Companion LWO missing from cache; incomplete skeleton placeholder"}],
        "sha256": {name: hashlib.sha256(data).hexdigest() for name, data in sorted(files.items())}
    }
    files["kit-manifest.json"] = (json.dumps(manifest, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    files["SHA256SUMS.txt"] = "".join(hashlib.sha256(data).hexdigest() + "  " + name + "\n" for name, data in sorted(files.items())).encode("utf-8")
    target = args.output / ("KK_AssetImport_" + build_id + "_ME403_ME500_TestKit.zip")
    write_zip(target, {"KK_AssetImport_TestKit/" + name: data for name, data in files.items()})
    target.with_suffix(target.suffix + ".sha256").write_text(hashlib.sha256(target.read_bytes()).hexdigest() + "  " + target.name + "\n")
    print(target)
    print("{} files, {:.1f} MiB".format(len(files), target.stat().st_size / 1024 ** 2))


if __name__ == "__main__":
    main()
