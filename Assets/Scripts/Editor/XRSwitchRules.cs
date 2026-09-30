using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "XRSwitchRules", menuName = "XR Config/XR Switch Rules")]
public class XRSwitchRules : ScriptableObject
{
    public enum PlatformTarget { Any, Android, Standalone, WSA }

    [System.Serializable]
    public class FeatureRule
    {
        [Tooltip("OpenXR Feature")]
        public string featureKeyword;
        
        public PlatformTarget platformTarget = PlatformTarget.Any;
        
        public bool enableOnPico = false;
        
        public bool enableOnMeta = false;
    }

    [System.Serializable]
    public class DependencyRule
    {
        public string packageId;
        public string packageVersionOrUrl;
    }

    [Header("KernalMind OpenXR Build Platform Switch Rules")]
    public FeatureRule[] featureRules;

    [Header("Graphics API Settings")]
    [Tooltip("Pico: OpenGLES3; Meta: Vulkan + OpenGLES3")]
    public bool switchGraphicsAPI = false;

    [Header("Package Dependencies")]
    public DependencyRule[] picoDependencies;
    public DependencyRule[] metaDependencies;
}