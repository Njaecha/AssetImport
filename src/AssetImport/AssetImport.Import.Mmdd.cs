using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AssetImport
{
    public partial class Import
    {
        internal static void InstallMmddRuntime()
        {
            try
            {
                string problem;
                string lib = MmddExportPaths.FindLibrary(BepInEx.Paths.GameRootPath, BepInEx.Paths.PluginPath,
                    AssetImport.mmddLibraryPath.Value, out problem);
                if (lib == null) return;
                using (var stream = typeof(Import).Assembly.GetManifestResourceStream("AssetImport.MmddMorphFunctions.py"))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    WriteChanged(Path.Combine(lib, "MmddExtMorphFunc_AssetImport_Pmx_Runtime.py"), reader.ReadToEnd() + "\n_register({})\n");
            }
            catch (Exception e) { Logger.LogWarning("Could not install MMDD runtime extension: " + e.Message); }
        }

        internal LocalizedText LastMmddExportStatus { get; private set; } = "";

        internal LocalizedText ExportMmddMapping()
        {
            if (!IsPmx || !ImportBones) return L.Message("Select an imported PMX item with bones.");
            LastMmddExportStatus = ExportMmddMapping(SourceIdentifier, _pmx, Renderers.OfType<SkinnedMeshRenderer>(),
                BoneNodes.Where(n => n.IsDynamicRoot).Select(n => n.GameObject.name));
            return LastMmddExportStatus;
        }

        // Rebuild an existing item's files using its saved PMX bytes and actual
        // renderer/physics selection. No new meshes, FK entries or materials.
        internal static LocalizedText RepairMmddMapping(string sourceIdentifier, IEnumerable<SkinnedMeshRenderer> renderers, IEnumerable<string> roots)
        {
            try
            {
                var model = PmxReader.Read(RamCacheUtility.GetFileBlob(sourceIdentifier));
                PmxSceneBuilder.RefreshMorphWarnings(model);
                return ExportMmddMapping(sourceIdentifier, model, renderers, roots);
            }
            catch (Exception e) { return MmddExportFailure(e); }
        }

        private static LocalizedText ExportMmddMapping(string sourceIdentifier, PmxModel model,
            IEnumerable<SkinnedMeshRenderer> renderers, IEnumerable<string> dynamicRootNames)
        {
            string backupDirectory = null;
            try
            {
                var targets = new List<PmxMorphTarget>();
                foreach (var renderer in renderers)
                {
                    if (!renderer) continue;
                    Mesh mesh = renderer.sharedMesh;
                    if (!mesh) continue;
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                        targets.Add(new PmxMorphTarget { Renderer = renderer.name, Shape = mesh.GetBlendShapeName(i) });
                }
                string sourceFileName = RamCacheUtility.GetFileName(sourceIdentifier);
                string label = Path.GetFileNameWithoutExtension(sourceFileName);
                var roots = new HashSet<string>(dynamicRootNames);
                var indices = Enumerable.Range(0, model.Bones.Length).Where(i => roots.Contains(PmxModel.BoneName(i, model.Bones[i].Name)));
                var result = PmxMmddMapping.Build(model, RamCacheUtility.GetFileBlob(sourceIdentifier), label, targets, indices);
                foreach (string warning in result.Warnings) Logger.LogWarning("PMX Auto: " + warning);
                string directory = Path.Combine(BepInEx.Paths.GameRootPath, "UserData/AssetImport/MMDD/" + result.ModelId);
                string name = "item_AssetImport_PMX_" + result.ModelId;
                string script;
                using (var stream = typeof(Import).Assembly.GetManifestResourceStream("AssetImport.MmddMorphFunctions.py"))
                using (var reader = new StreamReader(stream, Encoding.UTF8)) script = reader.ReadToEnd();
                script += "\n\n_register(" + result.TargetsJson + ")\n";
                Directory.CreateDirectory(directory);
                WriteChanged(Path.Combine(directory, name + ".json"), result.MappingJson);
                if (AssetDebug.Enabled) WriteChanged(Path.Combine(directory, name + ".report.txt"), result.ReportJson);
                string extension = "MmddExtMorphFunc_AssetImport_Pmx_" + result.ModelId + ".py";
                WriteChanged(Path.Combine(directory, extension), script);
                backupDirectory = directory;
                string problem;
                string configured = AssetImport.mmddLibraryPath == null ? "" : AssetImport.mmddLibraryPath.Value;
                string lib = MmddExportPaths.FindLibrary(BepInEx.Paths.GameRootPath, BepInEx.Paths.PluginPath, configured, out problem);
                LocalizedText status;
                if (lib != null)
                {
                    Directory.CreateDirectory(Path.Combine(lib, "mmdd"));
                    WriteChanged(Path.Combine(Path.Combine(lib, "mmdd"), name + ".json"), result.MappingJson);
                    WriteChanged(Path.Combine(lib, extension), script);
                    status = L.Message("Ready: {0} / AssetImport PMX Auto\n{1}.json\nInstalled to: {2}\nAfter updating AssetImport, restart MMDD once to load its runtime extension. Then select this ITEM and this model's PMX Auto definition.", label, name, lib);
                    Logger.LogInfo($"MMDD Auto mapping ready: {sourceFileName}; {result.BoneCount} bones, {result.IkCount} IK, {result.MorphCount} morphs; {name}; Lib={lib}. Open/restart MMDD after import, then choose this model's 'PMX Auto' definition.");
                }
                else
                {
                    status = L.Message("Mapping saved, but NOT installed into MMDD.\n{0}\nBackup: {1}\nCopy JSON to MMDD Lib/mmdd and PY to Lib, or set the path and repair again.", problem, directory);
                    Logger.LogWarning(status.English);
                }
                if (result.Warnings.Length > 0)
                    status += L.Message("\nWarnings: {0}. See the log for details.", result.Warnings.Length);
                return status;
            }
            catch (Exception e)
            {
                // An unsupported mapping must not abort an otherwise valid model import.
                return MmddExportFailure(e, backupDirectory);
            }
        }

        private static LocalizedText MmddExportFailure(Exception error, string backupDirectory = null)
        {
            LocalizedText status = L.Message("PMX Auto export failed: {0}", error.Message);
            if (backupDirectory != null) status += L.Message("\nBackup saved: {0}", backupDirectory);
            Logger.LogWarning(status.English);
            return status;
        }

        private static void WriteChanged(string path, string text)
        {
            if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == text) return;
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }
}
