using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CycloneDDS
{
    /// <summary>
    /// Standardized Channel Types mapping directly to DDS / ROS 2 message formats.
    /// </summary>
    public enum DdsChannelType
    {
        Pose = 0,       // geometry_msgs/msg/PoseStamped
        Point = 1,      // geometry_msgs/msg/PointStamped
        Vector3 = 2,    // geometry_msgs/msg/Vector3Stamped
        Float = 3,      // std_msgs/msg/Float32
        Bool = 4,       // std_msgs/msg/Bool
        Twist = 5,      // geometry_msgs/msg/Twist
        Rotation = 6,   // geometry_msgs/msg/QuaternionStamped
        Int32 = 7       // std_msgs/msg/Int32
    }

    /// <summary>
    /// ScriptableAsset defining an individual DDS topic publishing channel for an InputAction.
    /// Classified strictly by individual InputAction stream, independent of hardware platform.
    /// Publishing timing is controlled entirely by DdsPublisher's high-precision thread.
    /// Includes zero-glitch rejection and last-valid-state holding to prevent intermittent (0,0,0) dropouts.
    /// </summary>
    [CreateAssetMenu(fileName = "NewDdsChannel", menuName = "CycloneDDS/Input Action Channel", order = 1)]
    public class DdsInputActionChannel : ScriptableObject
    {
        [Header("Channel Identification")]
        [Tooltip("Enable or disable this individual channel")]
        public bool isEnabled = true;

        [Tooltip("Standardized data type mapping to DDS message")]
        public DdsChannelType channelType = DdsChannelType.Pose;

        [Tooltip("DDS / ROS 2 Topic Name (e.g. rt/xr/head/pose or rt/xr/l_hand/trigger)")]
        public string topicName = "rt/xr/head/pose";

        [Tooltip("TF Frame ID for header-stamped messages (Default: map)")]
        public string frameId = "map";

        [Tooltip("DDS Quality of Service preset")]
        public DdsQosPreset qos = DdsQosPreset.SensorData;

        [Header("Input Action Binding")]
        [Tooltip("The single InputAction to sample and publish (Pose, Vector3, Float, or Bool)")]
        public InputActionProperty inputAction;

        [Header("Data Formatting & Filtering")]
        [Tooltip("Convert coordinates from Unity left-handed (X right, Y up, Z fwd) to standard ROS right-handed (X fwd, Y left, Z up)")]
        public bool convertToRosCoordinates = true;

        #region Runtime State (Non-Serialized)

        [NonSerialized] public int ddsHandle = -1;
        [NonSerialized] public long publishedCount = 0;
        [NonSerialized] public int matchedSubscribers = 0;
        #endregion

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(frameId))
            {
                frameId = "map";
            }
        }

        public void EnableActions()
        {
            if (inputAction.action != null && !inputAction.action.enabled)
                inputAction.action.Enable();
        }

        public void DisableActions()
        {
            // Intentionally NO-OP for shared InputActionReferences.
            // Shared project actions (like Head Tracking centerEyePosition/centerEyeRotation and Controller poses)
            // are shared with Unity's TrackedPoseDriver, XR Origin, and UI Interactors.
            // Disabling them freezes the VR Camera and locks the display!
        }

        public bool Initialize(int domainId)
        {
            if (!isEnabled || string.IsNullOrWhiteSpace(topicName)) return false;

            EnableActions();

            DdsMessageType msgType;
            switch (channelType)
            {
                case DdsChannelType.Pose:
                    msgType = DdsMessageType.GeometryPoseStamped;
                    break;
                case DdsChannelType.Point:
                    msgType = DdsMessageType.GeometryPointStamped;
                    break;
                case DdsChannelType.Vector3:
                    msgType = DdsMessageType.GeometryVector3Stamped;
                    break;
                case DdsChannelType.Float:
                    msgType = DdsMessageType.StdFloat32;
                    break;
                case DdsChannelType.Bool:
                    msgType = DdsMessageType.StdBool;
                    break;
                case DdsChannelType.Twist:
                    msgType = DdsMessageType.GeometryTwist;
                    break;
                case DdsChannelType.Rotation:
                    msgType = DdsMessageType.GeometryQuaternionStamped;
                    break;
                case DdsChannelType.Int32:
                    msgType = DdsMessageType.StdInt32;
                    break;
                default:
                    msgType = DdsMessageType.GeometryPoseStamped;
                    break;
            }

            ddsHandle = CddsNative.cdds_create_publisher(topicName.Trim(), msgType, qos);
            if (ddsHandle <= 0)
            {
                DdsLog.Error($"Failed to create DDS publisher for channel '{name}' on topic '{topicName}': handle={ddsHandle}");
                return false;
            }

            publishedCount = 0;
            DdsLog.Info($"Channel '{name}' initialized on topic '{topicName}' ({channelType}, QoS: {qos}, FrameId: {frameId})");
            return true;
        }

        public void Publish(long timestampNs)
        {
            if (!isEnabled || ddsHandle <= 0) return;

            string fid = string.IsNullOrEmpty(frameId) ? "map" : frameId;

            switch (channelType)
            {
                case DdsChannelType.Pose:
                    PublishPose(timestampNs, fid);
                    break;

                case DdsChannelType.Point:
                    PublishPoint(timestampNs, fid);
                    break;

                case DdsChannelType.Vector3:
                    PublishVector3(timestampNs, fid);
                    break;

                case DdsChannelType.Float:
                    PublishFloat();
                    break;

                case DdsChannelType.Bool:
                    PublishBool();
                    break;

                case DdsChannelType.Twist:
                    PublishTwist();
                    break;

                case DdsChannelType.Rotation:
                    PublishRotation(timestampNs, fid);
                    break;

                case DdsChannelType.Int32:
                    PublishInt32();
                    break;
            }

            if (publishedCount % 50 == 0 && ddsHandle > 0)
            {
                matchedSubscribers = CddsNative.cdds_get_matched_subscriptions(ddsHandle);
            }
        }

        private void PublishPose(long timestampNs, string fid)
        {
            if (TryGetPose(out Vector3 pos, out Quaternion rot))
            {
                if (convertToRosCoordinates)
                {
                    DdsConfig.UnityToRos(pos, rot, out double x, out double y, out double z, out double qx, out double qy, out double qz, out double qw);
                    CddsNative.cdds_publish_pose_stamped(ddsHandle, timestampNs, fid, x, y, z, qx, qy, qz, qw);
                }
                else
                {
                    CddsNative.cdds_publish_pose_stamped(ddsHandle, timestampNs, fid, pos.x, pos.y, pos.z, rot.x, rot.y, rot.z, rot.w);
                }
                publishedCount++;
            }
        }

        private void PublishPoint(long timestampNs, string fid)
        {
            if (TryGetVector3(out Vector3 pos))
            {
                if (convertToRosCoordinates)
                {
                    DdsConfig.UnityToRos(pos, out double x, out double y, out double z);
                    CddsNative.cdds_publish_point_stamped(ddsHandle, timestampNs, fid, x, y, z);
                }
                else
                {
                    CddsNative.cdds_publish_point_stamped(ddsHandle, timestampNs, fid, pos.x, pos.y, pos.z);
                }
                publishedCount++;
            }
        }

        private void PublishVector3(long timestampNs, string fid)
        {
            if (TryGetVector3(out Vector3 vec))
            {
                if (convertToRosCoordinates)
                {
                    DdsConfig.UnityToRos(vec, out double x, out double y, out double z);
                    CddsNative.cdds_publish_vector3_stamped(ddsHandle, timestampNs, fid, x, y, z);
                }
                else
                {
                    CddsNative.cdds_publish_vector3_stamped(ddsHandle, timestampNs, fid, vec.x, vec.y, vec.z);
                }
                publishedCount++;
            }
        }

        private void PublishFloat()
        {
            if (TryGetFloat(out float val))
            {
                CddsNative.cdds_publish_float(ddsHandle, val);
                publishedCount++;
            }
        }

        private void PublishBool()
        {
            if (TryGetBool(out bool val))
            {
                CddsNative.cdds_publish_bool(ddsHandle, val);
                publishedCount++;
            }
        }

        private void PublishRotation(long timestampNs, string fid)
        {
            if (TryGetRotation(out Quaternion rot))
            {
                if (convertToRosCoordinates)
                {
                    DdsConfig.UnityToRos(rot, out double qx, out double qy, out double qz, out double qw);
                    CddsNative.cdds_publish_quaternion_stamped(ddsHandle, timestampNs, fid, qx, qy, qz, qw);
                }
                else
                {
                    CddsNative.cdds_publish_quaternion_stamped(ddsHandle, timestampNs, fid, rot.x, rot.y, rot.z, rot.w);
                }
                publishedCount++;
            }
        }

        private void PublishInt32()
        {
            if (TryGetInt(out int val))
            {
                CddsNative.cdds_publish_int32(ddsHandle, val);
                publishedCount++;
            }
        }

        private void PublishTwist()
        {
            if (TryGetVector2(out Vector2 v2))
            {
                if (convertToRosCoordinates)
                {
                    CddsNative.cdds_publish_twist(ddsHandle, v2.y, -v2.x, 0, 0, 0, 0);
                }
                else
                {
                    CddsNative.cdds_publish_twist(ddsHandle, v2.x, v2.y, 0, 0, 0, 0);
                }
                publishedCount++;
            }
        }

        #region Value Extraction Helpers

        private bool TryGetPose(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;

            if (inputAction.action != null && inputAction.action.enabled)
            {
                try
                {
                    var p = inputAction.action.ReadValue<Pose>();
                    pos = p.position;
                    rot = p.rotation;
                    return true;
                }
                catch { }

                try
                {
                    pos = inputAction.action.ReadValue<Vector3>();
                    return true;
                }
                catch { }
            }
            return false;
        }

        private bool TryGetVector3(out Vector3 vec)
        {
            vec = Vector3.zero;
            if (inputAction.action != null && inputAction.action.enabled)
            {
                try
                {
                    vec = inputAction.action.ReadValue<Vector3>();
                    return true;
                }
                catch { }

                try
                {
                    var p = inputAction.action.ReadValue<Pose>();
                    vec = p.position;
                    return true;
                }
                catch { }
            }
            return false;
        }

        private bool TryGetRotation(out Quaternion rot)
        {
            rot = Quaternion.identity;
            if (inputAction.action != null && inputAction.action.enabled)
            {
                try
                {
                    rot = inputAction.action.ReadValue<Quaternion>();
                    return true;
                }
                catch { }

                try
                {
                    var p = inputAction.action.ReadValue<Pose>();
                    rot = p.rotation;
                    return true;
                }
                catch { }
            }
            return false;
        }

        private bool TryGetFloat(out float val)
        {
            val = 0f;
            if (inputAction.action != null && inputAction.action.enabled)
            {
                try
                {
                    val = inputAction.action.ReadValue<float>();
                    return true;
                }
                catch { }

                try
                {
                    val = inputAction.action.IsPressed() ? 1.0f : 0.0f;
                    return true;
                }
                catch { }
            }
            return false;
        }

        private bool TryGetBool(out bool val)
        {
            val = false;
            if (inputAction.action != null && inputAction.action.enabled)
            {
                try
                {
                    val = inputAction.action.IsPressed();
                    return true;
                }
                catch { }

                try
                {
                    val = inputAction.action.ReadValue<float>() > 0.5f;
                    return true;
                }
                catch { }
            }
            return false;
        }

        private bool TryGetVector2(out Vector2 vec)
        {
            vec = Vector2.zero;
            if (inputAction.action != null && inputAction.action.enabled)
            {
                try
                {
                    vec = inputAction.action.ReadValue<Vector2>();
                    return true;
                }
                catch { }
            }
            return false;
        }

        private bool TryGetInt(out int val)
        {
            val = 0;
            if (inputAction.action != null && inputAction.action.enabled)
            {
                try
                {
                    val = inputAction.action.ReadValue<int>();
                    return true;
                }
                catch { }
            }
            return false;
        }

        #endregion
    }
}
