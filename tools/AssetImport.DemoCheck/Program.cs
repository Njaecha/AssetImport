using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Assimp;
using LitJson;

// Standalone Assimp diagnostics. Does not load Unity, BepInEx, or MaterialEditor.
internal static class Program
{
    private static readonly PostProcessSteps Steps = PostProcessSteps.MakeLeftHanded | PostProcessSteps.Triangulate;
    private static string Argument(string[] args, string name, string fallback)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
    private static int Main(string[] args)
    {
        string kit = Path.GetFullPath(Argument(args, "--kit", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..")));
        string native = Path.GetFullPath(Argument(args, "--native", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes/win-x64/native/assimp.dll")));
        string report = Path.GetFullPath(Argument(args, "--report", Path.Combine(kit, "Reports", "ModelCheck_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"))));
        string sceneOutput = Argument(args, "--scene-output", null);
        if (sceneOutput != null) sceneOutput = Path.GetFullPath(sceneOutput);
        Directory.CreateDirectory(report);
        using (var log = new StreamWriter(Path.Combine(report, "model-check.log"), false, new UTF8Encoding(true)))
        {
            Action<string> write = text => { Console.WriteLine(text); log.WriteLine(text); log.Flush(); };
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                write("AssetImport standalone model check. PASS here is NOT a Windows game/render/save test.");
                write("Time: " + DateTime.Now.ToString("o") + " | OS: " + Environment.OSVersion + " | process: " + (IntPtr.Size * 8) + " bit");
                if (IntPtr.Size != 8) throw new InvalidOperationException("Run this checker as a 64-bit process.");
                if (!File.Exists(native)) throw new FileNotFoundException("Bundled native Assimp library is missing", native);
                if (!Assimp.Unmanaged.AssimpLibrary.Instance.LoadLibrary(native)) throw new InvalidOperationException("Native Assimp failed to load: " + native);
                var library = Assimp.Unmanaged.AssimpLibrary.Instance;
                write("Native Assimp: " + library.GetVersionMajor() + "." + library.GetVersionMinor() +
                    " (revision " + library.GetVersionRevision().ToString("X8") + ") | " + native);
                write("Managed wrapper: " + typeof(AssimpContext).Assembly.FullName);
                var manifest = JsonMapper.ToObject(File.ReadAllText(Path.Combine(kit, "models.json")));
                var rows = new List<Dictionary<string, object>>();
                using (var csv = new StreamWriter(Path.Combine(report, "model-check.csv"), false, new UTF8Encoding(true)))
                {
                    csv.WriteLine("id,format,entry,mode,result,meshes,vertices,triangles,bones,animations,textures,error");
                    foreach (JsonData demo in manifest["demos"])
                    {
                        string id = (string)demo["id"], format = (string)demo["format"], relative = (string)demo["entry"];
                        string path = Path.Combine(kit, relative.Replace('/', Path.DirectorySeparatorChar));
                        foreach (string mode in new[] { "plugin-io", "file" })
                        {
                            // Do not let a previous file importer make companion-file
                            // lookups accidentally succeed for a later memory import.
                            Directory.SetCurrentDirectory(kit);
                            var row = new Dictionary<string, object> { { "id", id }, { "format", format }, { "entry", relative }, { "mode", mode } };
                            string temp = null;
                            try
                            {
                                using (var importer = new AssimpContext())
                                {
                                    importer.SetConfig(new Assimp.Configs.RemoveEmptyBonesConfig(false));
                                    Scene scene = mode == "file" ? importer.ImportFile(path, Steps) : ImportLikePlugin(importer, path, out temp);
                                    if (scene == null || scene.RootNode == null || scene.MeshCount == 0) throw new InvalidDataException("No model geometry was returned.");
                                    if ((scene.SceneFlags & SceneFlags.Incomplete) != 0)
                                        throw new InvalidDataException("Incomplete scene: Assimp may have generated a skeleton placeholder instead of reading the referenced model. Do not count this as a successful import.");
                                    int triangles = scene.Meshes.Sum(mesh => mesh.Faces.Count(face => face.IndexCount == 3));
                                    if (triangles == 0) throw new InvalidDataException("No triangle surfaces were returned.");
                                    var textures = new List<string>();
                                    foreach (Material material in scene.Materials)
                                    {
                                        if (material.HasTextureDiffuse) textures.Add(material.TextureDiffuse.FilePath);
                                        if (material.HasTextureNormal) textures.Add(material.TextureNormal.FilePath);
                                    }
                                    row["result"] = "PASS"; row["meshes"] = scene.MeshCount;
                                    row["vertices"] = scene.Meshes.Sum(mesh => mesh.VertexCount); row["triangles"] = triangles;
                                    row["bones"] = scene.Meshes.Sum(mesh => mesh.BoneCount); row["animations"] = scene.AnimationCount;
                                    row["textures"] = string.Join(" | ", textures.Distinct()); row["error"] = "";
                                    if (sceneOutput != null && mode == "file")
                                    {
                                        Directory.CreateDirectory(sceneOutput);
                                        if (!importer.ExportFile(scene, Path.Combine(sceneOutput, id + ".assjson"), "assjson")) throw new IOException("Reference scene export failed.");
                                    }
                                }
                            }
                            catch (Exception exception)
                            {
                                row["result"] = "FAIL"; row["error"] = exception.Message;
                                write(id + " [" + mode + "] " + exception);
                            }
                            finally
                            {
                                // Windows scanners can briefly keep a temp file open.
                                // A cleanup failure must not discard this row or the rest of the run.
                                if (temp != null && Directory.Exists(temp))
                                {
                                    try { Directory.Delete(temp, true); }
                                    catch (Exception exception) { write("WARNING: Could not remove temporary folder " + temp + ": " + exception.Message); }
                                }
                            }
                            rows.Add(row);
                            string[] columns = { "id", "format", "entry", "mode", "result", "meshes", "vertices", "triangles", "bones", "animations", "textures", "error" };
                            csv.WriteLine(string.Join(",", columns.Select(key => Csv(row.ContainsKey(key) ? row[key] : "")))); csv.Flush();
                            write(id + " [" + mode + "] " + row["result"]);
                        }
                    }
                }
                File.WriteAllText(Path.Combine(report, "model-check.json"), JsonMapper.ToJson(rows), new UTF8Encoding(false));
                int failed = rows.Count(row => (string)row["result"] == "FAIL");
                write("Done: " + (rows.Count - failed) + " passed, " + failed + " failed. Report: " + report);
                write("plugin-io only mirrors file/memory selection and cached companions. It does not run the plugin or Unity.");
                return failed == 0 ? 0 : 2;
            }
            catch (Exception exception) { write("FATAL: " + exception); return 1; }
        }
    }
    private static string Csv(object value) => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\"";
    private static Scene ImportLikePlugin(AssimpContext importer, string path, out string temp)
    {
        temp = null;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        var companions = new List<string>();
        if (extension == ".obj")
        {
            string mtl = Path.ChangeExtension(path, ".mtl");
            if (File.Exists(mtl)) companions.Add(mtl);
        }
        else if (extension == ".gltf")
        {
            JsonData gltf = JsonMapper.ToObject(File.ReadAllText(path));
            if (gltf.ContainsKey("buffers"))
                foreach (JsonData buffer in gltf["buffers"]) companions.Add(Path.Combine(Path.GetDirectoryName(path), (string)buffer["uri"]));
        }
        if (companions.Count > 0)
        {
            temp = Path.Combine(Path.GetTempPath(), "AssetImport_DemoCheck_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            string main = Path.Combine(temp, Path.GetFileName(path));
            File.Copy(path, main);
            foreach (string companion in companions) File.Copy(companion, Path.Combine(temp, Path.GetFileName(companion)));
            return importer.ImportFile(main, Steps);
        }
        using (var stream = new MemoryStream(File.ReadAllBytes(path))) return importer.ImportFileFromStream(stream, Steps, extension.TrimStart('.'));
    }
}
