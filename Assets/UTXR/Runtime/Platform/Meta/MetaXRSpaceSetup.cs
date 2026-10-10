using UnityEngine;

using UTXR.Core;
namespace UTXR.Platform.Meta
{
    #if META_XR_SDK
    
    public class MetaXRSpaceSetup : MonoBehaviour
    {
        public void RequestSpaceSetup()
        {
            OVRScene.RequestSpaceSetup();
        }
    }
    
    #endif
}

