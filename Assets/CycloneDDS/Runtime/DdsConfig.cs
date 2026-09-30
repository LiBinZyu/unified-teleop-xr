using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using UnityEngine;

// Type Aliases for Convenience
using DdsPoint = CycloneDDS.GeometryPoint;
using DdsQuaternion = CycloneDDS.GeometryQuaternion;
using DdsPose = CycloneDDS.GeometryPose;
using DdsTransform = CycloneDDS.GeometryTransform;

namespace CycloneDDS
{
    /// <summary>
    /// Utility class providing application-level DDS helpers:
    /// - Log level switching and debug toggling
    /// - CycloneDDS XML configuration generation (interface binding, multicast, autodetermine)
    /// - ROS 2 / DDS timestamp generation and Header creation
    /// - Coordinate conversions between Unity left-handed and robotics ENU right-handed
    /// </summary>
    public static class DdsConfig
    {
        #region Logging Controls

        /// <summary>
        /// Current minimum log level.
        /// </summary>
        public static DdsLogLevel LogLevel
        {
            get => DdsLog.LogLevel;
            set => DdsLog.LogLevel = value;
        }

        /// <summary>
        /// Global switch to enable or disable verbose debug logging.
        /// </summary>
        public static bool IsDebugLogEnabled
        {
            get => DdsLog.EnableDebugLog;
            set => DdsLog.EnableDebugLog = value;
        }

        /// <summary>
        /// Set the minimum logging level.
        /// </summary>
        public static void SetLogLevel(DdsLogLevel level)
        {
            DdsLog.LogLevel = level;
        }

        /// <summary>
        /// Enable or disable verbose debug logging.
        /// </summary>
        public static void SetDebugLog(bool enable)
        {
            DdsLog.EnableDebugLog = enable;
        }

        /// <summary>
        /// Enable verbose debug logging.
        /// </summary>
        public static void EnableDebugLog()
        {
            DdsLog.EnableDebugLog = true;
        }

        /// <summary>
        /// Disable verbose debug logging.
        /// </summary>
        public static void DisableDebugLog()
        {
            DdsLog.EnableDebugLog = false;
        }

        /// <summary>
        /// Toggle verbose debug logging state.
        /// </summary>
        public static bool ToggleDebugLog()
        {
            DdsLog.EnableDebugLog = !DdsLog.EnableDebugLog;
            return DdsLog.EnableDebugLog;
        }

        #endregion

        #region Network & CycloneDDS XML Configuration

        /// <summary>
        /// Build a CycloneDDS XML configuration string targeting a peer IP.
        /// Automatically resolves the matching local physical network interface.
        /// </summary>
        /// <param name="targetIp">Target peer IP or hostname (e.g. "192.168.8.181").</param>
        /// <param name="bindMatchingInterface">Whether to lock/bind to the local matching physical network card (default true).</param>
        /// <param name="allowMulticast">Whether to enable multicast discovery (default true).</param>
        /// <param name="autodetermine">CycloneDDS autodetermine flag for interface (default false when locked).</param>
        public static string BuildCycloneDdsXml(
            string targetIp,
            bool bindMatchingInterface = true,
            bool allowMulticast = true,
            bool autodetermine = false)
        {
            // Auto Subnet Broadcast Discovery: when targetIp is empty or "auto"
            if (string.IsNullOrWhiteSpace(targetIp) || targetIp.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                string broadcastIp = DdsNetworkHelper.GetDefaultSubnetBroadcast();
                string localIp = null;
                if (DdsNetworkHelper.TryGetPrimaryInterface(out var primaryNic))
                {
                    localIp = primaryNic.IpAddress;
                }

                var peers = new List<string>();
                if (!string.IsNullOrEmpty(broadcastIp))
                {
                    peers.Add(broadcastIp);
                }

                DdsLog.Info($"[Auto-Discovery] No Target IP specified. Using Subnet Broadcast Discovery (Local IP: {localIp ?? "auto"}, Broadcast Peer: {broadcastIp})");
                return BuildCycloneDdsXml(localIp, peers.ToArray(), bindMatchingInterface: !string.IsNullOrEmpty(localIp), allowMulticast: true, autodetermine: string.IsNullOrEmpty(localIp));
            }

            // Explicit Peer IP Mode
            string matchedLocalIp = null;
            if (bindMatchingInterface)
            {
                matchedLocalIp = DdsNetworkHelper.GetMatchingLocalIp(targetIp);
                if (string.IsNullOrEmpty(matchedLocalIp))
                {
                    DdsLog.Warning($"No matching local interface found for target {targetIp}. Falling back to default routing.");
                }
            }

            return BuildCycloneDdsXml(matchedLocalIp, new[] { targetIp }, bindMatchingInterface, allowMulticast, autodetermine);
        }

