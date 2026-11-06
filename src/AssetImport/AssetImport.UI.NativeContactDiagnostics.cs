using System;
using System.IO;

namespace AssetImport
{
    internal partial class AssetUI
    {
        private LocalizedText _diagnosticActionStatus;

        private LocalizedText SaveContactDiagnostics(AssetPhysicsEnvironment selected, string selectionStatus)
        {
            if (!AssetDebug.Enabled) return L.Message("Enable Debug to save diagnostics.");
            NativeContactDiagnostics.Select(selected, _environment == selected && _environmentDraft != null
                ? (bool?)_environmentDraft.NativeBreastContacts : null, selectionStatus);
            return NativeContactDiagnostics.Save("manual") ? L.Message("Saved: {0}", NativeContactDiagnostics.ReportPath) : (LocalizedText)NativeContactDiagnostics.LastError;
        }
        private LocalizedText OpenContactDiagnosticsFolder()
        {
            if (!AssetDebug.Enabled) return L.Message("Enable Debug to open diagnostics.");
            if (!NativeContactDiagnostics.Save("manual")) return NativeContactDiagnostics.LastError;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = Path.GetDirectoryName(NativeContactDiagnostics.ReportPath), UseShellExecute = true });
                return L.Message("Report: {0}", NativeContactDiagnostics.ReportPath);
            }
            catch (Exception error) { return L.Message("Report: {0}. Could not open folder: {1}", NativeContactDiagnostics.ReportPath, error.Message); }
        }
    }
}
