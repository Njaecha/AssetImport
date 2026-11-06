# AssetImport

[English](README.md) | [简体中文](README.zh-CN.md)

Import external 3D models into **Koikatsu (KK)** and **Koikatsu Sunshine (KKS)** as Studio items or character accessories. AssetImport uses [Assimp](https://assimp.org/) for general formats and a dedicated reader for PMX 2.0.

This project extends [Njaecha/AssetImport](https://github.com/Njaecha/AssetImport) with KK support, PMX import, MMDD animation mapping, and DynamicBone integration.

## Features

- Import meshes, materials, textures, armatures, and supported blend shapes from formats such as FBX, OBJ, glTF, and PMX.
- Save imported assets with Studio scenes and character accessories.
- Generate MMDD 3 mappings for PMX bone animation and vertex morphs.
- Create DynamicBone chains and body colliders from supported PMX metadata; control physics from the import panel.
- Set gravity response and shape retention per physics chain, with supported PMX angular limits, ground collision, and initial settling.
- Optionally enable experimental breast contacts at physics branch endpoints, mark chains manually, and adjust sphere or capsule volumes with mirrored controls and previews.
- Decode BMP, TGA, and common DDS textures, with optional `.cube` LUT conversion for diffuse colors.
- Prepare textures asynchronously and apply them through MaterialEditor.

Imported models remain items or accessories; they do not become native game characters with clothing slots.

## Requirements

Windows x64 and the matching game's plugins:

| Dependency | KK | KKS |
| --- | --- | --- |
| BepInEx | 5.4.22 or later in the 5.x series | 5.4.22 or later in the 5.x series |
| Game API | KKAPI 1.45.1+ | KKSAPI 1.43.0+ |
| MaterialEditor | 4.0.3+ | 3.13.5+ |
| LoadFileLimitedFix | KK build | KKS build |

MMDD 3 is optional and required only for VMD playback. DynamicBoneEditor / KKPE / KKSPE provide optional editing integrations. Prerequisite plugins are installed separately.

## Installation

1. Download the matching `KK_AssetImport_*_Packed.zip` or `KKS_AssetImport_*_Packed.zip` from [Releases](https://github.com/PoeraWork/AssetImport/releases), or [build from source](CONTRIBUTING.md).
2. Close the game and Studio.
3. Extract both `BepInEx` and `runtimes` into the game root, preserving their directory structure.
4. Start the game and press **Left Alt + I**. In the character editor, select an existing accessory first.

When upgrading, replace the existing installation and move obsolete plugin copies out of `BepInEx/plugins`. KK and KKS use different runtime libraries; install the package for your game. ZIP files do not belong in `mods`.

Package instructions: [KK](docs/INSTALL-KK.txt) · [KKS](docs/INSTALL-KKS.txt).

Development builds are available as `packages-…` artifacts from successful [Actions runs](https://github.com/PoeraWork/AssetImport/actions/workflows/build.yml), retained for 30 days. A GitHub account is required to download them. See [CI and releases](CONTRIBUTING.md#ci-and-releases) for build and release instructions.

## Usage

In the plugin settings list (BepInEx Configuration Manager), open **KK_AssetImport / KKS_AssetImport → _General_ → Language** to choose **English** or **简体中文**. English is the default; changes apply immediately, including pending status messages. No translation plugin is required. The same setting is available in `BepInEx/config/org.njaecha.plugins.assetimport.cfg`. Diagnostic logs retain their original identifiers and technical details.

Choose a model, adjust the import options and scale, then inspect the preview and finish importing. Keep companion textures beside the model in their original folders. Wait for **Textures ready** before saving, copying the object, or switching coordinates.

For PMX, start with **PMX / VMD preset** and a scaling factor of **1**. In Studio, use MMDD's **Load VMD Advanced** to select the generated **AssetImport PMX Auto** definition. See the [PMX guide](docs/PMX-SUPPORT.md) for mapping, physics, texture conversion, and troubleshooting.

PMX support is partial: PMX 2.1, full MMD rigid-body/joint simulation, toon/sphere shading, and bone/UV/material morphs are not implemented. SDEF uses linear skinning. Invalid optional constraints can be skipped with diagnostics, so a successful import does not guarantee identical MMD behavior.

## Development and support

See [CONTRIBUTING.md](CONTRIBUTING.md) for prerequisites, builds, automated checks, and packaging.

**Debug** in the main panel is off by default. It controls detailed logging, physics overlays, diagnostic reports and asset dumps. The same setting is available as **_General_ / Debug** in the plugin configuration. Enable it when adjusting contact volumes or investigating a problem; normal physics and error messages work with it off. See the [diagnostic guide](docs/PMX-SUPPORT.md#debug-mode) for report locations.

Report bugs through [Issues](https://github.com/PoeraWork/AssetImport/issues). Include the game, plugin build from `BUILD.txt` or the Debug panel, reproduction steps, and relevant `BepInEx/LogOutput.log` output (or `output_log.txt`). For animation problems, include the frame range and import settings. Share models or motions only when their license permits it.

## Credits

- [Njaecha/AssetImport](https://github.com/Njaecha/AssetImport) — original plugin.
- [Assimp](https://github.com/assimp/assimp), AssimpNet, and [AssimpNetter](https://github.com/Njaecha/AssimpNetter) — model loading.
- [IllusionMods](https://github.com/IllusionMods) — game APIs and plugin integrations.

Third-party license notices are available in [docs/licenses](docs/licenses) and the binary packages.
