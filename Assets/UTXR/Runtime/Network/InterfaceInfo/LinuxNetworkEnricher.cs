using System;
using System.IO;
using UnityEngine;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Linux platform enrichment provider (Ubuntu / Jetson Orin / Embedded Linux).
    /// Queries kernel routing via /proc/net/route and interface attributes via /sys/class/net.
    /// Runs strictly on the Cold Path.
    /// </summary>
    internal static class LinuxNetworkEnricher
    {
        public static void Enrich(RawInterfaceData[] rawInterfaces, int count)
        {
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
            if (rawInterfaces == null || count <= 0) return;

            try
            {
                EnrichDefaultRoute(rawInterfaces, count);
                EnrichSysfs(rawInterfaces, count);
            }
            catch (Exception ex)
            {
                NetworkLogger.LogWarning($"[Network] Linux enrichment failed: {ex.Message}");
            }
#endif
        }

#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
        private static void EnrichDefaultRoute(RawInterfaceData[] rawInterfaces, int count)
        {
            const string routePath = "/proc/net/route";
            if (!File.Exists(routePath)) return;

            string[] lines = File.ReadAllLines(routePath);
            string defaultIfaceName = null;
            int lowestMetric = int.MaxValue;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 7)
                {
                    string iface = parts[0];
                    string dest = parts[1];
                    string metricStr = parts[6];

                    // Destination 00000000 represents kernel default gateway
                    if (dest == "00000000")
                    {
                        int metric = int.TryParse(metricStr, out int m) ? m : 0;
                        if (metric < lowestMetric)
                        {
                            lowestMetric = metric;
                            defaultIfaceName = iface;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(defaultIfaceName))
            {
                for (int i = 0; i < count; i++)
                {
                    if (string.Equals(rawInterfaces[i].Name, defaultIfaceName, StringComparison.OrdinalIgnoreCase))
                    {
                        rawInterfaces[i].IsDefaultRoute = true;
                    }
                }
            }
        }

        private static void EnrichSysfs(RawInterfaceData[] rawInterfaces, int count)
        {
            for (int i = 0; i < count; i++)
            {
                string sysNetDir = $"/sys/class/net/{rawInterfaces[i].Name}";
                if (!Directory.Exists(sysNetDir)) continue;

                // PHY link speed
                if (rawInterfaces[i].LinkSpeedMbps <= 0)
                {
                    string speedFile = $"{sysNetDir}/speed";
                    if (File.Exists(speedFile))
                    {
                        try
                        {
                            string text = File.ReadAllText(speedFile).Trim();
                            if (long.TryParse(text, out long sp) && sp > 0)
                            {
                                rawInterfaces[i].LinkSpeedMbps = sp;
                            }
                        }
                        catch { }
                    }
                }

                // Wireless classification
                if (Directory.Exists($"{sysNetDir}/wireless") && rawInterfaces[i].Type != NetworkType.WiFi)
                {
                    rawInterfaces[i].Type = NetworkType.WiFi;
                }
            }
        }
#endif
    }
}
