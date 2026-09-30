#if PICO_OPENXR
using UnityEngine;
using Unity.XR.OpenXR.Features.PICOSupport;

public class PicoPassthroughControl : MonoBehaviour
{
    public bool isPassthroughOn { get; private set; } = true;
    public bool enbaleOnStart;
    
    void Start()
    {
        if(enbaleOnStart)
        {
            OpenPassthrough();
        }
        else
        {
            ClosePassthrough();
        }
    }

    public void OpenPassthrough()
    {
        isPassthroughOn = true;
#if !UNITY_EDITOR
        PassthroughFeature.EnableVideoSeeThrough = true;
#endif
    }

    public void ClosePassthrough()
    {
        isPassthroughOn = false;
#if !UNITY_EDITOR
        PassthroughFeature.EnableVideoSeeThrough = false;
#endif
    }

    public void TogglePassthrough()
    {
        if (isPassthroughOn)
        {
            ClosePassthrough();
        }
        else
        {
            OpenPassthrough();
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (!isPassthroughOn) return;

#if !UNITY_EDITOR
        if (pauseStatus)
        {
            PassthroughFeature.PassthroughPause();
        }
        else
        {
            PassthroughFeature.PassthroughStart();
        }
#endif
    }
}
#else
using UnityEngine;

public class PicoPassthroughControl : MonoBehaviour
{
    public bool isPassthroughOn { get; private set; } = false;
    public bool enbaleOnStart;

    public void OpenPassthrough() {}
    public void ClosePassthrough() {}
    public void TogglePassthrough() {}
}
#endif