#ifndef CDDS_PLUGIN_H
#define CDDS_PLUGIN_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>

#if defined(_WIN32)
#define CDDS_EXPORT __declspec(dllexport)
#else
#define CDDS_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef enum {
    DDS_TYPE_UNKNOWN = 0,

    // --- std_msgs ---
    DDS_TYPE_STD_MSGS_BOOL = 1,
    DDS_TYPE_STD_MSGS_BYTE = 2,
    DDS_TYPE_STD_MSGS_CHAR = 3,
    DDS_TYPE_STD_MSGS_COLOR_RGBA = 4,
    DDS_TYPE_STD_MSGS_EMPTY = 5,
    DDS_TYPE_STD_MSGS_FLOAT32 = 6,
    DDS_TYPE_STD_MSGS_FLOAT64 = 7,
    DDS_TYPE_STD_MSGS_INT8 = 8,
    DDS_TYPE_STD_MSGS_INT16 = 9,
    DDS_TYPE_STD_MSGS_INT32 = 10,
    DDS_TYPE_STD_MSGS_INT64 = 11,
    DDS_TYPE_STD_MSGS_UINT8 = 12,
    DDS_TYPE_STD_MSGS_UINT16 = 13,
    DDS_TYPE_STD_MSGS_UINT32 = 14,
    DDS_TYPE_STD_MSGS_UINT64 = 15,
    DDS_TYPE_STD_MSGS_STRING = 16,
    DDS_TYPE_STD_MSGS_FLOAT32_MULTI_ARRAY = 17,

    // --- geometry_msgs ---
    DDS_TYPE_GEOMETRY_MSGS_POINT = 20,
    DDS_TYPE_GEOMETRY_MSGS_POINT32 = 21,
    DDS_TYPE_GEOMETRY_MSGS_POINT_STAMPED = 22,
    DDS_TYPE_GEOMETRY_MSGS_VECTOR3 = 23,
    DDS_TYPE_GEOMETRY_MSGS_VECTOR3_STAMPED = 24,
    DDS_TYPE_GEOMETRY_MSGS_QUATERNION = 25,
    DDS_TYPE_GEOMETRY_MSGS_QUATERNION_STAMPED = 26,
    DDS_TYPE_GEOMETRY_MSGS_POSE = 27,
    DDS_TYPE_GEOMETRY_MSGS_POSE2D = 28,
    DDS_TYPE_GEOMETRY_MSGS_POSE_STAMPED = 29,
    DDS_TYPE_GEOMETRY_MSGS_TRANSFORM = 30,
    DDS_TYPE_GEOMETRY_MSGS_TRANSFORM_STAMPED = 31,
    DDS_TYPE_GEOMETRY_MSGS_TWIST = 32,
    DDS_TYPE_GEOMETRY_MSGS_TWIST_STAMPED = 33,
    DDS_TYPE_GEOMETRY_MSGS_ACCEL = 34,
    DDS_TYPE_GEOMETRY_MSGS_ACCEL_STAMPED = 35,
    DDS_TYPE_GEOMETRY_MSGS_WRENCH = 36,
    DDS_TYPE_GEOMETRY_MSGS_WRENCH_STAMPED = 37,

    // --- sensor_msgs ---
    DDS_TYPE_SENSOR_MSGS_JOY = 50,
    DDS_TYPE_SENSOR_MSGS_JOINT_STATE = 51,
    DDS_TYPE_SENSOR_MSGS_IMU = 52,
    DDS_TYPE_SENSOR_MSGS_BATTERY_STATE = 53
} dds_msg_type_t;

typedef enum {
    DDS_QOS_SENSOR_DATA = 0,    // BestEffort + KeepLast(1)
    DDS_QOS_DEFAULT = 1,        // Reliable + KeepLast(10)
    DDS_QOS_STATE_TRANSIENT = 2 // Reliable + TransientLocal(1)
} dds_qos_preset_t;

// Lifecycle
CDDS_EXPORT int cdds_init(int domain_id, const char* peer_ip_or_xml);
CDDS_EXPORT void cdds_shutdown(void);
CDDS_EXPORT bool cdds_is_initialized(void);

// Entity Creation & Destruction
CDDS_EXPORT int cdds_create_publisher(const char* topic_name, dds_msg_type_t type, dds_qos_preset_t qos_preset);
CDDS_EXPORT int cdds_create_subscriber(const char* topic_name, dds_msg_type_t type, dds_qos_preset_t qos_preset);
CDDS_EXPORT void cdds_destroy_entity(int handle);

