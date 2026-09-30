#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace CycloneDDS.Editor
{
    [CustomEditor(typeof(DdsPublisher))]
    public class DdsPublisherEditor : UnityEditor.Editor
    {
        private static bool _isScanning = false;
        private static List<string> _lastDiscoveredIps = new List<string>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var pub = (DdsPublisher)target;

            EditorGUILayout.Space(12);

            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Real-Time Runtime Diagnostics", EditorStyles.boldLabel);

                if (pub.isInitialized)
                {
                    if (pub.publishOnlyWhenSubscribersMatched && !pub.hasMatchedSubscribers)
                    {
                        EditorGUILayout.HelpBox(
                            $"Status: STANDBY (0 Subscribers Matched - Paused)\\n" +
                            $"Target: {(string.IsNullOrWhiteSpace(pub.targetIp) ? "Subnet Auto-Discovery (Broadcast)" : pub.targetIp)}\\n" +
                            $"Active Channels: {(pub.channels != null ? pub.channels.Count : 0)} ready\\n" +
                            $"Note: High-rate streaming will auto-start as soon as a ROS 2 subscriber connects to any topic.",
                            MessageType.Warning
                        );
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            $"Status: PUBLISHING ({pub.totalMatchedSubscribers} Subscriber(s) Connected)\\n" +
                            $"Measured Rate: {pub.measuredPublishRate:F1} Hz (Cap: {pub.publishRateHz} Hz)\\n" +
                            $"Total Published: {pub.totalPublishedCount} samples\\n" +
                            $"Target: {(string.IsNullOrWhiteSpace(pub.targetIp) ? "Subnet Auto-Discovery (Broadcast)" : pub.targetIp)}\\n" +
                            $"Channels: {(pub.channels != null ? pub.channels.Count : 0)} active",
                            MessageType.Info
                        );
                    }

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Restart DDS"))
                    {
                        pub.Shutdown();
                        pub.InitializeDds();
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button("Shutdown DDS"))
                    {
                        pub.Shutdown();
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.HelpBox("Status: DDS UNINITIALIZED", MessageType.Warning);
                    if (GUILayout.Button("Initialize DDS"))
                    {
                        pub.InitializeDds();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Subnet Discovery & Network Tools", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(_isScanning);
            if (GUILayout.Button(_isScanning ? "Scanning Subnet (Probing IPs)..." : "Probe Subnet Devices (Find DDS / ROS 2 Peers)"))
            {
                ExecuteSubnetScan(pub);
            }
            EditorGUI.EndDisabledGroup();

            if (_lastDiscoveredIps.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"Discovered {_lastDiscoveredIps.Count} reachable device(s) on local subnet:\\n" +
                    string.Join("\\n", _lastDiscoveredIps),
                    MessageType.None
                );
            }
        }

        private static async void ExecuteSubnetScan(DdsPublisher pub)
        {
            _isScanning = true;
            try
            {
                DdsLog.Info("Scanning local subnet ARP cache for active devices...");
                var discovered = await Task.Run(() => DdsNetworkHelper.GetActiveSubnetIps());
                _lastDiscoveredIps = discovered;
                if (pub != null)
                {
                    pub.ForceDiscoveredIps(discovered);
                }

                if (discovered.Count > 0)
                {
                    DdsLog.Info($"Subnet scan found {discovered.Count} device(s): {string.Join(", ", discovered)}");
                }
                else
                {
                    DdsLog.Warning("Subnet scan completed: No active devices responded to ping.");
                }
            }
            catch (System.Exception ex)
            {
                DdsLog.Error($"Subnet scan error: {ex.Message}");
            }
            finally
            {
                _isScanning = false;
            }
        }
    }
}
#endif