        /// <summary>
        /// Build CycloneDDS XML configuration string with explicit interface and peer list.
        /// </summary>
        /// <param name="localInterfaceIp">Local network interface IP to bind, or null.</param>
        /// <param name="peerIps">List of peer IP addresses.</param>
        /// <param name="bindMatchingInterface">Whether to include the NetworkInterface binding tag.</param>
        /// <param name="allowMulticast">Whether to allow multicast discovery (default true).</param>
        /// <param name="autodetermine">Whether autodetermine is true or false in the NetworkInterface tag.</param>
        public static string BuildCycloneDdsXml(
            string localInterfaceIp,
            string[] peerIps,
            bool bindMatchingInterface = true,
            bool allowMulticast = true,
            bool autodetermine = false)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" ?>");
            sb.Append("<CycloneDDS xmlns=\"https://cdds.io/config\">");
            sb.Append("<Domain id=\"any\">");
            sb.Append("<General>");

            // Network Interface Binding
            if (bindMatchingInterface && !string.IsNullOrEmpty(localInterfaceIp))
            {
                sb.Append($"<Interfaces><NetworkInterface address=\"{localInterfaceIp}\" autodetermine=\"{(autodetermine ? "true" : "false")}\"/></Interfaces>");
            }
            else if (autodetermine)
            {
                sb.Append("<Interfaces><NetworkInterface autodetermine=\"true\"/></Interfaces>");
            }

            // Multicast Setting (default true)
            sb.Append($"<AllowMulticast>{(allowMulticast ? "default" : "false")}</AllowMulticast>");
            sb.Append("</General>");

            // Discovery Peers
            if (peerIps != null && peerIps.Length > 0)
            {
                sb.Append("<Discovery><Peers>");
                foreach (var peer in peerIps)
                {
                    if (!string.IsNullOrWhiteSpace(peer))
                    {
                        sb.Append($"<Peer address=\"{peer.Trim()}\"/>");
                    }
                }
                sb.Append("</Peers></Discovery>");
            }

