using AssetImport;

string root = Path.Combine(Path.GetTempPath(), "assetimport-mmdd-paths-" + Guid.NewGuid().ToString("N"));
string plugins = Path.Combine(root, "BepInEx", "plugins");
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
}
void CreateLibrary(string directory)
{
    Directory.CreateDirectory(directory);
    foreach (string name in new[] { "vmdlib.py", "mmddirector.py", "mmddmorphfuncext.py" })
        File.WriteAllText(Path.Combine(directory, name), "# Test fixture, never an MMDD distribution.\n");
}
try
{
    Directory.CreateDirectory(plugins);
    string found = MmddExportPaths.FindLibrary(root, plugins, "", out string message);
    Check(found == null && message.Contains("not found"), "missing installation reports no destination");

    string standard = Path.Combine(plugins, "Console", "Lib");
    CreateLibrary(standard);
    found = MmddExportPaths.FindLibrary(root, plugins, "", out message);
    Check(found == standard && message == null, "standard layout detected");

    string moved = Path.Combine(plugins, "工具", "MMDD 3", "Console", "Lib");
    Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
    Directory.Move(standard, moved);
    found = MmddExportPaths.FindLibrary(root, plugins, " ", out message);
    Check(found == moved && message == null, "nested, renamed installation supports spaces and Unicode");

    string incomplete = Path.Combine(plugins, "Other", "Lib");
    Directory.CreateDirectory(incomplete);
    File.WriteAllText(Path.Combine(incomplete, "vmdlib.py"), "# incomplete");
    found = MmddExportPaths.FindLibrary(root, plugins, "", out message);
    Check(found == moved, "incomplete install is not a candidate");

    CreateLibrary(standard);
    found = MmddExportPaths.FindLibrary(root, plugins, "", out message);
    Check(found == null && message.Contains(standard) && message.Contains(moved), "several installations report ambiguity");

    found = MmddExportPaths.FindLibrary(root, plugins, moved, out message);
    Check(found == moved && message == null, "configured path resolves ambiguity");
    found = MmddExportPaths.FindLibrary(root, plugins, " \"BepInEx/plugins/Console/Lib\" ", out message);
    Check(found == standard, "relative configured path uses game root and accepts pasted quotes");

    found = MmddExportPaths.FindLibrary(root, plugins, incomplete, out message);
    Check(found == null && message.Contains("not a valid"), "invalid explicit path never falls back to a different install");

    string external = Path.Combine(root, "custom-console", "Lib");
    CreateLibrary(external);
    found = MmddExportPaths.FindLibrary(root, plugins, external, out message);
    Check(found == external, "explicit path supports Lib outside plugins");

    Directory.Delete(standard, true);
    Directory.Delete(moved, true);
    found = MmddExportPaths.FindLibrary(root, plugins, "", out message);
    Check(found == null, "auto discovery does not scan arbitrary game directories");

    string tooDeep = Path.Combine(plugins, "1", "2", "3", "4", "5", "6", "Lib");
    CreateLibrary(tooDeep);
    found = MmddExportPaths.FindLibrary(root, plugins, "", out message);
    Check(found == null, "recursive search is bounded");
    found = MmddExportPaths.FindLibrary(root, plugins, tooDeep, out message);
    Check(found == tooDeep, "explicit path bypasses discovery depth bound");

    // Symlinks/junctions should not import a second game's installation or loop.
    try
    {
        Directory.CreateSymbolicLink(Path.Combine(plugins, "linked-game"), external);
        found = MmddExportPaths.FindLibrary(root, plugins, "", out message);
        Check(found == null, "auto discovery ignores reparse points");
    }
    catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314)
    {
        Console.WriteLine("SKIP: reparse-point check requires Windows Developer Mode or the symbolic-link privilege.");
    }
    Console.WriteLine($"PASS: {checks} MMDD path checks");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
