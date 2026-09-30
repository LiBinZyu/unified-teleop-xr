#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata; 
using System.IO;
using System.Xml;
using UnityEditor.Android;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


public class OpenXRConfigSwitcher
{
    private const string TAG = "[OpenXRConfigSwitcher] ";

    private const string PICO_LIVE_PREVIEW_LOADER = "Unity.XR.PICO.LivePreview.PXR_PTLoader";
    private const string OPENXR_LOADER = "UnityEngine.XR.OpenXR.OpenXRLoader";

    private static readonly BuildTargetGroup[] TargetGroups = new BuildTargetGroup[] 
    {
        BuildTargetGroup.Android, 
        BuildTargetGroup.Standalone,
        BuildTargetGroup.WSA
    };

    // 已移除顶部菜单栏特性 [MenuItem]，改为由 UI 按钮直接触发
    public static void SwitchToPico()
    {
        SwitchXRFeatures(isPico: true);
    }

    // 已移除顶部菜单栏特性 [MenuItem]，改为由 UI 按钮直接触发
    public static void SwitchToMeta()
    {
        SwitchXRFeatures(isPico: false);
    }

    private static void CloseProjectSettingsWindow()
    {
        var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
        foreach (var window in windows)
        {
            if (window != null && window.GetType().Name == "ProjectSettingsWindow")
            {
                window.Close();
                Debug.Log($"{TAG}Closed Project Settings window to ensure settings apply correctly.");
            }
        }
    }

    private static void SwitchXRFeatures(bool isPico)
    {
        CloseProjectSettingsWindow();

        EditorPrefs.SetBool("TargetXR_IsPico", isPico);

        XRSwitchRules rulesAsset = GetOrCreateRulesAsset();

        foreach (var targetGroup in TargetGroups)
        {
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(targetGroup);
            
            if (settings != null)
            {
                var features = settings.GetFeatures<OpenXRFeature>();
                bool settingsDirty = false;

                foreach (var feature in features)
                {
                    var attribute = feature.GetType().GetCustomAttribute<OpenXRFeatureAttribute>();
                    string id = attribute != null ? attribute.FeatureId : feature.name;
                    string lowerId = id.ToLower();
                    string featureName = feature.name.ToLower();

                    bool targetState = feature.enabled;
                    bool ruleApplied = false;

                    if (rulesAsset != null && rulesAsset.featureRules != null)
                    {
                        foreach (var rule in rulesAsset.featureRules)
                        {
                            if (string.IsNullOrEmpty(rule.featureKeyword)) continue;

                            bool platformMatch = rule.platformTarget == XRSwitchRules.PlatformTarget.Any ||
                                (rule.platformTarget == XRSwitchRules.PlatformTarget.Android && targetGroup == BuildTargetGroup.Android) ||
                                (rule.platformTarget == XRSwitchRules.PlatformTarget.Standalone && targetGroup == BuildTargetGroup.Standalone) ||
                                (rule.platformTarget == XRSwitchRules.PlatformTarget.WSA && targetGroup == BuildTargetGroup.WSA);

                            if (!platformMatch) continue;

                            if (lowerId.Contains(rule.featureKeyword.ToLower()) || featureName.Contains(rule.featureKeyword.ToLower()))
                            {
                                targetState = isPico ? rule.enableOnPico : rule.enableOnMeta;
                                ruleApplied = true;
                                break; 
                            }
                        }
                    }

                    if (!ruleApplied)
                    {
                        if (targetGroup == BuildTargetGroup.WSA)
                        {
                            if (feature is OpenXRInteractionFeature) targetState = false;
                        }
                        else
                        {
                            bool isMetaFeature = lowerId.Contains("meta") || lowerId.Contains("oculus") || lowerId.Contains("arfoundation-meta") || featureName.Contains("meta") || featureName.Contains("oculus");
                            bool isPicoFeature = lowerId.Contains("pico") || featureName.Contains("pico");

                            if (isMetaFeature || isPicoFeature)
                            {
                                targetState = isPico ? isPicoFeature : isMetaFeature;
                            }
                        }
                    }

                    if (feature.enabled != targetState)
                    {
                        feature.enabled = targetState;
                        settingsDirty = true;
                        Debug.Log($"{TAG}[{targetGroup}] Feature {feature.name} set to {targetState}");
                    }
                }

                if (settingsDirty) EditorUtility.SetDirty(settings);
            }

            UpdateFeatureGroups(targetGroup, isPico);
        }

        UpdateXRLoaders(isPico);
        UpdateScriptingDefineSymbols(isPico);

        if (rulesAsset != null && rulesAsset.switchGraphicsAPI)
        {
            UpdateGraphicsAPI(isPico);
        }

        AssetDatabase.SaveAssets();

        if (rulesAsset != null)
        {
            UpdateManifestDependencies(rulesAsset, isPico);
        }

        string targetName = isPico ? "PICO" : "Meta Quest";
        Debug.Log($"{TAG}Successfully switched all platforms to {targetName}.");
    }

