using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Windows platform enrichment provider.
    /// Queries Wi-Fi connection attributes (SSID, PHY link rate) via wlanapi.dll
    /// and resolves authoritative default gateway egress via iphlpapi.dll GetBestInterface.
    /// Runs strictly on the Cold Path.
    /// </summary>
    internal static class WindowsNetworkEnricher
    {
        public static void Enrich(RawInterfaceData[] rawInterfaces, int count, NetworkInterface[] nics)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (rawInterfaces == null || count <= 0) return;
            EnrichWlan(rawInterfaces, count);
            EnrichDefaultRoute(rawInterfaces, count, nics);
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        #region Native Windows P/Invoke Definitions

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern int WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern int WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern void WlanFreeMemory(IntPtr pMemory);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern int WlanQueryInterface(
            IntPtr hClientHandle,
            [In] ref Guid pInterfaceGuid,
            int OpCode,
            IntPtr pReserved,
            out uint pdwDataSize,
            out IntPtr ppData,
            IntPtr pWlanOpcodeValueType);

        private enum WLAN_INTERFACE_STATE
        {
            wlan_interface_state_not_ready = 0,
            wlan_interface_state_connected = 1,
            wlan_interface_state_ad_hoc_network_formed = 2,
            wlan_interface_state_disconnecting = 3,
            wlan_interface_state_disconnected = 4,
            wlan_interface_state_associating = 5,
            wlan_interface_state_discovering = 6,
            wlan_interface_state_authenticating = 7
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_INTERFACE_INFO
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strInterfaceDescription;
            public WLAN_INTERFACE_STATE isState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DOT11_SSID
        {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_ASSOCIATION_ATTRIBUTES
        {
            public DOT11_SSID dot11Ssid;
            public int dot11BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] dot11Bssid;
            public int dot11PhyType;
            public uint uDot11PhyIndex;
            public uint wlanSignalQuality;
            public uint ulRxRate;
            public uint ulTxRate;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_CONNECTION_ATTRIBUTES
        {
            public WLAN_INTERFACE_STATE isState;
            public int wlanConnectionMode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProfileName;
            public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
        }

        #endregion

        private static void EnrichWlan(RawInterfaceData[] rawInterfaces, int count)
        {
            IntPtr clientHandle = IntPtr.Zero;
            try
            {
                int res = WlanOpenHandle(2, IntPtr.Zero, out _, out clientHandle);
                if (res != 0 || clientHandle == IntPtr.Zero) return;

                res = WlanEnumInterfaces(clientHandle, IntPtr.Zero, out IntPtr ifListPtr);
                if (res != 0 || ifListPtr == IntPtr.Zero) return;

                try
                {
                    uint numItems = (uint)Marshal.ReadInt32(ifListPtr);
                    int infoSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
                    IntPtr currentPtr = IntPtr.Add(ifListPtr, 8); // Skip dwNumberOfItems + dwIndex

                    for (int i = 0; i < numItems; i++)
                    {
                        var ifInfo = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(currentPtr);
                        currentPtr = IntPtr.Add(currentPtr, infoSize);

                        if (ifInfo.isState != WLAN_INTERFACE_STATE.wlan_interface_state_connected) continue;

                        Guid guid = ifInfo.InterfaceGuid;
                        int queryRes = WlanQueryInterface(clientHandle, ref guid, 7 /* current_connection */, IntPtr.Zero, out _, out IntPtr dataPtr, IntPtr.Zero);
                        if (queryRes != 0 || dataPtr == IntPtr.Zero) continue;

                        try
                        {
                            var conn = Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(dataPtr);
                            string ssid = null;
                            if (conn.wlanAssociationAttributes.dot11Ssid.uSSIDLength > 0 &&
                                conn.wlanAssociationAttributes.dot11Ssid.uSSIDLength <= 32)
                            {
                                ssid = Encoding.UTF8.GetString(
                                    conn.wlanAssociationAttributes.dot11Ssid.ucSSID,
                                    0,
                                    (int)conn.wlanAssociationAttributes.dot11Ssid.uSSIDLength);
                            }

                            uint rxRateMbps = conn.wlanAssociationAttributes.ulRxRate / 1000;
                            string guidStr = guid.ToString("B");

                            for (int r = 0; r < count; r++)
                            {
                                if (string.Equals(rawInterfaces[r].Id, guidStr, StringComparison.OrdinalIgnoreCase))
                                {
                                    rawInterfaces[r].Type = NetworkType.WiFi;
                                    if (!string.IsNullOrEmpty(ssid))
                                    {
                                        rawInterfaces[r].Ssid = ssid;
                                        rawInterfaces[r].DisplayName = ssid;
                                    }
                                    if (rxRateMbps > 0 && rawInterfaces[r].LinkSpeedMbps <= 0)
                                    {
                                        rawInterfaces[r].LinkSpeedMbps = rxRateMbps;
                                    }
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            WlanFreeMemory(dataPtr);
                        }
                    }
                }
                finally
                {
                    WlanFreeMemory(ifListPtr);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[WindowsNetworkEnricher] WLAN query error: {ex.Message}");
            }
            finally
            {
                if (clientHandle != IntPtr.Zero)
                {
                    WlanCloseHandle(clientHandle, IntPtr.Zero);
                }
            }
        }

        private static void EnrichDefaultRoute(RawInterfaceData[] rawInterfaces, int count, NetworkInterface[] nics)
        {
            if (nics == null) return;

            int routeRes = GetBestInterface(0, out uint bestIfIdx);
            if (routeRes != 0) return;

            for (int r = 0; r < count; r++)
            {
                if (r < nics.Length && nics[r] != null)
                {
                    try
                    {
                        var ipv4Props = nics[r].GetIPProperties()?.GetIPv4Properties();
                        if (ipv4Props != null && (uint)ipv4Props.Index == bestIfIdx)
                        {
                            rawInterfaces[r].IsDefaultRoute = true;
                            break;
                        }
                    }
                    catch { }
                }
            }
        }
#endif
    }
}
