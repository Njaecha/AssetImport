using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace AssetImport
{
    // Iterate on Unity's main thread. Workers receive only owned managed buffers.
    internal sealed class ImportTextureData
    {
        private readonly CubeLut lut;
        private static Request active;
        internal static int PendingImports { get; private set; }
        internal static LocalizedText Status { get; private set; }
        internal ImportTextureData(CubeLut lut) { this.lut = lut; }

        private sealed class Data
        {
            internal byte[] Bytes;
            internal TgaDecoder.Image Image;
            internal bool NeedsUnityDecode;
        }

        private sealed class Work
        {
            private readonly object sync = new object();
            internal volatile bool Done;
            private volatile bool cancelled;
            internal Data Result;
            internal Exception Error;
            internal Work(Func<Action, Data> action)
            {
                if (!ThreadPool.QueueUserWorkItem(_ =>
                {
                    Data result = null;
                    try { CheckCancellation(); result = action(CheckCancellation); }
                    catch (Exception error) { Error = error; }
                    finally
                    {
                        lock (sync)
                        {
                            Result = cancelled ? null : result;
                            Done = true; // Publish before the main thread reads result/error.
                        }
                    }
                })) throw new InvalidOperationException("Cannot queue texture conversion.");
            }
            private void CheckCancellation()
            {
                if (cancelled) throw new OperationCanceledException();
            }
            internal void Cancel()
            {
                lock (sync)
                {
                    cancelled = true;
                    if (Done) Result = null;
                }
            }
        }

        private sealed class Request
        {
            internal Work Work;
            internal bool Abandoned;
            internal bool Running { get { return Work != null && !Work.Done; } }
        }

        // Group snapshots before yielding. Retain only the current encoded image,
        // apply all its material references, then release it instead of caching every PNG.
        internal IEnumerator ApplyAsync(IEnumerable<TexturePath> paths, Func<bool> isAlive,
            Action<TexturePath, byte[]> apply, Action<string> warning, Action completed)
        {
            var groups = new List<List<TexturePath>>();
            var lookup = new Dictionary<string, List<TexturePath>>(StringComparer.OrdinalIgnoreCase);
            foreach (TexturePath path in paths)
            {
                if (!path.Use || !path.PathOkay()) continue;
                string key = Path.GetFullPath(path.Path) + (lut != null && path.Type == Assimp.TextureType.Diffuse ? "|kk" : "|original");
                List<TexturePath> group;
                if (!lookup.TryGetValue(key, out group))
                {
                    group = new List<TexturePath>(); lookup.Add(key, group); groups.Add(group);
                }
                group.Add(new TexturePath(path.Material, path.Type, path.Path));
            }
            PendingImports++;
            int failures = 0;
            bool finished = false;
            try
            {
                for (int i = 0; i < groups.Count && isAlive(); i++)
                {
                    var group = groups[i];
                    TexturePath first = group[0];
                    Status = L.Message("Importing textures {0}/{1}. Wait before saving.", i + 1, groups.Count);
                    byte[] bytes = null;
                    Exception error = null;
                    IEnumerator read = ReadAsync(first.Path, lut != null && first.Type == Assimp.TextureType.Diffuse,
                        isAlive, (data, failure) => { bytes = data; error = failure; });
                    // Propagate coroutine disposal so a stopped import cancels its worker.
                    try { while (read.MoveNext()) yield return read.Current; }
                    finally { (read as IDisposable)?.Dispose(); }
                    if (!isAlive()) yield break;
                    if (error != null)
                    {
                        failures++;
                        warning("Texture conversion failed: " + first.Path + ": " + error.Message);
                        continue;
                    }
                    foreach (TexturePath path in group)
                    {
                        if (!isAlive()) yield break;
                        try { apply(path, bytes); }
                        catch (Exception failure)
                        {
                            failures++;
                            warning("Texture assignment failed: " + path.Path + ": " + failure.Message);
                        }
                        yield return null; // At most one MaterialEditor upload per frame.
                    }
                    bytes = null;
                }
                if (!isAlive()) yield break;
                finished = true;
                Status = failures == 0 ? L.Message("Textures ready.") : L.Message("Textures finished with {0} error(s). See log.", failures);
                completed();
            }
            finally
            {
                PendingImports--;
                if (PendingImports > 0) Status = L.Message("Importing textures. Wait before saving.");
                else if (!finished) Status = L.Message("Texture import cancelled.");
            }
        }

        private IEnumerator ReadAsync(string path, bool convert, Func<bool> isAlive, Action<byte[], Exception> completed)
        {
            // Keep the slot until a cancelled worker exits, even if its coroutine stops.
            // Full-size working images are bounded across simultaneous imports too.
            while (active != null)
            {
                if (active.Abandoned && !active.Running) { active = null; break; }
                if (!isAlive()) yield break;
                yield return null;
            }
            if (!isAlive()) yield break;
            var request = new Request();
            active = request;
            try
            {
                Exception error = null;
                try
                {
                    request.Work = new Work(check =>
                    {
                        byte[] bytes = File.ReadAllBytes(path);
                        check();
                        var image = TextureImageData.Decode(path, bytes);
                        if (image != null) return Encode(image, convert, check);
                        return new Data { Bytes = bytes, NeedsUnityDecode = convert };
                    });
                }
                catch (Exception failure) { error = failure; }
                if (error != null) { completed(null, error); yield break; }
                yield return null;
                while (!request.Work.Done && isAlive()) yield return null;
                if (!isAlive()) yield break;
                if (request.Work.Error != null) { completed(null, request.Work.Error); yield break; }
                Data data = request.Work.Result;
                request.Work = null;
                if (data.NeedsUnityDecode)
                {
                    try
                    {
                        data.Image = DecodeUnity(data.Bytes);
                        data.Bytes = null;
                        request.Work = new Work(check => Encode(data.Image, true, check));
                    }
                    catch (Exception failure) { error = failure; }
                    if (error != null) { completed(null, error); yield break; }
                    while (!request.Work.Done && isAlive()) yield return null;
                    if (!isAlive()) yield break;
                    data.Image = null;
                    completed(request.Work.Result == null ? null : request.Work.Result.Bytes, request.Work.Error);
                }
                else completed(data.Bytes, null);
            }
            finally
            {
                request.Abandoned = true;
                if (request.Work != null) request.Work.Cancel();
                if (!request.Running) active = null;
            }
        }

        private Data Encode(TgaDecoder.Image image, bool convert, Action check)
        {
            check();
            if (convert) lut.Apply(image.Pixels, check);
            return new Data { Bytes = TgaDecoder.EncodePng(image, check) };
        }

        private static TgaDecoder.Image DecodeUnity(byte[] bytes)
        {
            Texture2D texture = KKAPI.Utilities.TextureUtils.LoadTexture(bytes, TextureFormat.RGBA32, false);
            if (!texture) throw new FormatException("Cannot decode image for KK color conversion.");
            try
            {
                // LoadImage can change JPG to RGB24 even when created as RGBA32.
                if (texture.format == TextureFormat.RGBA32 && texture.mipmapCount == 1)
                {
                    byte[] pixels = texture.GetRawTextureData();
                    if (pixels.Length != checked(texture.width * texture.height * 4))
                        throw new FormatException("Unexpected RGBA texture data length.");
                    return new TgaDecoder.Image { Width = texture.width, Height = texture.height, Pixels = pixels };
                }
                Color32[] colors = texture.GetPixels32();
                byte[] expanded = new byte[checked(colors.Length * 4)];
                for (int i = 0; i < colors.Length; i++)
                {
                    expanded[i * 4] = colors[i].r; expanded[i * 4 + 1] = colors[i].g;
                    expanded[i * 4 + 2] = colors[i].b; expanded[i * 4 + 3] = colors[i].a;
                }
                return new TgaDecoder.Image { Width = texture.width, Height = texture.height, Pixels = expanded };
            }
            finally { UnityEngine.Object.Destroy(texture); }
        }
    }
}