    /// <summary>
    /// PICO: OpenGLES3；Meta: Vulkan + OpenGLES3。
    /// </summary>
    private static void UpdateGraphicsAPI(bool isPico)
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);

        if (isPico)
        {
            // PICO OpenGLES3
            var picoAPIs = new UnityEngine.Rendering.GraphicsDeviceType[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3
            };
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, picoAPIs);
            Debug.Log($"{TAG}Graphics API (Android) set to: OpenGLES3 only (PICO)");
        }
        else
        {
            // Meta: Vulkan + OpenGLES3
            var metaAPIs = new UnityEngine.Rendering.GraphicsDeviceType[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3
            };
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, metaAPIs);
            Debug.Log($"{TAG}Graphics API (Android) set to: Vulkan + OpenGLES3 (Meta)");
        }
    }

    private static void UpdateScriptingDefineSymbols(bool isPico)
    {
        string metaMacro = "META_XR_SDK";
        string picoMacro = "PICO_OPENXR";

        foreach (var targetGroup in TargetGroups)
        {
            if (targetGroup == BuildTargetGroup.Unknown) continue;

            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
            List<string> defineList = new List<string>(defines.Split(new char[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));

            bool isDirty = false;

            if (isPico)
            {
                if (defineList.Contains(metaMacro))
                {
                    defineList.Remove(metaMacro);
                    isDirty = true;
                }
                if (!defineList.Contains(picoMacro))
                {
                    defineList.Add(picoMacro);
                    isDirty = true;
                }
            }
            else
            {
                if (!defineList.Contains(metaMacro))
                {
                    defineList.Add(metaMacro);
                    isDirty = true;
                }
                if (defineList.Contains(picoMacro))
                {
                    defineList.Remove(picoMacro);
                    isDirty = true;
                }
            }

            if (isDirty)
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, string.Join(";", defineList.ToArray()));
                Debug.Log($"{TAG}[{targetGroup}] Updated Scripting Define Symbols: {(isPico ? "Added PICO_OPENXR, Removed META_XR_SDK" : "Added META_XR_SDK, Removed PICO_OPENXR")}");
            }
        }
    }

    private static void UpdateXRLoaders(bool isPico)
    {
        foreach (var targetGroup in TargetGroups)
        {
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(targetGroup);
            if (settings == null || settings.Manager == null) continue;

            var manager = settings.Manager;
            bool isDirty = false;

            if (targetGroup == BuildTargetGroup.Standalone)
            {
                if (isPico)
                {
                    if (XRPackageMetadataStore.AssignLoader(manager, PICO_LIVE_PREVIEW_LOADER, targetGroup))
                    {
                        Debug.Log($"{TAG}Assigned PICO Live Preview Loader for Standalone.");
                        isDirty = true;
                    }
                    if (XRPackageMetadataStore.RemoveLoader(manager, OPENXR_LOADER, targetGroup))
                    {
                        Debug.Log($"{TAG}Removed OpenXR Loader from Standalone for PICO compatibility.");
                        isDirty = true;
                    }
                }
                else
                {
                    if (XRPackageMetadataStore.AssignLoader(manager, OPENXR_LOADER, targetGroup))
                    {
                        Debug.Log($"{TAG}Assigned OpenXR Loader for Standalone (Meta).");
                        isDirty = true;
                    }
                    if (XRPackageMetadataStore.RemoveLoader(manager, PICO_LIVE_PREVIEW_LOADER, targetGroup))
                    {
                        Debug.Log($"{TAG}Removed PICO Live Preview Loader from Standalone.");
                        isDirty = true;
                    }
                }
            }
            else if (targetGroup == BuildTargetGroup.Android)
            {
                if (XRPackageMetadataStore.AssignLoader(manager, OPENXR_LOADER, targetGroup))
                {
                    isDirty = true;
                }
            }
            else if (targetGroup == BuildTargetGroup.WSA)
            {
                if (XRPackageMetadataStore.AssignLoader(manager, OPENXR_LOADER, targetGroup))
                {
                    isDirty = true;
                }
            }

            if (isDirty)
            {
                EditorUtility.SetDirty(manager);
                EditorUtility.SetDirty(settings);
            }
        }
    }

    private static void UpdateFeatureGroups(BuildTargetGroup targetGroup, bool isPico)
    {
        var featureSetsManagerType = System.Type.GetType("UnityEditor.XR.OpenXR.Features.OpenXRFeatureSetManager, Unity.XR.OpenXR.Editor");
        if (featureSetsManagerType == null) return;

        var getFeatureSetsMethod = featureSetsManagerType.GetMethod("FeatureSetsForBuildTarget", BindingFlags.Public | BindingFlags.Static);
        var applyFeatureSetsMethod = featureSetsManagerType.GetMethod("SetFeaturesFromEnabledFeatureSets", BindingFlags.Public | BindingFlags.Static);
        
        if (getFeatureSetsMethod != null && applyFeatureSetsMethod != null)
        {
            var featureSets = getFeatureSetsMethod.Invoke(null, new object[] { targetGroup }) as IEnumerable;
            bool isDirty = false;

            if (featureSets != null)
            {
                foreach (var featureSet in featureSets)
                {
                    var featureSetType = featureSet.GetType();
                    var featureSetIdField = featureSetType.GetField("featureSetId");
                    var isEnabledField = featureSetType.GetField("isEnabled");
                    
                    if (featureSetIdField != null && isEnabledField != null)
                    {
                        string id = (string)featureSetIdField.GetValue(featureSet);
                        bool currentState = (bool)isEnabledField.GetValue(featureSet);
                        bool targetState = currentState;

                        if (id == "com.picoxr.openxr.features")
                        {
                            targetState = isPico;
                        }
                        else if (id == "com.meta.openxr.featureset.metaxr" || id == "com.unity.openxr.featureset.meta")
                        {
                            targetState = !isPico;
                        }

                        if (currentState != targetState)
                        {
                            isEnabledField.SetValue(featureSet, targetState);
                            isDirty = true;
                        }
                    }
                }

                if (isDirty)
                {
                    applyFeatureSetsMethod.Invoke(null, new object[] { targetGroup });
                }
            }
        }
    }

    private static XRSwitchRules GetOrCreateRulesAsset()
    {
        string[] guids = AssetDatabase.FindAssets("t:XRSwitchRules");
        XRSwitchRules rulesAsset = null;
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            rulesAsset = AssetDatabase.LoadAssetAtPath<XRSwitchRules>(path);
        }
        else
        {
            rulesAsset = ScriptableObject.CreateInstance<XRSwitchRules>();
            rulesAsset.featureRules = new XRSwitchRules.FeatureRule[]
            {
                new XRSwitchRules.FeatureRule { featureKeyword = "compositionlayer", platformTarget = XRSwitchRules.PlatformTarget.Any, enableOnPico = true, enableOnMeta = true },
                new XRSwitchRules.FeatureRule { featureKeyword = "handcommonposes", platformTarget = XRSwitchRules.PlatformTarget.Any, enableOnPico = false, enableOnMeta = true },
                new XRSwitchRules.FeatureRule { featureKeyword = "handinteraction", platformTarget = XRSwitchRules.PlatformTarget.Any, enableOnPico = false, enableOnMeta = true },
                new XRSwitchRules.FeatureRule { featureKeyword = "picog3controllerprofile", platformTarget = XRSwitchRules.PlatformTarget.Any, enableOnPico = false, enableOnMeta = false },
                new XRSwitchRules.FeatureRule { featureKeyword = "oculusquestfeature", platformTarget = XRSwitchRules.PlatformTarget.Android, enableOnPico = false, enableOnMeta = false },
                new XRSwitchRules.FeatureRule { featureKeyword = "oculustouchcontrollerprofile", platformTarget = XRSwitchRules.PlatformTarget.WSA, enableOnPico = false, enableOnMeta = true }
            };

            rulesAsset.picoDependencies = new XRSwitchRules.DependencyRule[]
            {
                new XRSwitchRules.DependencyRule { packageId = "com.unity.pico.livepreview", packageVersionOrUrl = "file:D:/apex_teleop/misc/PicoLivePreview" },
                new XRSwitchRules.DependencyRule { packageId = "com.unity.xr.openxr.picoxr", packageVersionOrUrl = "file:D:/apex_teleop/misc/UnityOpenXRIntegrationSDK-1.4.0-20250407" }
            };
            rulesAsset.metaDependencies = new XRSwitchRules.DependencyRule[]
            {
                new XRSwitchRules.DependencyRule { packageId = "com.meta.xr.mrutilitykit", packageVersionOrUrl = "85.0.0" },
                new XRSwitchRules.DependencyRule { packageId = "com.meta.xr.sdk.core", packageVersionOrUrl = "85.0.0" }
            };

            if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
            if (!AssetDatabase.IsValidFolder("Assets/XR/Settings")) AssetDatabase.CreateFolder("Assets/XR", "Settings");

            AssetDatabase.CreateAsset(rulesAsset, "Assets/XR/Settings/XRSwitchRules.asset");
            AssetDatabase.SaveAssets();
            Debug.Log($"{TAG}Generated visual rule asset at Assets/XR/Settings/XRSwitchRules.asset");
        }

        return rulesAsset;
    }

    private static void UpdateManifestDependencies(XRSwitchRules rules, bool isPico)
    {
        string manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            Debug.LogError($"{TAG}manifest.json not found at: {manifestPath}");
            return;
        }

        try
        {
            string jsonText = File.ReadAllText(manifestPath);
            JObject manifest = JObject.Parse(jsonText);
            JObject dependencies = manifest["dependencies"] as JObject;
            if (dependencies == null)
            {
                Debug.LogError($"{TAG}Invalid manifest.json: 'dependencies' key not found.");
                return;
            }

            var toRemove = isPico ? rules.metaDependencies : rules.picoDependencies;
            var toAdd = isPico ? rules.picoDependencies : rules.metaDependencies;
            bool isDirty = false;

            if (toRemove != null)
            {
                foreach (var dep in toRemove)
                {
                    if (dep != null && !string.IsNullOrEmpty(dep.packageId))
                    {
                        if (dependencies[dep.packageId] != null)
                        {
                            dependencies.Remove(dep.packageId);
                            isDirty = true;
                            Debug.Log($"{TAG}Removed package: {dep.packageId}");
                        }
                    }
                }
            }

            if (toAdd != null)
            {
                foreach (var dep in toAdd)
                {
                    if (dep != null && !string.IsNullOrEmpty(dep.packageId))
                    {
                        var currentVal = dependencies[dep.packageId];
                        if (currentVal == null || currentVal.ToString() != dep.packageVersionOrUrl)
                        {
                            dependencies[dep.packageId] = dep.packageVersionOrUrl;
                            isDirty = true;
                            Debug.Log($"{TAG}Added/Updated package: {dep.packageId} -> {dep.packageVersionOrUrl}");
                        }
                    }
                }
            }

            if (isDirty)
            {
                // Sort dependencies
                var sortedDependencies = new JObject();
                var sortedKeys = new List<string>();
                foreach (var prop in dependencies.Properties())
                {
                    sortedKeys.Add(prop.Name);
                }
                sortedKeys.Sort();
                foreach (var key in sortedKeys)
                {
                    sortedDependencies[key] = dependencies[key];
                }
                manifest["dependencies"] = sortedDependencies;

                File.WriteAllText(manifestPath, manifest.ToString(Newtonsoft.Json.Formatting.Indented));
                Debug.Log($"{TAG}Successfully updated Packages/manifest.json dependencies for {(isPico ? "PICO" : "Meta")}.");
                
                UnityEditor.PackageManager.Client.Resolve();
            }
            else
            {
                Debug.Log($"{TAG}Dependencies are already up to date. Skipping Resolve.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"{TAG}Failed to update manifest.json: {ex.Message}");
        }
    }
}

