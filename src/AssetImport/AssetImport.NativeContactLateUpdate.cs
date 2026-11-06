using UnityEngine;

namespace AssetImport
{
    // A single execution order cannot provide both an early Update restore and
    // a late LateUpdate write. Camera callbacks run in the rendering phase and
    // are too late to be the normal source of skinned-mesh bone motion.
    [DefaultExecutionOrder(32000)]
    internal sealed class NativeContactLateUpdate : MonoBehaviour
    {
        public NativeContactController Controller;

        private void LateUpdate()
        {
            if (Controller && Controller.isActiveAndEnabled) Controller.ApplyLateUpdate();
        }
    }
}
