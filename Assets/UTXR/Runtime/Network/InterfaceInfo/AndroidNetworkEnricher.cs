using System;
using System.IO;
using UnityEngine;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Android XR platform enrichment provider (Pico / Quest / AOSP).
    /// Uses Android ConnectivityManager and WifiManager to associate Wi-Fi SSID,
    /// transport types, estimated downstream bandwidth, and default routing.
    /// Runs strictly on the Cold Path.
    /// </summary>
    internal static class AndroidNetworkEnricher
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool _permissionRequested;
#endif

        public static void Enrich(RawInterfaceData[] rawInterfaces, int count)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (rawInterfaces == null || count <= 0) return;

            if (!_permissionRequested && !System.Threading.Thread.CurrentThread.IsThreadPoolThread)
            {
                _permissionRequested = true;
                AndroidWifiSsidPermission.RequestLocationPermissions();
            }

            bool workerThread = System.Threading.Thread.CurrentThread.IsThreadPoolThread;
            if (workerThread && AndroidJNI.AttachCurrentThread() != 0) return;
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null) return;

                using var context = activity.Call<AndroidJavaObject>("getApplicationContext");
                var targetContext = context ?? activity;

                using var cm = targetContext.Call<AndroidJavaObject>("getSystemService", "connectivity");
                if (cm == null) return;

                // 1. Authoritative default route from active network
                string defaultIfaceName = null;
                using var activeNetwork = cm.Call<AndroidJavaObject>("getActiveNetwork");
                if (activeNetwork != null)
                {
                    using var activeLp = cm.Call<AndroidJavaObject>("getLinkProperties", activeNetwork);
                    if (activeLp != null)
                    {
                        defaultIfaceName = activeLp.Call<string>("getInterfaceName");
                    }
                }
                if (!string.IsNullOrEmpty(defaultIfaceName))
                {
                    NetworkLogger.Log($"[Network] Default route interface: '{defaultIfaceName}'");
                    for (int i = 0; i < count; i++)
                    {
                        if (string.Equals(rawInterfaces[i].Name, defaultIfaceName, StringComparison.OrdinalIgnoreCase))
                        {
                            rawInterfaces[i].IsDefaultRoute = true;
                        }
                    }
                }

                // 2. Iterate all networks to map capabilities and transports
                using var networksArray = cm.Call<AndroidJavaObject>("getAllNetworks");
                if (networksArray != null)
                {
                    var rawNetworks = AndroidJNIHelper.ConvertFromJNIArray<AndroidJavaObject[]>(networksArray.GetRawObject());
                    if (rawNetworks != null)
                    {
                        foreach (var net in rawNetworks)
                        {
                            if (net == null) continue;
                            using (net)
                            {
                                using var lp = cm.Call<AndroidJavaObject>("getLinkProperties", net);
                                using var caps = cm.Call<AndroidJavaObject>("getNetworkCapabilities", net);
                                if (lp == null || caps == null) continue;

                                string ifName = lp.Call<string>("getInterfaceName");
                                if (string.IsNullOrEmpty(ifName)) continue;

                                int matchIdx = -1;
                                for (int i = 0; i < count; i++)
                                {
                                    if (string.Equals(rawInterfaces[i].Name, ifName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        matchIdx = i;
                                        break;
                                    }
                                }

                                // Transports: 0=Cellular, 1=WiFi, 2=Bluetooth, 3=Ethernet, 4=VPN
                                bool isWifi = caps.Call<bool>("hasTransport", 1);
                                bool isEthernet = caps.Call<bool>("hasTransport", 3);
                                bool isCellular = caps.Call<bool>("hasTransport", 0);
                                bool isVpn = caps.Call<bool>("hasTransport", 4);
                                bool isBluetooth = caps.Call<bool>("hasTransport", 2);

                                if (matchIdx < 0) continue;

                                if (isWifi) rawInterfaces[matchIdx].Type = NetworkType.WiFi;
                                else if (isEthernet) rawInterfaces[matchIdx].Type = NetworkType.Ethernet;
                                else if (isCellular) rawInterfaces[matchIdx].Type = NetworkType.Cellular;
                                else if (isVpn) rawInterfaces[matchIdx].Type = NetworkType.VPN;
                                else if (isBluetooth) rawInterfaces[matchIdx].Type = NetworkType.Bluetooth;

                                int downstreamKbps = caps.Call<int>("getLinkDownstreamBandwidthKbps");
                                if (downstreamKbps > 0)
                                {
                                    rawInterfaces[matchIdx].EstimatedBandwidthMbps = downstreamKbps / 1000;
                                }

                                if (isWifi)
                                {
                                    EnrichAndroidWifi(targetContext, ref rawInterfaces[matchIdx]);
                                }
                            }
                        }
                    }
                }

                // 3. Physical link speed for Ethernet or other interfaces via Linux sysfs
                for (int i = 0; i < count; i++)
                {
                    if (rawInterfaces[i].LinkSpeedMbps <= 0)
                    {
                        long sysSpeed = ReadSysfsSpeed(rawInterfaces[i].Name);
                        if (sysSpeed > 0)
                        {
                            rawInterfaces[i].LinkSpeedMbps = sysSpeed;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                NetworkLogger.LogWarning($"[Network] Android enrichment failed: {ex.Message}");
            }
            finally
            {
                if (workerThread) AndroidJNI.DetachCurrentThread();
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void EnrichAndroidWifi(AndroidJavaObject context, ref RawInterfaceData iface)
        {
            try
            {
                using var wm = context.Call<AndroidJavaObject>("getSystemService", "wifi");
                if (wm == null) return;

                using var wifiInfo = wm.Call<AndroidJavaObject>("getConnectionInfo");
                if (wifiInfo == null) return;

                string ssid = wifiInfo.Call<string>("getSSID");
                if (!string.IsNullOrEmpty(ssid))
                {
                    ssid = ssid.Trim('\"');
                    if (ssid != "<unknown ssid>" && ssid != "0x" && ssid != "")
                    {
                        iface.Ssid = ssid;
                        iface.DisplayName = ssid;
                    }
                }

                int linkSpeed = wifiInfo.Call<int>("getLinkSpeed");
                if (linkSpeed > 0)
                {
                    iface.LinkSpeedMbps = linkSpeed;
                }
                NetworkLogger.Log($"[Network] Wi-Fi '{iface.Name}': SSID='{iface.Ssid}', Speed={iface.LinkSpeedMbps}Mbps");
            }
            catch (Exception ex)
            {
                NetworkLogger.LogWarning($"[Network] Wi-Fi enrichment failed: {ex.Message}");
            }
        }

        private static long ReadSysfsSpeed(string ifaceName)
        {
            try
            {
                string speedFile = $"/sys/class/net/{ifaceName}/speed";
                if (File.Exists(speedFile))
                {
                    string text = File.ReadAllText(speedFile).Trim();
                    if (long.TryParse(text, out long speed) && speed > 0)
                    {
                        return speed;
                    }
                }
            }
            catch { }
            return -1;
        }
#endif
    }
}
