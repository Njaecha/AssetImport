namespace AssetImport
{
    // A single process-wide switch; never serialized into a model or scene.
    // Configuration callbacks only set the flag. Unity inspection and report
    // writes remain on the main thread through the normal update hooks.
    internal static class AssetDebug
    {
        internal static volatile bool Enabled = false;
    }
}