            sb.Append("</Domain></CycloneDDS>");
            return sb.ToString();
        }

        /// <summary>
        /// Search local network interfaces for an IP matching the subnet of the target IP.
        /// Delegates to DdsNetworkHelper.
        /// </summary>
        public static string GetMatchingLocalIp(string targetIpStr) => DdsNetworkHelper.GetMatchingLocalIp(targetIpStr);

        /// <summary>
        /// Retrieve all available local IPv4 addresses.
        /// Delegates to DdsNetworkHelper.
        /// </summary>
        public static string[] GetLocalIPv4Addresses() => DdsNetworkHelper.GetLocalIPv4Addresses();

        #endregion

        #region Timestamp Helpers

        /// <summary>
        /// Get current UTC Unix timestamp in nanoseconds.
        /// </summary>
        public static long GetCurrentTimestampNs()
        {
            return (DateTime.UtcNow.Ticks - 621355968000000000L) * 100L;
        }

        /// <summary>
        /// Get current UTC Unix timestamp split into seconds and nanoseconds.
        /// </summary>
        public static void GetCurrentTime(out int sec, out uint nanosec)
        {
            long ns = GetCurrentTimestampNs();
            sec = (int)(ns / 1000000000L);
            nanosec = (uint)(ns % 1000000000L);
        }

        /// <summary>
        /// Create a DdsTime instance populated with the current UTC time.
        /// </summary>
        public static DdsTime Now()
        {
            GetCurrentTime(out int s, out uint ns);
            return new DdsTime { sec = s, nanosec = ns };
        }

        /// <summary>
        /// Create a DdsHeader struct populated with current timestamp and frame ID.
        /// </summary>
        public static DdsHeader CreateHeader(string frameId, long timestampNs = 0)
        {
            if (timestampNs <= 0) timestampNs = GetCurrentTimestampNs();
            return new DdsHeader
            {
                stamp = new DdsTime
                {
                    sec = (int)(timestampNs / 1000000000L),
                    nanosec = (uint)(timestampNs % 1000000000L)
                },
                frame_id = frameId ?? string.Empty
            };
        }

        #endregion

        #region Coordinate System Conversions (Unity <-> ROS)

        /// <summary>
        /// Convert position and orientation from Unity left-handed (X right, Y up, Z forward)
        /// to standard ROS right-handed coordinates (X forward, Y left, Z up).
        /// </summary>
        public static void UnityToRos(Vector3 pos, Quaternion rot,
            out double x, out double y, out double z,
            out double qx, out double qy, out double qz, out double qw)
        {
            x = pos.z;
            y = -pos.x;
            z = pos.y;
            qx = -rot.z;
            qy = rot.x;
            qz = -rot.y;
            qw = rot.w;
        }

        /// <summary>
        /// Convert position only from Unity left-handed to standard ROS right-handed coordinates.
        /// </summary>
        public static void UnityToRos(Vector3 pos, out double x, out double y, out double z)
        {
            x = pos.z;
            y = -pos.x;
            z = pos.y;
        }

        /// <summary>
        /// Convert rotation only from Unity left-handed to standard ROS right-handed coordinates.
        /// </summary>
        public static void UnityToRos(Quaternion rot, out double qx, out double qy, out double qz, out double qw)
        {
            qx = -rot.z;
            qy = rot.x;
            qz = -rot.y;
            qw = rot.w;
        }

        /// <summary>
        /// Convert position and orientation from standard ROS right-handed coordinates
        /// back to Unity left-handed.
        /// </summary>
        public static void RosToUnity(double x, double y, double z,
            double qx, double qy, double qz, double qw,
            out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3((float)(-y), (float)z, (float)x);
            rot = new Quaternion((float)qy, (float)(-qz), (float)(-qx), (float)qw);
        }

        /// <summary>
        /// Create a GeometryPoint from Unity Vector3, with optional ROS coordinate conversion.
        /// </summary>
        public static GeometryPoint ToGeometryPoint(Vector3 pos, bool convertToRos = true)
        {
            if (convertToRos)
            {
                UnityToRos(pos, out double x, out double y, out double z);
                return new GeometryPoint { x = x, y = y, z = z };
            }
            return new GeometryPoint { x = pos.x, y = pos.y, z = pos.z };
        }

        /// <summary>
        /// Create a GeometryQuaternion from Unity Quaternion, with optional ROS coordinate conversion.
        /// </summary>
        public static GeometryQuaternion ToGeometryQuaternion(Quaternion rot, bool convertToRos = true)
        {
            if (convertToRos)
            {
                UnityToRos(rot, out double qx, out double qy, out double qz, out double qw);
                return new GeometryQuaternion { x = qx, y = qy, z = qz, w = qw };
            }
            return new GeometryQuaternion { x = rot.x, y = rot.y, z = rot.z, w = rot.w };
        }

        /// <summary>
        /// Create a GeometryPose from Unity position and rotation, with optional ROS coordinate conversion.
        /// </summary>
        public static GeometryPose ToGeometryPose(Vector3 pos, Quaternion rot, bool convertToRos = true)
        {
            return new GeometryPose
            {
                position = ToGeometryPoint(pos, convertToRos),
                orientation = ToGeometryQuaternion(rot, convertToRos)
            };
        }

        public static DdsPoint ToDdsPoint(Vector3 pos, bool convertToRos = true) => ToGeometryPoint(pos, convertToRos);
        public static DdsQuaternion ToDdsQuaternion(Quaternion rot, bool convertToRos = true) => ToGeometryQuaternion(rot, convertToRos);
        public static DdsPose ToDdsPose(Vector3 pos, Quaternion rot, bool convertToRos = true) => ToGeometryPose(pos, rot, convertToRos);

        #endregion
    }
}
