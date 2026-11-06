# Regression checks

Run `python scripts/validate.py` from the repository root after preparing dependencies as described in [CONTRIBUTING.md](../CONTRIBUTING.md). The runner builds both game targets and executes C# console checks and Python unittest suites.

The small OBJ and texture under `fixtures/` can also be imported manually to inspect material binding and scene/card persistence. They are synthetic inputs, not a model compatibility catalogue.

Physics checks cover PMX joint parsing and binding, asymmetric angular limits, spring response, per-chain modes, and saved settings. The physics fixture uses engine substitutes, checks hook signatures against both game reference APIs, and executes the coefficient rewrite. It does not replace native DynamicBone playback or visual checks in Studio.

## Optional model corpus

```powershell
python scripts/check_pmx_corpus.py --model-root D:/Models --dotnet dotnet --mmdd-lib D:/MMDD/BepInEx/plugins/Console/Lib
```

`--model-root` accepts a PMX file or directory and may be repeated. The command deduplicates inputs by content hash, runs production conversion and independent mapping checks, and writes reports under `artifacts/pmx-corpus/`. The optional MMDD library enables checks against a locally installed MMDD 3 runtime. Models and reports are not required by the default suite.

## Numerical motion probe

```powershell
python scripts/mmdd/check_pmx_motion.py --pmx D:/Models/model.pmx --vmd D:/Motions/motion.vmd --mmdd-lib D:/MMDD/BepInEx/plugins/Console/Lib --frames 0:300:1 --output artifacts/motion-check.json
```

The frame range is inclusive (`start:end:step`). The probe records ankle/IK target errors and selected bone positions using MMDD frame indexing and interpolation with numerical transform substitutes. `--extension <file>` selects another runtime extension for comparison.

The probe does not evaluate skinning, physics, fixed-axis behavior, or Unity/CLR execution. It rejects motions that disable mapped IK chains. A numerical result does not replace in-game playback checks.
