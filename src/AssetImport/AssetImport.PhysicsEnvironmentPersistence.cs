namespace AssetImport
{
    internal static class PhysicsEnvironmentPersistence
    {
        internal static PhysicsEnvironmentSettings Read(Asset asset)
        {
            return new PhysicsEnvironmentSettings { Floor = asset.FloorCollision, FloorOffset = asset.FloorOffset,
                DownwardForce = asset.DownwardForce, Settle = asset.SettlePhysics, NativeBreastContacts = asset.NativeBreastContacts,
                // Earlier builds enabled free hanging by default. Only the
                // versioned per-chain settings represent an explicit choice.
                DefaultMode = asset.ChainPhysicsVersion >= 1 ? asset.PhysicsDefaultMode : (int)PhysicsChainMode.KeepShape,
                Chains = asset.ChainPhysicsVersion >= 1 ? asset.ChainPhysicsSettings : null }.Copy();
        }

        internal static void Write(Asset asset, PhysicsEnvironmentSettings settings)
        {
            var clean = settings.Copy();
            asset.FloorCollision = clean.Floor;
            asset.FloorOffset = clean.FloorOffset;
            asset.DownwardForce = clean.DownwardForce;
            asset.SettlePhysics = clean.Settle;
            asset.NativeBreastContacts = clean.NativeBreastContacts;
            asset.NaturalDrape = clean.NaturalDrape;
            asset.ChainPhysicsVersion = 1;
            asset.PhysicsDefaultMode = clean.DefaultMode;
            asset.ChainPhysicsSettings = clean.Chains;
        }
    }
}