// Android 清单自动清理后端 
public class XRPermissionCleaner : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 99; 

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        bool isPicoEnabled = EditorPrefs.GetBool("TargetXR_IsPico", false);
        Debug.Log($"[XRPermissionCleaner] Target platform linked from Switcher: {(isPicoEnabled ? "PICO" : "Meta Quest")}");

        string manifestPath = path + "/src/main/AndroidManifest.xml";
        if (!File.Exists(manifestPath)) return;

        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(manifestPath);
        
        XmlNode manifestNode = xmlDoc.SelectSingleNode("/manifest");
        if (manifestNode == null) return;

        bool modified = false;

        string[] nodeTypes = new string[] { "uses-permission", "uses-feature" };
        foreach (string nodeType in nodeTypes)
        {
            XmlNodeList nodes = manifestNode.SelectNodes(nodeType);
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                XmlNode node = nodes[i];
                XmlAttribute nameAttribute = node.Attributes["android:name"];
                if (nameAttribute != null)
                {
                    string attrValue = nameAttribute.Value.ToLower();
                    bool shouldRemove = false;

                    if (isPicoEnabled)
                    {
                        if (attrValue.Contains("com.oculus.") || attrValue.Contains("horizonos."))
                        {
                            shouldRemove = true;
                        }
                    }
                    else
                    {
                        if (attrValue.Contains("com.pvr."))
                        {
                            shouldRemove = true;
                        }
                    }

                    if (shouldRemove)
                    {
                        manifestNode.RemoveChild(node);
                        modified = true;
                        Debug.Log($"[XRPermissionCleaner] Removed irrelevant {(isPicoEnabled ? "Meta" : "PICO")} tag: <{nodeType} android:name=\"{nameAttribute.Value}\" />");
                    }
                }
            }
        }

        XmlNode appNode = manifestNode.SelectSingleNode("application");
        if (appNode != null)
        {
            XmlNodeList metaNodes = appNode.SelectNodes("meta-data");
            for (int i = metaNodes.Count - 1; i >= 0; i--)
            {
                XmlNode node = metaNodes[i];
                XmlAttribute nameAttribute = node.Attributes["android:name"];
                if (nameAttribute != null)
                {
                    string attrValue = nameAttribute.Value.ToLower();
                    bool shouldRemove = false;

                    if (isPicoEnabled && (attrValue.Contains("com.oculus.") || attrValue.Contains("horizonos.")))
                    {
                        shouldRemove = true;
                    }
                    else if (!isPicoEnabled && attrValue.Contains("com.pvr."))
                    {
                        shouldRemove = true;
                    }

                    if (shouldRemove)
                    {
                        appNode.RemoveChild(node);
                        modified = true;
                        Debug.Log($"[XRPermissionCleaner] Removed irrelevant meta-data: {nameAttribute.Value}");
                    }
                }
            }
            
            XmlNodeList activities = appNode.SelectNodes("activity");
            foreach (XmlNode activityNode in activities)
            {
                XmlNodeList activityMetaNodes = activityNode.SelectNodes("meta-data");
                for (int i = activityMetaNodes.Count - 1; i >= 0; i--)
                {
                    XmlNode node = activityMetaNodes[i];
                    XmlAttribute nameAttribute = node.Attributes["android:name"];
                    if (nameAttribute != null)
                    {
                        string attrValue = nameAttribute.Value.ToLower();
                        bool shouldRemove = false;

                        if (isPicoEnabled && (attrValue.Contains("com.oculus.") || attrValue.Contains("horizonos.")))
                        {
                            shouldRemove = true;
                        }
                        else if (!isPicoEnabled && attrValue.Contains("com.pvr."))
                        {
                            shouldRemove = true;
                        }

                        if (shouldRemove)
                        {
                            activityNode.RemoveChild(node);
                            modified = true;
                            Debug.Log($"[XRPermissionCleaner] Removed irrelevant activity meta-data: {nameAttribute.Value}");
                        }
                    }
                }
            }
        }

        if (modified)
        {
            xmlDoc.Save(manifestPath);
            Debug.Log($"[XRPermissionCleaner] Successfully cleaned AndroidManifest.xml for {(isPicoEnabled ? "PICO" : "Meta Quest")} build.");
        }
    }
}

