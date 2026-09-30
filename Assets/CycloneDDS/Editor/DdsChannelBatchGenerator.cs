#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CycloneDDS.Editor
{
    /// <summary>
    /// Editor utility to automatically generate or update all DdsInputActionChannel ScriptableObject assets
    /// directly from Assets/Scripts/XRI Input Actions.inputactions.
    /// Classified strictly by individual InputAction streams (single binding per channel),
    /// defaulting frameId to "map", and assigning appropriate ROS topics, QoS, and loop modes.
    /// </summary>
    [InitializeOnLoad]
    public static class DdsChannelBatchGenerator
    {
        public const string InputActionsPath = "Assets/Scripts/XRI Input Actions.inputactions";
        public const string BaseChannelsDir = "Assets/CycloneDDS/Samples/Channels";

        static DdsChannelBatchGenerator()
        {
            EditorApplication.delayCall += EnsureChannelsExistOnStartup;
        }

        private static void EnsureChannelsExistOnStartup()
        {
            if (!Directory.Exists(BaseChannelsDir) || Directory.GetFiles(BaseChannelsDir, "*.asset", SearchOption.AllDirectories).Length == 0)
            {
                Debug.Log("[CycloneDDS] Channels directory empty or missing. Auto-generating all channels from XRI Input Actions...");
                GenerateAllChannels();
            }
        }

        [MenuItem("CycloneDDS/Generate Channels from XRI Input Actions", false, 10)]
        public static void GenerateAllChannels()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (asset == null)
            {
                Debug.LogError($"[CycloneDDS] Cannot find InputActionAsset at '{InputActionsPath}'!");
                return;
            }

            var allObjects = AssetDatabase.LoadAllAssetsAtPath(InputActionsPath);
            var actionReferences = allObjects.OfType<InputActionReference>().ToList();

            if (actionReferences.Count == 0)
            {
                Debug.LogError($"[CycloneDDS] No InputActionReference sub-assets found inside '{InputActionsPath}'!");
                return;
            }

            if (!Directory.Exists(BaseChannelsDir))
            {
                Directory.CreateDirectory(BaseChannelsDir);
            }

            int createdCount = 0;
            int updatedCount = 0;

            foreach (var actionRef in actionReferences)
            {
                var action = actionRef.action;
                if (action == null || action.actionMap == null) continue;

                string mapName = action.actionMap.name;
                string actionName = action.name;

                // Subdirectory by action map (e.g. Channels/Head, Channels/Left, Channels/Right)
                string safeMapDir = SanitizeFolderName(mapName);
                string targetDir = Path.Combine(BaseChannelsDir, safeMapDir).Replace('\\', '/');
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string safeAssetName = SanitizeFileName($"{mapName}_{actionName}.asset");
                string assetPath = $"{targetDir}/{safeAssetName}";

                DdsInputActionChannel channel = AssetDatabase.LoadAssetAtPath<DdsInputActionChannel>(assetPath);
                bool isNew = (channel == null);

                if (isNew)
                {
                    channel = ScriptableObject.CreateInstance<DdsInputActionChannel>();
                    createdCount++;
                }
                else
                {
                    updatedCount++;
                }

                // Configure Channel properties
                channel.isEnabled = true;
                channel.channelType = InferChannelType(action);
                channel.topicName = GenerateTopicName(mapName, actionName);
                channel.frameId = "map";
                channel.convertToRosCoordinates = (channel.channelType == DdsChannelType.Pose ||
                                                   channel.channelType == DdsChannelType.Point ||
                                                   channel.channelType == DdsChannelType.Rotation ||
                                                   channel.channelType == DdsChannelType.Vector3 ||
                                                   channel.channelType == DdsChannelType.Twist);

                // QoS defaults
                ConfigureQos(channel, mapName, actionName);

                // Assign single InputAction binding
                channel.inputAction = new InputActionProperty(actionRef);

                if (isNew)
                {
                    AssetDatabase.CreateAsset(channel, assetPath);
                }
                else
                {
                    EditorUtility.SetDirty(channel);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[CycloneDDS] Batch Generation Complete: {createdCount} channels created, {updatedCount} channels updated in '{BaseChannelsDir}'.");

            AutoAssignToScenePublisher();
        }

        public static void AutoAssignToScenePublisher()
        {
            var publisher = UnityEngine.Object.FindFirstObjectByType<DdsPublisher>();
            if (publisher == null) return;

            var allChannels = AssetDatabase.FindAssets("t:DdsInputActionChannel", new[] { BaseChannelsDir })
                .Select(guid => AssetDatabase.LoadAssetAtPath<DdsInputActionChannel>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(c => c != null)
                .OrderBy(c => c.topicName)
                .ToList();

            Undo.RecordObject(publisher, "Auto-assign DDS Channels");
            publisher.channels = allChannels;
            EditorUtility.SetDirty(publisher);
            Debug.Log($"[CycloneDDS] Automatically assigned {allChannels.Count} channels to DdsPublisher in current scene.");
        }

        private static DdsChannelType InferChannelType(InputAction action)
        {
            string name = action.name.ToLowerInvariant();
            string expectedType = (action.expectedControlType ?? "").ToLowerInvariant();

            if (name.Contains("rotation") || expectedType.Contains("quaternion"))
            {
                return DdsChannelType.Rotation;
            }
            if (name.Contains("position") || name.Contains("point"))
            {
                return DdsChannelType.Point;
            }
            if (name.Contains("velocity") || name.Contains("acceleration"))
            {
                return DdsChannelType.Vector3;
            }
            if (expectedType.Contains("vector2") || name.Contains("thumbstick") || name.Contains("move") || name.Contains("turn") || name.Contains("scroll"))
            {
                return DdsChannelType.Twist;
            }
            if (expectedType.Contains("axis") || name.Contains("value"))
            {
                return DdsChannelType.Float;
            }
            if (expectedType.Contains("button") || action.type == InputActionType.Button || name.StartsWith("button") ||
                name.Contains("select") || name.Contains("activate") || name.Contains("press") || name.Contains("tracked") || name.Contains("cancel"))
            {
                return DdsChannelType.Bool;
            }
            if (expectedType.Contains("integer") || name.Contains("state") || name.Contains("flags") || name.Contains("count"))
            {
                return DdsChannelType.Int32;
            }

            return DdsChannelType.Point;
        }

        private static string GenerateTopicName(string mapName, string actionName)
        {
            string prefix = "head";
            string lowerMap = mapName.ToLowerInvariant();

            if (lowerMap.Contains("left")) prefix = "left";
            else if (lowerMap.Contains("right")) prefix = "right";
            else if (lowerMap.Contains("ui")) prefix = "ui";
            else if (lowerMap.Contains("gesture")) prefix = "gestures";

            if (lowerMap.Contains("locomotion")) prefix += "/locomotion";

            string cleanAction = ToSnakeCase(actionName);
            return $"rt/xr/{prefix}/{cleanAction}";
        }

        private static void ConfigureQos(DdsInputActionChannel channel, string mapName, string actionName)
        {
            switch (channel.channelType)
            {
                case DdsChannelType.Point:
                case DdsChannelType.Rotation:
                case DdsChannelType.Pose:
                case DdsChannelType.Vector3:
                case DdsChannelType.Twist:
                case DdsChannelType.Float:
                    channel.qos = DdsQosPreset.SensorData;
                    break;

                case DdsChannelType.Bool:
                case DdsChannelType.Int32:
                    channel.qos = DdsQosPreset.StateTransient;
                    break;
            }
        }

        private static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string clean = Regex.Replace(input, @"[^a-zA-Z0-9]+", "_");
            clean = Regex.Replace(clean, @"([a-z0-9])([A-Z])", "$1_$2");
            return clean.Trim('_').ToLowerInvariant();
        }

        private static string SanitizeFolderName(string name)
        {
            return Regex.Replace(name, @"[^a-zA-Z0-9_]+", "");
        }

        private static string SanitizeFileName(string name)
        {
            return Regex.Replace(name, @"[^a-zA-Z0-9_\.]+", "_");
        }
    }
}
#endif