// Raw Struct-Level Access (Direct Memory)
CDDS_EXPORT int cdds_publish_raw(int pub_handle, const void* sample_data);
CDDS_EXPORT int cdds_take_raw(int sub_handle, void* out_sample, size_t sample_size);

// Low-Level std_msgs Publishing
CDDS_EXPORT int cdds_publish_bool(int pub_handle, bool value);
CDDS_EXPORT int cdds_publish_byte(int pub_handle, uint8_t value);
CDDS_EXPORT int cdds_publish_char(int pub_handle, uint8_t value);
CDDS_EXPORT int cdds_publish_color_rgba(int pub_handle, float r, float g, float b, float a);
CDDS_EXPORT int cdds_publish_empty(int pub_handle);
CDDS_EXPORT int cdds_publish_float(int pub_handle, float value);
CDDS_EXPORT int cdds_publish_double(int pub_handle, double value);
CDDS_EXPORT int cdds_publish_int8(int pub_handle, int8_t value);
CDDS_EXPORT int cdds_publish_int16(int pub_handle, int16_t value);
CDDS_EXPORT int cdds_publish_int32(int pub_handle, int32_t value);
CDDS_EXPORT int cdds_publish_int64(int pub_handle, int64_t value);
CDDS_EXPORT int cdds_publish_uint8(int pub_handle, uint8_t value);
CDDS_EXPORT int cdds_publish_uint16(int pub_handle, uint16_t value);
CDDS_EXPORT int cdds_publish_uint32(int pub_handle, uint32_t value);
CDDS_EXPORT int cdds_publish_uint64(int pub_handle, uint64_t value);
CDDS_EXPORT int cdds_publish_string(int pub_handle, const char* str);

// Low-Level geometry_msgs Publishing
CDDS_EXPORT int cdds_publish_point(int pub_handle, double x, double y, double z);
CDDS_EXPORT int cdds_publish_point32(int pub_handle, float x, float y, float z);
CDDS_EXPORT int cdds_publish_point_stamped(int pub_handle, int64_t timestamp_ns, const char* frame_id, double x, double y, double z);
CDDS_EXPORT int cdds_publish_vector3(int pub_handle, double x, double y, double z);
CDDS_EXPORT int cdds_publish_vector3_stamped(int pub_handle, int64_t timestamp_ns, const char* frame_id, double x, double y, double z);
CDDS_EXPORT int cdds_publish_quaternion(int pub_handle, double x, double y, double z, double w);
CDDS_EXPORT int cdds_publish_quaternion_stamped(int pub_handle, int64_t timestamp_ns, const char* frame_id, double x, double y, double z, double w);
CDDS_EXPORT int cdds_publish_pose(int pub_handle, double px, double py, double pz, double qx, double qy, double qz, double qw);
CDDS_EXPORT int cdds_publish_pose2d(int pub_handle, double x, double y, double theta);
CDDS_EXPORT int cdds_publish_pose_stamped(
    int pub_handle, int64_t timestamp_ns, const char* frame_id,
    double px, double py, double pz,
    double qx, double qy, double qz, double qw
);
CDDS_EXPORT int cdds_publish_transform(int pub_handle, double tx, double ty, double tz, double qx, double qy, double qz, double qw);
CDDS_EXPORT int cdds_publish_transform_stamped(
    int pub_handle, int64_t timestamp_ns, const char* frame_id, const char* child_frame_id,
    double tx, double ty, double tz,
    double qx, double qy, double qz, double qw
);
CDDS_EXPORT int cdds_publish_twist(int pub_handle, double vx, double vy, double vz, double wx, double wy, double wz);
CDDS_EXPORT int cdds_publish_twist_stamped(
    int pub_handle, int64_t timestamp_ns, const char* frame_id,
    double vx, double vy, double vz,
    double wx, double wy, double wz
);
CDDS_EXPORT int cdds_publish_accel(int pub_handle, double ax, double ay, double az, double aax, double aay, double aaz);
CDDS_EXPORT int cdds_publish_accel_stamped(
    int pub_handle, int64_t timestamp_ns, const char* frame_id,
    double ax, double ay, double az,
    double aax, double aay, double aaz
);
CDDS_EXPORT int cdds_publish_wrench(int pub_handle, double fx, double fy, double fz, double tx, double ty, double tz);
CDDS_EXPORT int cdds_publish_wrench_stamped(
    int pub_handle, int64_t timestamp_ns, const char* frame_id,
    double fx, double fy, double fz,
    double tx, double ty, double tz
);

