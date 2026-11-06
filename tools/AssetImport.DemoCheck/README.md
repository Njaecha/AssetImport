# Standalone model checker

This CLI loads the same AssimpNet, LitJSON, and Windows native Assimp libraries distributed with the KK plugin. It does not load BepInEx, Unity, or MaterialEditor, and cannot certify textures, hooks, rendering, or card/scene save behavior.

Build the KK plugin first, then run `dotnet build tools/AssetImport.DemoCheck/AssetImport.DemoCheck.csproj -c Release`. The `net462` executable runs on Windows x64 with .NET Framework 4.6.2+. The `net8.0` target permits host-side validation against a separately built native Assimp library.

Options: `--kit <testkit folder>`, `--native <native Assimp library>`, `--report <report directory>`, and optional `--scene-output <directory>` (exports file-mode scenes as assjson for offline reference rendering). Defaults point at the packaged layout.

Each demo is tested in two modes. `file` reads the original file with its companion files available. `plugin-io` reproduces the current KK plugin's choice of memory import versus cached file import: same-name OBJ MTL and glTF external buffers are copied to a temporary folder, while other inputs are read from a memory stream. Both use `MakeLeftHanded | Triangulate` and retain empty bones. This is an IO simulation, not execution of the full plugin cache or Unity construction code.

The checker rejects empty meshes and `SceneFlags.Incomplete`. In Assimp 5.0.1, LWS memory import can return a skeleton placeholder after failing to find the referenced LWO. Nonzero triangle counts alone must not mark this as a success. Treat incomplete companion-file imports as failures.

Exit codes: 0 = all parse checks pass; 2 = one or more model checks fail; 1 = fatal initialization/manifest/report error. Reports include a CSV, JSON and full log. They are independent of the game's BepInEx log.