// =========================================================================
// 定义资产 Inspector 界面渲染与切换展示
// =========================================================================
[CustomEditor(typeof(XRSwitchRules))]
public class XRSwitchRulesEditor : Editor
{
    public override void OnInspectorGUI()
    {
        bool isPico = EditorPrefs.GetBool("TargetXR_IsPico", false);
        
        EditorGUILayout.Space(5);
        string currentPlatform = isPico ? "PICO (Live Preview / Standalone / Android)" : "Meta Quest (Quest Link / Standalone / Android)";
        
        EditorGUILayout.LabelField("Current Platform: " + currentPlatform, EditorStyles.boldLabel);

        EditorGUILayout.LabelField("Switch to:");
        
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("PICO", GUILayout.Height(35)))
            {
                OpenXRConfigSwitcher.SwitchToPico();
            }
            
            if (GUILayout.Button("Meta Quest", GUILayout.Height(35)))
            {
                OpenXRConfigSwitcher.SwitchToMeta();
            }
        }
        EditorGUILayout.Space(10);

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Asset", target, typeof(XRSwitchRules), false);
        EditorGUI.EndDisabledGroup(); 

        DrawDefaultInspector();

    }
}

// Project Settings 页面注册类
public static class XRProjectSettingsRegister
{
    private static Editor cachedRulesEditor;