// Low-Level std_msgs Taking (Receiving)
CDDS_EXPORT int cdds_take_bool(int sub_handle, bool* out_value);
CDDS_EXPORT int cdds_take_byte(int sub_handle, uint8_t* out_value);
CDDS_EXPORT int cdds_take_char(int sub_handle, uint8_t* out_value);
CDDS_EXPORT int cdds_take_color_rgba(int sub_handle, float* out_rgba); // float[4]
CDDS_EXPORT int cdds_take_float(int sub_handle, float* out_value);
CDDS_EXPORT int cdds_take_double(int sub_handle, double* out_value);
CDDS_EXPORT int cdds_take_int8(int sub_handle, int8_t* out_value);
CDDS_EXPORT int cdds_take_int16(int sub_handle, int16_t* out_value);
CDDS_EXPORT int cdds_take_int32(int sub_handle, int32_t* out_value);
CDDS_EXPORT int cdds_take_int64(int sub_handle, int64_t* out_value);
CDDS_EXPORT int cdds_take_uint8(int sub_handle, uint8_t* out_value);
CDDS_EXPORT int cdds_take_uint16(int sub_handle, uint16_t* out_value);
CDDS_EXPORT int cdds_take_uint32(int sub_handle, uint32_t* out_value);
CDDS_EXPORT int cdds_take_uint64(int sub_handle, uint64_t* out_value);
CDDS_EXPORT int cdds_take_string(int sub_handle, char* out_buf, int max_len);

// Low-Level geometry_msgs Taking (Receiving)
CDDS_EXPORT int cdds_take_point(int sub_handle, double* out_xyz); // double[3]
CDDS_EXPORT int cdds_take_point32(int sub_handle, float* out_xyz); // float[3]
CDDS_EXPORT int cdds_take_point_stamped(int sub_handle, int64_t* out_timestamp_ns, char* out_frame_id, int max_frame_len, double* out_xyz);
CDDS_EXPORT int cdds_take_vector3(int sub_handle, double* out_xyz); // double[3]
CDDS_EXPORT int cdds_take_vector3_stamped(int sub_handle, int64_t* out_timestamp_ns, char* out_frame_id, int max_frame_len, double* out_xyz);
CDDS_EXPORT int cdds_take_quaternion(int sub_handle, double* out_xyzw); // double[4]
CDDS_EXPORT int cdds_take_quaternion_stamped(int sub_handle, int64_t* out_timestamp_ns, char* out_frame_id, int max_frame_len, double* out_xyzw);
CDDS_EXPORT int cdds_take_pose(int sub_handle, double* out_pos_xyz, double* out_rot_xyzw);
CDDS_EXPORT int cdds_take_pose2d(int sub_handle, double* out_x, double* out_y, double* out_theta);
CDDS_EXPORT int cdds_take_pose_stamped(
    int sub_handle, int64_t* out_timestamp_ns,
    char* out_frame_id, int max_frame_len,
    double* out_pos_xyz, double* out_rot_xyzw
);
CDDS_EXPORT int cdds_take_transform(int sub_handle, double* out_trans_xyz, double* out_rot_xyzw);
CDDS_EXPORT int cdds_take_transform_stamped(
    int sub_handle, int64_t* out_timestamp_ns,
    char* out_frame_id, int max_frame_len,
    char* out_child_frame_id, int max_child_len,
    double* out_trans_xyz, double* out_rot_xyzw
);
CDDS_EXPORT int cdds_take_twist(int sub_handle, double* out_linear_xyz, double* out_angular_xyz);
CDDS_EXPORT int cdds_take_twist_stamped(
    int sub_handle, int64_t* out_timestamp_ns,
    char* out_frame_id, int max_frame_len,
    double* out_linear_xyz, double* out_angular_xyz
);
CDDS_EXPORT int cdds_take_accel(int sub_handle, double* out_linear_xyz, double* out_angular_xyz);
CDDS_EXPORT int cdds_take_accel_stamped(
    int sub_handle, int64_t* out_timestamp_ns,
    char* out_frame_id, int max_frame_len,
    double* out_linear_xyz, double* out_angular_xyz
);
CDDS_EXPORT int cdds_take_wrench(int sub_handle, double* out_force_xyz, double* out_torque_xyz);
CDDS_EXPORT int cdds_take_wrench_stamped(
    int sub_handle, int64_t* out_timestamp_ns,
    char* out_frame_id, int max_frame_len,
    double* out_force_xyz, double* out_torque_xyz
);

// Discovery
CDDS_EXPORT int cdds_get_matched_subscriptions(int pub_handle);
CDDS_EXPORT int cdds_get_matched_publications(int sub_handle);

#ifdef __cplusplus
}
#endif

#endif // CDDS_PLUGIN_H
