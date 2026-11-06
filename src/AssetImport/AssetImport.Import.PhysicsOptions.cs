namespace AssetImport
{
    public partial class Import
    {
        internal bool PhysicsEnabledAfterImport { get; set; } = true;
        internal int PhysicsProfile { get; set; }
        internal bool PmxHairCollisionAssist { get; set; }
        internal PhysicsEnvironmentSettings PhysicsEnvironment { get; set; } = new PhysicsEnvironmentSettings();
        internal LocalizedText InitialPhysicsSelectionStatus { get; private set; } = "";

        // Called only for a fresh file import. Loading/copying saved assets must
        // preserve their stored roots and enabled state instead of UI defaults.
        internal void PrepareNewPmxPhysics(bool autoSelect, bool enabled, bool usePreset, bool hairCollisionAssist = false, PhysicsEnvironmentSettings environment = null)
        {
            if (!IsPmx || !ImportBones) return;
            PhysicsEnabledAfterImport = enabled;
            PhysicsProfile = usePreset ? 1 : 0;
            PmxHairCollisionAssist = hairCollisionAssist;
            PhysicsEnvironment = (environment ?? new PhysicsEnvironmentSettings()).Copy();
            InitialPhysicsSelectionStatus = autoSelect ? SelectPmxPhysicsRoots() : L.Message("Automatic selection is off. Select the physics roots manually.");
        }
    }
}
