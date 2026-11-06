using System;
using System.Collections.Generic;
using System.IO;

namespace AssetImport
{
    // MMDD resolves definitions relative to the active vmdlib.py, not the game
    // working directory. Do not silently choose between several installations.
    internal static class MmddExportPaths
    {
        internal static string FindLibrary(string gameRoot, string pluginRoot, string configured, out string message)
        {
            if (!string.IsNullOrEmpty(configured) && configured.Trim().Length > 0)
            {
                string path = configured.Trim().Trim('"');
                if (!Path.IsPathRooted(path)) path = Path.Combine(gameRoot, path);
                path = Path.GetFullPath(path);
                if (IsLibrary(path)) { message = null; return path; }
                message = "MMDD Library Path is not a valid MMDD 3 Lib directory: " + path;
                return null;
            }

            var matches = new List<string>();
            FindLibraries(pluginRoot, 0, matches);
            matches.Sort(StringComparer.OrdinalIgnoreCase);
            if (matches.Count == 1) { message = null; return matches[0]; }
            if (matches.Count == 0)
                message = "MMDD 3 Lib was not found under BepInEx/plugins. Set PMX / MMDD Library Path to the active MMDD Lib directory, then repair again.";
            else
                message = "Several MMDD Lib directories were found. Set PMX / MMDD Library Path to the active one, then repair again:\n" + string.Join("\n", matches.ToArray());
            return null;
        }

        private static bool IsLibrary(string path)
        {
            return File.Exists(Path.Combine(path, "vmdlib.py"))
                && File.Exists(Path.Combine(path, "mmddirector.py"))
                && File.Exists(Path.Combine(path, "mmddmorphfuncext.py"));
        }

        private static void FindLibraries(string directory, int depth, List<string> matches)
        {
            if (!Directory.Exists(directory)) return;
            if (IsLibrary(directory))
            {
                matches.Add(Path.GetFullPath(directory));
                return;
            }
            if (depth >= 6) return;
            string[] children;
            try { children = Directory.GetDirectories(directory); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            foreach (string child in children)
            {
                try
                {
                    // Avoid junction loops and scanning a second game through a link.
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                    FindLibraries(child, depth + 1, matches);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
