using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Display formatting unit for network traffic throughput.
    /// </summary>
    public enum TrafficUnitFormat
    {
        /// <summary>Megabits per second formatted as "{value:F1} MBPS"</summary>
        Mbps,
        /// <summary>Bytes per second auto-scaled (e.g. "450.2 KB/s", "12.3 MB/s")</summary>
        AutoBytes,
        /// <summary>Bits per second auto-scaled (e.g. "850.0 Kbps", "12.5 Mbps")</summary>
        AutoBits
    }

    /// <summary>
    /// Unified network traffic throughput measurement engine.
    /// Pure network layer logic: zero UI dependencies (no TMPro / TextMeshProUGUI).
    /// Manages low-overhead byte counter reading across Windows, Linux, and Android XR (Pico/Quest).
    /// </summary>
    public static class NetworkTrafficMonitor
    {
        /// <summary>
        /// Master switch state indicating whether traffic throughput monitoring is enabled.
        /// </summary>
        public static bool IsEnabled { get; private set; } = true;

        /// <summary>
        /// Dispatched when traffic monitoring is toggled on or off.
        /// </summary>
        public static event Action<bool> OnMonitoringToggled;

        /// <summary>
        /// Sets master monitoring state.
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            OnMonitoringToggled?.Invoke(enabled);
        }

        /// <summary>
        /// Turns ON real-time traffic monitoring.
        /// </summary>
        public static void Enable() => SetEnabled(true);

        /// <summary>
        /// Turns OFF real-time traffic monitoring to save CPU and stop polling.
        /// </summary>
        public static void Disable() => SetEnabled(false);

        /// <summary>
        /// Toggles real-time traffic monitoring state.
        /// </summary>
        public static void Toggle() => SetEnabled(!IsEnabled);

        #region Value Formatting

        /// <summary>
        /// Formats given throughput values into display strings according to format rules.
        /// </summary>
        public static string FormatValue(double mbps, double bytesPerSec, TrafficUnitFormat format)
        {
            switch (format)
            {
                case TrafficUnitFormat.AutoBytes:
                {
                    double b = Math.Max(0.0, bytesPerSec);
                    if (b >= 1024.0 * 1024.0)
                        return $"{b / (1024.0 * 1024.0):F1} MB/s";
                    if (b >= 1024.0)
                        return $"{b / 1024.0:F1} KB/s";
                    return $"{b:F0} B/s";
                }

                case TrafficUnitFormat.AutoBits:
                {
                    double bps = Math.Max(0.0, mbps * 1_000_000.0);
                    if (bps >= 1_000_000_000.0)
                        return $"{bps / 1_000_000_000.0:F1} Gbps";
                    if (bps >= 1_000_000.0)
                        return $"{bps / 1_000_000.0:F1} Mbps";
                    if (bps >= 1_000.0)
                        return $"{bps / 1_000.0:F1} Kbps";
                    return $"{bps:F0} bps";
                }

                case TrafficUnitFormat.Mbps:
                default:
                {
                    double m = Math.Max(0.0, mbps);
                    return $"{m:F1} MBPS";
                }
            }
        }

        #endregion

        #region Cross-Platform Hardware Counter Readers

        /// <summary>
        /// Cross-platform resolution of cumulative RX and TX octets for the target interface.
        /// </summary>
        public static bool TryReadBytes(string ifaceName, string ifaceId, out long rxBytes, out long txBytes)
        {
            rxBytes = 0;
            txBytes = 0;

#if UNITY_ANDROID && !UNITY_EDITOR
            // 1. Android sysfs statistics
            if (TryReadSysfs(ifaceName, out rxBytes, out txBytes)) return true;

            // 2. Android /proc/net/dev kernel table
            if (TryReadProcNetDev(ifaceName, out rxBytes, out txBytes)) return true;

            // 3. Fallback to Android TrafficStats total
            if (TryReadAndroidTrafficStats(out rxBytes, out txBytes)) return true;

            // 4. Fallback to standard .NET
            if (TryReadDotNet(ifaceName, ifaceId, out rxBytes, out txBytes)) return true;
#elif UNITY_STANDALONE_LINUX && !UNITY_EDITOR
            if (TryReadSysfs(ifaceName, out rxBytes, out txBytes)) return true;
            if (TryReadProcNetDev(ifaceName, out rxBytes, out txBytes)) return true;
            if (TryReadDotNet(ifaceName, ifaceId, out rxBytes, out txBytes)) return true;
#else
            // Windows / Editor
            if (TryReadDotNet(ifaceName, ifaceId, out rxBytes, out txBytes)) return true;
            if (TryReadSysfs(ifaceName, out rxBytes, out txBytes)) return true;
            if (TryReadProcNetDev(ifaceName, out rxBytes, out txBytes)) return true;
#endif

            return false;
        }

        private static bool TryReadDotNet(string ifaceName, string ifaceId, out long rxBytes, out long txBytes)
        {
            rxBytes = 0;
            txBytes = 0;
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces();
                if (nics == null || nics.Length == 0) return false;

                NetworkInterface matchedNic = null;
                for (int i = 0; i < nics.Length; i++)
                {
                    var nic = nics[i];
                    if (nic == null) continue;

                    if (!string.IsNullOrEmpty(ifaceId) && string.Equals(nic.Id, ifaceId, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedNic = nic;
                        break;
                    }

                    if (!string.IsNullOrEmpty(ifaceName) &&
                        (string.Equals(nic.Name, ifaceName, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(nic.Description, ifaceName, StringComparison.OrdinalIgnoreCase)))
                    {
                        matchedNic = nic;
                        break;
                    }
                }

                if (matchedNic == null)
                {
                    for (int i = 0; i < nics.Length; i++)
                    {
                        var nic = nics[i];
                        if (nic == null) continue;
                        if (nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        {
                            matchedNic = nic;
                            break;
                        }
                    }
                }

                if (matchedNic != null)
                {
                    try
                    {
                        var stats = matchedNic.GetIPStatistics();
                        rxBytes = stats.BytesReceived;
                        txBytes = stats.BytesSent;
                        return true;
                    }
                    catch
                    {
                        var stats4 = matchedNic.GetIPv4Statistics();
                        rxBytes = stats4.BytesReceived;
                        txBytes = stats4.BytesSent;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryReadProcNetDev(string ifaceName, out long rxBytes, out long txBytes)
        {
            rxBytes = 0;
            txBytes = 0;
            const string procPath = "/proc/net/dev";
            if (!File.Exists(procPath)) return false;

            try
            {
                using var reader = new StreamReader(procPath);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int colonIndex = line.IndexOf(':');
                    if (colonIndex <= 0) continue;

                    string name = line.Substring(0, colonIndex).Trim();
                    if (string.Equals(name, "lo", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool matches = string.IsNullOrEmpty(ifaceName) ||
                                   string.Equals(name, ifaceName, StringComparison.OrdinalIgnoreCase);

                    if (matches)
                    {
                        string data = line.Substring(colonIndex + 1).Trim();
                        string[] tokens = data.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length >= 9)
                        {
                            if (long.TryParse(tokens[0], out long rx) && long.TryParse(tokens[8], out long tx))
                            {
                                rxBytes = rx;
                                txBytes = tx;
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryReadSysfs(string ifaceName, out long rxBytes, out long txBytes)
        {
            rxBytes = 0;
            txBytes = 0;
            if (string.IsNullOrEmpty(ifaceName)) return false;

            try
            {
                string rxPath = $"/sys/class/net/{ifaceName}/statistics/rx_bytes";
                string txPath = $"/sys/class/net/{ifaceName}/statistics/tx_bytes";
                if (File.Exists(rxPath) && File.Exists(txPath))
                {
                    if (long.TryParse(File.ReadAllText(rxPath).Trim(), out rxBytes) &&
                        long.TryParse(File.ReadAllText(txPath).Trim(), out txBytes))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool TryReadAndroidTrafficStats(out long rxBytes, out long txBytes)
        {
            rxBytes = 0;
            txBytes = 0;
            try
            {
                using var trafficStats = new UnityEngine.AndroidJavaClass("android.net.TrafficStats");
                long rx = trafficStats.CallStatic<long>("getTotalRxBytes");
                long tx = trafficStats.CallStatic<long>("getTotalTxBytes");
                if (rx >= 0 && tx >= 0)
                {
                    rxBytes = rx;
                    txBytes = tx;
                    return true;
                }
            }
            catch { }
            return false;
        }
#endif

        #endregion

        #region Reusable Delta Sampler

        /// <summary>
        /// Lightweight delta throughput sampler for reusable per-interface traffic tracking.
        /// </summary>
        public class Sampler
        {
            private long _lastRxBytes;
            private long _lastTxBytes;
            private long _lastTicks;
            private bool _hasSample;

            public double RxMbps { get; private set; }
            public double TxMbps { get; private set; }
            public double RxBytesPerSec { get; private set; }
            public double TxBytesPerSec { get; private set; }
            public bool HasSample => _hasSample;

            public void Reset()
            {
                _lastRxBytes = 0;
                _lastTxBytes = 0;
                _lastTicks = 0;
                _hasSample = false;
                RxMbps = 0.0;
                TxMbps = 0.0;
                RxBytesPerSec = 0.0;
                TxBytesPerSec = 0.0;
            }

            public bool UpdateSample(string interfaceName = null, string interfaceId = null)
            {
                if (!TryReadBytes(interfaceName, interfaceId, out long currentRx, out long currentTx))
                {
                    RxMbps = 0.0;
                    TxMbps = 0.0;
                    RxBytesPerSec = 0.0;
                    TxBytesPerSec = 0.0;
                    return false;
                }

                long currentTicks = Stopwatch.GetTimestamp();

                if (!_hasSample || _lastTicks <= 0)
                {
                    _lastRxBytes = currentRx;
                    _lastTxBytes = currentTx;
                    _lastTicks = currentTicks;
                    _hasSample = true;
                    RxMbps = 0.0;
                    TxMbps = 0.0;
                    RxBytesPerSec = 0.0;
                    TxBytesPerSec = 0.0;
                    return true;
                }

                double elapsedSeconds = (double)(currentTicks - _lastTicks) / Stopwatch.Frequency;
                if (elapsedSeconds < 0.05)
                {
                    return false;
                }

                long deltaRx = currentRx - _lastRxBytes;
                long deltaTx = currentTx - _lastTxBytes;

                if (deltaRx >= 0)
                {
                    RxMbps = (deltaRx * 8.0) / (elapsedSeconds * 1_000_000.0);
                    RxBytesPerSec = deltaRx / elapsedSeconds;
                }
                else
                {
                    RxMbps = 0.0;
                    RxBytesPerSec = 0.0;
                }

                if (deltaTx >= 0)
                {
                    TxMbps = (deltaTx * 8.0) / (elapsedSeconds * 1_000_000.0);
                    TxBytesPerSec = deltaTx / elapsedSeconds;
                }
                else
                {
                    TxMbps = 0.0;
                    TxBytesPerSec = 0.0;
                }

                _lastRxBytes = currentRx;
                _lastTxBytes = currentTx;
                _lastTicks = currentTicks;
                return true;
            }

            public string GetFormattedRx(TrafficUnitFormat format = TrafficUnitFormat.Mbps)
            {
                return FormatValue(RxMbps, RxBytesPerSec, format);
            }

            public string GetFormattedTx(TrafficUnitFormat format = TrafficUnitFormat.Mbps)
            {
                return FormatValue(TxMbps, TxBytesPerSec, format);
            }
        }

        #endregion
    }
}
