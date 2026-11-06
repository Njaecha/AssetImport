using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AssetImport;

internal static class Program
{
    private const string Model = "mtllib mesh.mtl\no Mesh\nv 0 0 0\n";
    private const string Material = "newmtl Example\nKd 1 0 0\n";
    private static readonly byte[] ModelBytes = Encoding.UTF8.GetBytes(Model);
    private static readonly byte[] MaterialBytes = Encoding.UTF8.GetBytes(Material);

    private static int Main()
    {
        var tests = new Action<string>[]
        {
            ObjMaterialHashesAndContents,
            SameContentsReuseFirstFilename,
            SavedFilesRestoreWithoutSourceFiles,
            GltfBuffersRestoreWithoutSourceFiles,
            PmxBlobRestoresWithoutOriginalDirectory,
            MissingFilesAndUnknownHashesFailCleanly
        };
        foreach (var test in tests)
        {
            string directory = Path.Combine(Path.GetTempPath(), "assetimport-cache-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            RamCacheUtility.ClearCache();
            try
            {
                test(directory);
                Console.WriteLine("PASS " + test.Method.Name);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL " + test.Method.Name + ": " + exception);
                return 1;
            }
            finally
            {
                RamCacheUtility.ClearCache();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
        Console.WriteLine(tests.Length + " cache regression tests passed.");
        return 0;
    }

    private static void ObjMaterialHashesAndContents(string directory)
    {
        string hash = CacheObj(directory);
        Equal("3648518548CA26E6CF7BA62D9FD83FA4", hash, "OBJ MD5");
        Equal("mesh.obj", RamCacheUtility.GetFileName(hash), "OBJ filename");
        Bytes(ModelBytes, RamCacheUtility.GetFileBlob(hash), "OBJ contents");
        List<string> related = RamCacheUtility.GetFileAdditionalFileHashes(hash);
        Equal(1, related.Count, "MTL association count");
        Equal("2B722DF8437CFA1C76609FBBCB85BB23", related[0], "MTL MD5");
        Equal("mesh.mtl", RamCacheUtility.GetFileName(related[0]), "MTL filename");
        Bytes(MaterialBytes, RamCacheUtility.GetFileBlob(related[0]), "MTL contents");
        using (MemoryStream stream = RamCacheUtility.GetFileStream(hash))
            Bytes(ModelBytes, stream.ToArray(), "OBJ stream contents");
    }

    private static void SameContentsReuseFirstFilename(string directory)
    {
        string originalHash = CacheObj(directory);
        string copyPath = Path.Combine(directory, "copy.obj");
        File.WriteAllBytes(copyPath, ModelBytes);
        string copyHash = RamCacheUtility.ToCache(copyPath);
        Equal(originalHash, copyHash, "content deduplication");
        Equal("mesh.obj", RamCacheUtility.GetFileName(copyHash), "first filename preserved");
        Equal(1, RamCacheUtility.GetFileAdditionalFileHashes(copyHash).Count, "material association preserved");
    }

    private static void SavedFilesRestoreWithoutSourceFiles(string directory)
    {
        string hash = CacheObj(directory);
        var savedModel = Snapshot(hash);
        var savedMaterial = Snapshot(savedModel.RelatedFiles.Single());
        Directory.Delete(directory, true);
        RamCacheUtility.ClearCache();
        Equal(null, RamCacheUtility.GetFileBlob(hash), "cache cleared");
        // Card / scene loading repopulates the cache from AssetFile records.
        RamCacheUtility.ToCache(savedModel);
        RamCacheUtility.ToCache(savedMaterial);
        Bytes(ModelBytes, RamCacheUtility.GetFileBlob(hash), "restored OBJ contents");
        string materialHash = RamCacheUtility.GetFileAdditionalFileHashes(hash).Single();
        Equal(savedMaterial.Hash, materialHash, "restored MTL association");
        Equal("mesh.mtl", RamCacheUtility.GetFileName(materialHash), "restored MTL filename");
        Bytes(MaterialBytes, RamCacheUtility.GetFileBlob(materialHash), "restored MTL contents");
        // The importer's byte-array path must retain the same content identity too.
        Equal(hash, RamCacheUtility.ToCache(savedModel.File, savedModel.FileName, savedModel.RelatedFiles), "restored blob identity");
    }

    private static void GltfBuffersRestoreWithoutSourceFiles(string directory)
    {
        byte[] buffer = { 0, 1, 2, 127, 128, 255 };
        const string gltf = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"mesh.bin\",\"byteLength\":6}]}";
        File.WriteAllBytes(Path.Combine(directory, "mesh.gltf"), Encoding.UTF8.GetBytes(gltf));
        File.WriteAllBytes(Path.Combine(directory, "mesh.bin"), buffer);
        string hash = RamCacheUtility.ToCache(Path.Combine(directory, "mesh.gltf"));
        AssetFile savedModel = Snapshot(hash);
        AssetFile savedBuffer = Snapshot(savedModel.RelatedFiles.Single());
        Directory.Delete(directory, true);
        RamCacheUtility.ClearCache();
        RamCacheUtility.ToCache(savedBuffer);
        RamCacheUtility.ToCache(savedModel);
        Bytes(Encoding.UTF8.GetBytes(gltf), RamCacheUtility.GetFileBlob(hash), "restored glTF contents");
        string bufferHash = RamCacheUtility.GetFileAdditionalFileHashes(hash).Single();
        Equal("mesh.bin", RamCacheUtility.GetFileName(bufferHash), "restored glTF buffer filename");
        Bytes(buffer, RamCacheUtility.GetFileBlob(bufferHash), "restored glTF buffer contents");
    }

    private static void PmxBlobRestoresWithoutOriginalDirectory(string directory)
    {
        // Cache stores opaque bytes. The PMX project separately parses an actual
        // PMX blob; here check that the upper-case extension survives scene data.
        byte[] pmx = Encoding.UTF8.GetBytes("PMX original cache fixture");
        string file = Path.Combine(directory, "モデル.Pmx");
        File.WriteAllBytes(file, pmx);
        string hash = RamCacheUtility.ToCache(file);
        AssetFile saved = Snapshot(hash);
        Directory.Delete(directory, true);
        RamCacheUtility.ClearCache();
        RamCacheUtility.ToCache(saved);
        Equal("モデル.Pmx", RamCacheUtility.GetFileName(hash), "PMX extension/name retained");
        Bytes(pmx, RamCacheUtility.GetFileBlob(hash), "PMX cached source independent of original file");
        Equal(null, RamCacheUtility.GetFileAdditionalFileHashes(hash), "PMX geometry does not depend on sidecar files");
    }

    private static void MissingFilesAndUnknownHashesFailCleanly(string directory)
    {
        Equal(null, RamCacheUtility.ToCache(Path.Combine(directory, "missing.obj")), "missing source");
        Equal(null, RamCacheUtility.GetFileBlob("unknown"), "unknown blob");
        Equal(null, RamCacheUtility.GetFileName("unknown"), "unknown filename");
        Equal(null, RamCacheUtility.GetFileStream("unknown"), "unknown stream");
        Equal(null, RamCacheUtility.GetFileAdditionalFileHashes("unknown"), "unknown associations");
        Equal(false, new AssetFile().AutoFill("unknown"), "unknown snapshot");
    }

    private static string CacheObj(string directory)
    {
        File.WriteAllBytes(Path.Combine(directory, "mesh.obj"), ModelBytes);
        File.WriteAllBytes(Path.Combine(directory, "mesh.mtl"), MaterialBytes);
        return RamCacheUtility.ToCache(Path.Combine(directory, "mesh.obj"));
    }

    private static AssetFile Snapshot(string hash)
    {
        var saved = new AssetFile();
        Equal(true, saved.AutoFill(hash), "snapshot created");
        // Keep independent copies, as deserialization creates when loading a saved card.
        saved.File = saved.File.ToArray();
        saved.RelatedFiles = saved.RelatedFiles == null ? null : new List<string>(saved.RelatedFiles);
        return saved;
    }

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(description + ": expected " + expected + ", got " + actual);
    }

    private static void Bytes(byte[] expected, byte[] actual, string description)
    {
        if (actual == null || !expected.SequenceEqual(actual))
            throw new InvalidOperationException(description + ": byte arrays differ");
    }
}
