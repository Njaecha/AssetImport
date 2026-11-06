using Studio;

namespace AssetImport
{
    public partial class AssetSceneController
    {
        internal AssetPhysicsEnvironment GetSelectedPhysicsEnvironment(out Asset asset)
        {
            asset = null;
            var studio = Studio.Studio.Instance;
            var selected = studio.treeNodeCtrl.selectNodes;
            if (selected == null || selected.Length != 1) return null;
            ObjectCtrlInfo info;
            if (!studio.dicInfo.TryGetValue(selected[0], out info)) return null;
            var item = info as OCIItem;
            if (item == null || !LoadedObjects.TryGetValue(item.itemInfo.dicKey, out asset)) return null;
            return item.objectItem.GetComponent<AssetPhysicsEnvironment>();
        }
    }
}
