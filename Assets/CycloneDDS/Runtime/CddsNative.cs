using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace CycloneDDS
{
    public enum DdsMessageType
    {
        Unknown = 0,

        // --- std_msgs ---
        StdBool = 1,
        StdByte = 2,
        StdChar = 3,
        StdColorRGBA = 4,
        StdEmpty = 5,
        StdFloat32 = 6,
        StdFloat64 = 7,
        StdInt8 = 8,
        StdInt16 = 9,
        StdInt32 = 10,
        StdInt64 = 11,
        StdUInt8 = 12,
        StdUInt16 = 13,
        StdUInt32 = 14,
        StdUInt64 = 15,
        StdString = 16,
        StdFloat32MultiArray = 17,

        // --- geometry_msgs ---
        GeometryPoint = 20,
        GeometryPoint32 = 21,
        GeometryPointStamped = 22,
        GeometryVector3 = 23,
        GeometryVector3Stamped = 24,
        GeometryQuaternion = 25,
        GeometryQuaternionStamped = 26,
        GeometryPose = 27,
        GeometryPose2D = 28,
        GeometryPoseStamped = 29,
        GeometryTransform = 30,
        GeometryTransformStamped = 31,
        GeometryTwist = 32,
        GeometryTwistStamped = 33,
        GeometryAccel = 34,
        GeometryAccelStamped = 35,
        GeometryWrench = 36,
        GeometryWrenchStamped = 37,

        // --- sensor_msgs ---
        SensorJoy = 50,
        SensorJointState = 51,
        SensorImu = 52,
        SensorBatteryState = 53
    }

    public enum DdsQosPreset
    {
        SensorData = 0,     // BestEffort + KeepLast(1)
        Default = 1,        // Reliable + KeepLast(10)
        StateTransient = 2  // Reliable + TransientLocal(1)
    }

    /// <summary>
    /// Pure, Low-Level CycloneDDS C P/Invoke Primitives
    /// </summary>
    public static class CddsNative
    {
        public const string LibName = "cdds_plugin";

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        static CddsNative()
        {
            try
            {
                string basePath = Path.Combine(Application.dataPath, "CycloneDDS", "Plugins", "Windows", "x86_64");
                string ddscPath = Path.Combine(basePath, "ddsc.dll");
                string pluginPath = Path.Combine(basePath, "cdds_plugin.dll");

                if (File.Exists(ddscPath)) LoadLibrary(ddscPath);
                if (File.Exists(pluginPath)) LoadLibrary(pluginPath);
            }
            catch (Exception ex)
            {
                DdsLog.Warning($"Preload warning: {ex.Message}");
            }
        }
#endif

        // Lifecycle
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_init(int domain_id, string peer_ip_or_xml);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void cdds_shutdown();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool cdds_is_initialized();

        // Entity Management
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_create_publisher(string topic_name, DdsMessageType type, DdsQosPreset qos_preset);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_create_subscriber(string topic_name, DdsMessageType type, DdsQosPreset qos_preset);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void cdds_destroy_entity(int handle);

        // Raw Struct Memory Access
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_raw(int pub_handle, IntPtr sample_data);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_raw(int sub_handle, IntPtr out_sample, UIntPtr sample_size);

        // std_msgs Low-Level Publishing
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_bool(int pub_handle, bool value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_byte(int pub_handle, byte value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_char(int pub_handle, byte value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_color_rgba(int pub_handle, float r, float g, float b, float a);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_empty(int pub_handle);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_float(int pub_handle, float value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_double(int pub_handle, double value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_int8(int pub_handle, sbyte value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_int16(int pub_handle, short value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_int32(int pub_handle, int value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_int64(int pub_handle, long value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_uint8(int pub_handle, byte value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_uint16(int pub_handle, ushort value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_uint32(int pub_handle, uint value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_uint64(int pub_handle, ulong value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_string(int pub_handle, string str);

        // geometry_msgs Low-Level Publishing
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_point(int pub_handle, double x, double y, double z);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_point32(int pub_handle, float x, float y, float z);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_point_stamped(int pub_handle, long timestamp_ns, string frame_id, double x, double y, double z);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_vector3(int pub_handle, double x, double y, double z);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_vector3_stamped(int pub_handle, long timestamp_ns, string frame_id, double x, double y, double z);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_quaternion(int pub_handle, double x, double y, double z, double w);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_quaternion_stamped(int pub_handle, long timestamp_ns, string frame_id, double x, double y, double z, double w);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_pose(int pub_handle, double px, double py, double pz, double qx, double qy, double qz, double qw);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_pose2d(int pub_handle, double x, double y, double theta);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_pose_stamped(
            int pub_handle, long timestamp_ns, string frame_id,
            double px, double py, double pz,
            double qx, double qy, double qz, double qw
        );

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_transform(int pub_handle, double tx, double ty, double tz, double qx, double qy, double qz, double qw);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_transform_stamped(
            int pub_handle, long timestamp_ns, string frame_id, string child_frame_id,
            double tx, double ty, double tz,
            double qx, double qy, double qz, double qw
        );

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_twist(int pub_handle, double vx, double vy, double vz, double wx, double wy, double wz);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_twist_stamped(
            int pub_handle, long timestamp_ns, string frame_id,
            double vx, double vy, double vz,
            double wx, double wy, double wz
        );

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_accel(int pub_handle, double ax, double ay, double az, double aax, double aay, double aaz);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_accel_stamped(
            int pub_handle, long timestamp_ns, string frame_id,
            double ax, double ay, double az,
            double aax, double aay, double aaz
        );

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_wrench(int pub_handle, double fx, double fy, double fz, double tx, double ty, double tz);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_publish_wrench_stamped(
            int pub_handle, long timestamp_ns, string frame_id,
            double fx, double fy, double fz,
            double tx, double ty, double tz
        );

        // std_msgs Low-Level Taking (Receiving)
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_bool(int sub_handle, out bool out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_byte(int sub_handle, out byte out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_char(int sub_handle, out byte out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_color_rgba(int sub_handle, [In, Out] float[] out_rgba);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_float(int sub_handle, out float out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_double(int sub_handle, out double out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_int8(int sub_handle, out sbyte out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_int16(int sub_handle, out short out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_int32(int sub_handle, out int out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_int64(int sub_handle, out long out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_uint8(int sub_handle, out byte out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_uint16(int sub_handle, out ushort out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_uint32(int sub_handle, out uint out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_uint64(int sub_handle, out ulong out_value);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_string(int sub_handle, StringBuilder out_buf, int max_len);

        // geometry_msgs Low-Level Taking (Receiving)
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_point(int sub_handle, [In, Out] double[] out_xyz);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_point32(int sub_handle, [In, Out] float[] out_xyz);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_point_stamped(int sub_handle, out long out_timestamp_ns, StringBuilder out_frame_id, int max_frame_len, [In, Out] double[] out_xyz);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_vector3(int sub_handle, [In, Out] double[] out_xyz);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_vector3_stamped(int sub_handle, out long out_timestamp_ns, StringBuilder out_frame_id, int max_frame_len, [In, Out] double[] out_xyz);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_quaternion(int sub_handle, [In, Out] double[] out_xyzw);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_quaternion_stamped(int sub_handle, out long out_timestamp_ns, StringBuilder out_frame_id, int max_frame_len, [In, Out] double[] out_xyzw);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_pose(int sub_handle, [In, Out] double[] out_pos_xyz, [In, Out] double[] out_rot_xyzw);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_pose2d(int sub_handle, out double out_x, out double out_y, out double out_theta);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_pose_stamped(
            int sub_handle, out long out_timestamp_ns,
            StringBuilder out_frame_id, int max_frame_len,
            [In, Out] double[] out_pos_xyz, [In, Out] double[] out_rot_xyzw
        );

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_transform(int sub_handle, [In, Out] double[] out_trans_xyz, [In, Out] double[] out_rot_xyzw);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_transform_stamped(
            int sub_handle, out long out_timestamp_ns,
            StringBuilder out_frame_id, int max_frame_len,
            StringBuilder out_child_frame_id, int max_child_len,
            [In, Out] double[] out_trans_xyz, [In, Out] double[] out_rot_xyzw
        );

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_twist(int sub_handle, [In, Out] double[] out_linear_xyz, [In, Out] double[] out_angular_xyz);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_twist_stamped(
            int sub_handle, out long out_timestamp_ns,
            StringBuilder out_frame_id, int max_frame_len,
            [In, Out] double[] out_linear_xyz, [In, Out] double[] out_angular_xyz
        );

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_accel(int sub_handle, [In, Out] double[] out_linear_xyz, [In, Out] double[] out_angular_xyz);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_accel_stamped(
            int sub_handle, out long out_timestamp_ns,
            StringBuilder out_frame_id, int max_frame_len,
            [In, Out] double[] out_linear_xyz, [In, Out] double[] out_angular_xyz
        );

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_wrench(int sub_handle, [In, Out] double[] out_force_xyz, [In, Out] double[] out_torque_xyz);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_take_wrench_stamped(
            int sub_handle, out long out_timestamp_ns,
            StringBuilder out_frame_id, int max_frame_len,
            [In, Out] double[] out_force_xyz, [In, Out] double[] out_torque_xyz
        );

        // Discovery
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_get_matched_subscriptions(int pub_handle);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int cdds_get_matched_publications(int sub_handle);
    }
}
