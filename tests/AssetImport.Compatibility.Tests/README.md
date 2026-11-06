# MaterialEditor compatibility regression

Requires the .NET 8 SDK and the pinned references prepared by `python3 scripts/prepare_dependencies.py`. Build the production plugin before running this test, from the repository root:

```sh
python3 scripts/prepare_dependencies.py
dotnet build src/AssetImport/AssetImport.csproj -c Release
dotnet run --project tests/AssetImport.Compatibility.Tests -c Release
```

Use the same configuration for both commands. The test copies the built KK/KKS plugin DLLs from `src/AssetImport/bin/<configuration>`; it deliberately fails if they are missing or still reference the wrong MaterialEditor baseline.

This console test exits with a nonzero status when a compatibility check fails. It links the **production** `AssetImport.MaterialEditorLoadDataPatch.cs`; it does not duplicate the matching/rewrite algorithm.

The harness reads these real DLL fixtures as PE metadata:

| Target | MaterialEditor fixture | Compiled minimum |
| --- | --- | --- |
| KK baseline | 4.0.3.0 | 4.0.3 |
| KK newer version regression | 5.0.0.0 | 4.0.3 |
| KKS | 3.13.5.0 | 3.13.5 |

For each fixture, it verifies the compiled plugin's actual `BepInDependency` attribute and MaterialEditor assembly reference, then resolves all directly linked MaterialEditor types and methods against the target DLL. Method signatures include calling convention, generic arity, parameters and return types. This covers both character and Studio texture import APIs, beyond the Harmony hook targets. Referenced enum constants are compared with the compilation baseline because values such as `ObjectType.Accessory` are inlined into the plugin.

It also checks the actual hook target signatures, the iterator's captured controller/accessory/body fields, and the `MoveNext` instructions. Game reference assemblies from the pinned NuGet packages supply the `Studio.SceneInfo.Load(string)` signature check. Restore uses the repository's `NuGet.Config` and this project's committed `packages.lock.json`; the legacy BepInEx dependency is excluded from runtime and used only to resolve the game metadata fixture packages.

The real instruction stream is adapted to inert reflection fixtures and passed into the production patcher. The resulting guard/callback/anchor fragment is emitted and executed under .NET 8 for all body/accessory flag combinations. The assertions check the captured receiver, flag values, callback order, unchanged body condition, and incoming branch label transfer. Unknown call anchors, branch destinations, and captured receiver layouts must preserve the original instructions and labels.

The built KK plugin is also checked for CLR 2 core references and absence of `System.IO.InvalidDataException`, which fails to load in Unity 5.6 before node transform conversion can complete. This check covers the required CLR 2 references; it does not verify every Unity framework type.

Game, built AssetImport, and MaterialEditor assemblies are inspected only as metadata; only the linked patcher and inert probes execute. This checks the binary API and cross-version patch contracts, including the emitted dependency minimum; it does not replace Windows/Unity/Mono testing of assembly binding or full character, coordinate, and Studio loading.
