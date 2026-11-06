using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace AssetImport
{
    // Capture Unity state on the main thread. Coalesce all model requests into
    // one current report, written at most once per five seconds automatically.
    internal static class NativeContactDiagnostics
    {
        private static bool _initialized, _pending, _active;
        private static float _nextWrite;
        private static string _reason, _primaryPath, _fallbackPath, _announcedPath;
        private static AssetPhysicsEnvironment _selected;
        private static bool? _draft;
        private static string _selectionStatus;
        internal static string ReportPath { get; private set; }
        internal static string LastError { get; private set; }
        internal static string Build { get; private set; }
        internal static string ShortBuild
        {
            get
            {
                string value = Build ?? "unknown";
                int start = value.IndexOf("git.", StringComparison.Ordinal);
                return start < 0 ? value : "g" + value.Substring(start + 4, Math.Min(12, value.Length - start - 4));
            }
        }
        internal static void Initialize()
        {
            _initialized = false; ReportPath = null; LastError = null;
            try
            {
                var build = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(typeof(AssetImport).Assembly, typeof(AssemblyInformationalVersionAttribute));
                Build = build == null ? "unknown" : build.InformationalVersion;
                _primaryPath = Path.Combine(BepInEx.Paths.GameRootPath, "UserData/AssetImport/Diagnostics/breast-contacts.txt");
                _fallbackPath = Path.Combine(Path.GetTempPath(), "AssetImport-Diagnostics/" + Guid.NewGuid().ToString("N") + "/breast-contacts.txt");
                _initialized = true; _announcedPath = null; _pending = false; _active = AssetDebug.Enabled;
                _nextWrite = 0;
                if (_active) Save("startup");
            }
            catch (Exception error) { Failed(error); }
        }
        internal static void Select(AssetPhysicsEnvironment selected, bool? draft, string status)
        { if (AssetDebug.Enabled) { _selected = selected; _draft = draft; _selectionStatus = status; } }
        internal static void Request(string reason) { if (AssetDebug.Enabled) { _pending = true; _reason = reason; } }
        internal static void Poll()
        {
            if (!AssetDebug.Enabled)
            {
                _active = false; _pending = false; _selected = null; _draft = null; _selectionStatus = null;
                ReportPath = null; LastError = null;
                return;
            }
            if (_initialized && !_active) { _active = true; Save("debug-enabled"); return; }
            if (_initialized && _pending && Time.realtimeSinceStartup >= _nextWrite) Save(_reason);
        }
        internal static bool Save(string reason)
        {
            if (!AssetDebug.Enabled || !_initialized) return false;
            _active = true;
            _pending = false; _nextWrite = Time.realtimeSinceStartup + 5f;
            try
            {
                string report;
                try { report = Capture(reason); }
                catch (Exception captureError)
                {
                    report = "AssetImport breast contact diagnostics\nBuild: " + Build + "\nUTC: " + DateTime.UtcNow.ToString("o") +
                        "\nGame root: " + BepInEx.Paths.GameRootPath + "\nCapture failed before model enumeration:\n" + captureError;
                }
                try { Write(_primaryPath, report); ReportPath = _primaryPath; LastError = null; }
                catch (Exception primaryError)
                {
                    Write(_fallbackPath, report + "\nPrimary report path unavailable: " + _primaryPath + "\n" + primaryError);
                    ReportPath = _fallbackPath; LastError = "Using temporary folder: " + primaryError.Message;
                }
                if (_announcedPath != ReportPath || reason == "manual")
                {
                    _announcedPath = ReportPath;
                    string line = "AssetImport diagnostics: build=" + Build + "; report=" + ReportPath;
                    try { AssetImport.Logger.LogInfo(line); } catch { }
                    // Reach the Unity log even if BepInEx routes its source
                    // messages only to LogOutput.log.
                    try { Debug.Log(line); } catch { }
                }
                return true;
            }
            catch (Exception error) { Failed(error); return false; }
        }
        private static void Write(string path, string report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report, new UTF8Encoding(true));
        }
        private static void Failed(Exception error)
        {
            if (!AssetDebug.Enabled) { LastError = null; return; }
            string message = "Could not save contact diagnostics: " + error.Message;
            if (LastError != message)
            {
                try { AssetImport.Logger.LogWarning(message); Debug.LogWarning("AssetImport: " + message); }
                catch { }
            }
            LastError = message;
        }
        private static string Capture(string reason)
        {
            var report = new StringBuilder();
            report.AppendLine("AssetImport breast contact diagnostics");
            report.AppendLine("UTC: " + DateTime.UtcNow.ToString("o") + "; reason: " + reason);
            report.AppendLine("Build: " + Build);
            report.AppendLine("Plugin: " + AssetImport.PluginName + "; Unity: " + Application.unityVersion);
            report.AppendLine("Game root: " + BepInEx.Paths.GameRootPath);
            report.AppendLine("Unity data: " + Application.dataPath);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name.IndexOf("AssetImport", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    try { report.AppendLine("Loaded assembly: " + assembly.FullName + "; path=" + assembly.Location); }
                    catch { report.AppendLine("Loaded assembly: " + assembly.FullName); }
                }
            report.AppendLine("Selection (last displayed): " + _selectionStatus);
            report.AppendLine("Selected environment: " + (_selected ? _selected.transform.name : "none"));
            if (_selected)
            {
                report.AppendLine("Applied contacts enabled: " + _selected.Settings.NativeBreastContacts);
                report.AppendLine("Panel draft contacts enabled: " + _draft);
                if (!_selected.GetComponent<NativeContactController>())
                    report.AppendLine("Selected model has no contact controller. Check PMX import, armature and physics chain selection.");
            }
            var controllers = Resources.FindObjectsOfTypeAll<NativeContactController>();
            report.AppendLine("Loaded contact controllers (including inactive models): " + controllers.Length);
            if (controllers.Length == 0)
                report.AppendLine("No contact controller was created. Reload/import a PMX model with armature and physics chains; include the import log if still absent.");
            for (int i = 0; i < controllers.Length; i++)
            {
                report.AppendLine("\n--- Controller " + i + " ---");
                try { report.AppendLine(controllers[i].BuildDiagnosticReport()); }
                catch (Exception error) { report.AppendLine("Diagnostic capture failed: " + error); }
            }
            return report.ToString();
        }
    }
}