    [SettingsProvider]
    public static SettingsProvider CreateXRSwitchRulesSettingsProvider()
    {
        var provider = new SettingsProvider("Project/XR Plug-in Management/KernalMind", SettingsScope.Project)
        {
            label = "KernalMind",
            guiHandler = (searchContext) =>
            {
                XRSwitchRules rulesAsset = FindRulesAsset();
                
                if (rulesAsset == null)
                {
                    EditorGUILayout.Space(10);
                    EditorGUILayout.HelpBox("No XRSwitchRules asset found\nClick the button below to initialize the default asset.", MessageType.Warning);
                    if (GUILayout.Button("Initialize XR Switch Config", GUILayout.Height(30)))
                    {
                        OpenXRConfigSwitcher.SwitchToMeta();
                    }
                    return;
                }

                // 实例化对应的自定义 Editor
                if (cachedRulesEditor == null || cachedRulesEditor.target != rulesAsset)
                {
                    cachedRulesEditor = Editor.CreateEditor(rulesAsset);
                }

                if (cachedRulesEditor != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(5);
                    GUILayout.BeginVertical();
                    
                    cachedRulesEditor.OnInspectorGUI();
                    
                    GUILayout.EndVertical();
                    GUILayout.Space(5);
                    GUILayout.EndHorizontal();
                }
            },

            keywords = new HashSet<string>(new[] { "XR", "Pico", "Meta", "Quest", "Switch", "Rules", "KernalMind" })
        };

        return provider;
    }

    private static XRSwitchRules FindRulesAsset()
    {
        string[] guids = AssetDatabase.FindAssets("t:XRSwitchRules");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<XRSwitchRules>(path);
        }
        return null;
    }
}
#endif