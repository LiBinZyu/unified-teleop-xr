using UnityEngine;

#if META_XR_SDK

public class MetaXRSpaceSetup : MonoBehaviour
{
    public void RequestSpaceSetup()
    {
        OVRScene.RequestSpaceSetup();
    }
}

#endif