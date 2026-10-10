#include "cdds_plugin.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#include <winsock2.h>
#include <iphlpapi.h>
#include <ws2tcpip.h>
#endif

#include "dds/dds.h"

// Standard OMG IDL headers for std_msgs
#include "std_msgs/msg/dds_/Bool_.h"
#include "std_msgs/msg/dds_/Byte_.h"
#include "std_msgs/msg/dds_/Char_.h"
#include "std_msgs/msg/dds_/ColorRGBA_.h"
#include "std_msgs/msg/dds_/Empty_.h"
#include "std_msgs/msg/dds_/Float32_.h"
#include "std_msgs/msg/dds_/Float64_.h"
#include "std_msgs/msg/dds_/Int8_.h"
#include "std_msgs/msg/dds_/Int16_.h"
#include "std_msgs/msg/dds_/Int32_.h"
#include "std_msgs/msg/dds_/Int64_.h"
#include "std_msgs/msg/dds_/UInt8_.h"
#include "std_msgs/msg/dds_/UInt16_.h"
#include "std_msgs/msg/dds_/UInt32_.h"
#include "std_msgs/msg/dds_/UInt64_.h"
#include "std_msgs/msg/dds_/String_.h"
#include "std_msgs/msg/dds_/Header_.h"
#include "std_msgs/msg/dds_/Float32MultiArray_.h"

// Standard OMG IDL headers for geometry_msgs
#include "geometry_msgs/msg/dds_/Point_.h"
#include "geometry_msgs/msg/dds_/Point32_.h"
#include "geometry_msgs/msg/dds_/PointStamped_.h"
#include "geometry_msgs/msg/dds_/Vector3_.h"
#include "geometry_msgs/msg/dds_/Vector3Stamped_.h"
#include "geometry_msgs/msg/dds_/Quaternion_.h"
#include "geometry_msgs/msg/dds_/QuaternionStamped_.h"
#include "geometry_msgs/msg/dds_/Pose_.h"
#include "geometry_msgs/msg/dds_/Pose2D_.h"
#include "geometry_msgs/msg/dds_/PoseStamped_.h"
#include "geometry_msgs/msg/dds_/Transform_.h"
#include "geometry_msgs/msg/dds_/TransformStamped_.h"
#include "geometry_msgs/msg/dds_/Twist_.h"
#include "geometry_msgs/msg/dds_/TwistStamped_.h"
#include "geometry_msgs/msg/dds_/Accel_.h"
#include "geometry_msgs/msg/dds_/AccelStamped_.h"
#include "geometry_msgs/msg/dds_/Wrench_.h"
#include "geometry_msgs/msg/dds_/WrenchStamped_.h"

// Standard OMG IDL headers for sensor_msgs
#include "sensor_msgs/msg/dds_/Joy_.h"
#include "sensor_msgs/msg/dds_/JointState_.h"
#include "sensor_msgs/msg/dds_/Imu_.h"
#include "sensor_msgs/msg/dds_/BatteryState_.h"

#define MAX_ENTITIES 128

typedef struct {
    int id;
    bool in_use;
    bool is_writer;
    dds_msg_type_t type;
    dds_entity_t entity;
    dds_entity_t topic;
    char topic_name[128];
} EntitySlot;

static dds_entity_t g_participant = 0;
static EntitySlot g_slots[MAX_ENTITIES];
static int g_slot_counter = 1;
static bool g_initialized = false;

static const dds_topic_descriptor_t* get_descriptor(dds_msg_type_t type) {
    switch (type) {
        // std_msgs
        case DDS_TYPE_STD_MSGS_BOOL: return &std_msgs_msg_dds__Bool__desc;
        case DDS_TYPE_STD_MSGS_BYTE: return &std_msgs_msg_dds__Byte__desc;
        case DDS_TYPE_STD_MSGS_CHAR: return &std_msgs_msg_dds__Char__desc;
        case DDS_TYPE_STD_MSGS_COLOR_RGBA: return &std_msgs_msg_dds__ColorRGBA__desc;
        case DDS_TYPE_STD_MSGS_EMPTY: return &std_msgs_msg_dds__Empty__desc;
        case DDS_TYPE_STD_MSGS_FLOAT32: return &std_msgs_msg_dds__Float32__desc;
        case DDS_TYPE_STD_MSGS_FLOAT64: return &std_msgs_msg_dds__Float64__desc;
        case DDS_TYPE_STD_MSGS_INT8: return &std_msgs_msg_dds__Int8__desc;
        case DDS_TYPE_STD_MSGS_INT16: return &std_msgs_msg_dds__Int16__desc;
        case DDS_TYPE_STD_MSGS_INT32: return &std_msgs_msg_dds__Int32__desc;
        case DDS_TYPE_STD_MSGS_INT64: return &std_msgs_msg_dds__Int64__desc;
        case DDS_TYPE_STD_MSGS_UINT8: return &std_msgs_msg_dds__UInt8__desc;
        case DDS_TYPE_STD_MSGS_UINT16: return &std_msgs_msg_dds__UInt16__desc;
        case DDS_TYPE_STD_MSGS_UINT32: return &std_msgs_msg_dds__UInt32__desc;
        case DDS_TYPE_STD_MSGS_UINT64: return &std_msgs_msg_dds__UInt64__desc;
        case DDS_TYPE_STD_MSGS_STRING: return &std_msgs_msg_dds__String__desc;
        case DDS_TYPE_STD_MSGS_FLOAT32_MULTI_ARRAY: return &std_msgs_msg_dds__Float32MultiArray__desc;

        // geometry_msgs
        case DDS_TYPE_GEOMETRY_MSGS_POINT: return &geometry_msgs_msg_dds__Point__desc;
        case DDS_TYPE_GEOMETRY_MSGS_POINT32: return &geometry_msgs_msg_dds__Point32__desc;
        case DDS_TYPE_GEOMETRY_MSGS_POINT_STAMPED: return &geometry_msgs_msg_dds__PointStamped__desc;
        case DDS_TYPE_GEOMETRY_MSGS_VECTOR3: return &geometry_msgs_msg_dds__Vector3__desc;
        case DDS_TYPE_GEOMETRY_MSGS_VECTOR3_STAMPED: return &geometry_msgs_msg_dds__Vector3Stamped__desc;
        case DDS_TYPE_GEOMETRY_MSGS_QUATERNION: return &geometry_msgs_msg_dds__Quaternion__desc;
        case DDS_TYPE_GEOMETRY_MSGS_QUATERNION_STAMPED: return &geometry_msgs_msg_dds__QuaternionStamped__desc;
        case DDS_TYPE_GEOMETRY_MSGS_POSE: return &geometry_msgs_msg_dds__Pose__desc;
        case DDS_TYPE_GEOMETRY_MSGS_POSE2D: return &geometry_msgs_msg_dds__Pose2D__desc;
        case DDS_TYPE_GEOMETRY_MSGS_POSE_STAMPED: return &geometry_msgs_msg_dds__PoseStamped__desc;
        case DDS_TYPE_GEOMETRY_MSGS_TRANSFORM: return &geometry_msgs_msg_dds__Transform__desc;
        case DDS_TYPE_GEOMETRY_MSGS_TRANSFORM_STAMPED: return &geometry_msgs_msg_dds__TransformStamped__desc;
        case DDS_TYPE_GEOMETRY_MSGS_TWIST: return &geometry_msgs_msg_dds__Twist__desc;
        case DDS_TYPE_GEOMETRY_MSGS_TWIST_STAMPED: return &geometry_msgs_msg_dds__TwistStamped__desc;
        case DDS_TYPE_GEOMETRY_MSGS_ACCEL: return &geometry_msgs_msg_dds__Accel__desc;
        case DDS_TYPE_GEOMETRY_MSGS_ACCEL_STAMPED: return &geometry_msgs_msg_dds__AccelStamped__desc;
        case DDS_TYPE_GEOMETRY_MSGS_WRENCH: return &geometry_msgs_msg_dds__Wrench__desc;
        case DDS_TYPE_GEOMETRY_MSGS_WRENCH_STAMPED: return &geometry_msgs_msg_dds__WrenchStamped__desc;

        // sensor_msgs
        case DDS_TYPE_SENSOR_MSGS_JOY: return &sensor_msgs_msg_dds__Joy__desc;
        case DDS_TYPE_SENSOR_MSGS_JOINT_STATE: return &sensor_msgs_msg_dds__JointState__desc;
        case DDS_TYPE_SENSOR_MSGS_IMU: return &sensor_msgs_msg_dds__Imu__desc;
        case DDS_TYPE_SENSOR_MSGS_BATTERY_STATE: return &sensor_msgs_msg_dds__BatteryState__desc;

        default:
            return NULL;
    }
}

static dds_qos_t* create_preset_qos(dds_qos_preset_t preset) {
    dds_qos_t *qos = dds_create_qos();
    if (preset == DDS_QOS_SENSOR_DATA) {
        dds_qset_reliability(qos, DDS_RELIABILITY_BEST_EFFORT, 0);
        dds_qset_history(qos, DDS_HISTORY_KEEP_LAST, 1);
        dds_qset_durability(qos, DDS_DURABILITY_VOLATILE);
    } else if (preset == DDS_QOS_STATE_TRANSIENT) {
        dds_qset_reliability(qos, DDS_RELIABILITY_RELIABLE, DDS_SECS(1));
        dds_qset_history(qos, DDS_HISTORY_KEEP_LAST, 1);
        dds_qset_durability(qos, DDS_DURABILITY_TRANSIENT_LOCAL);
    } else {
        dds_qset_reliability(qos, DDS_RELIABILITY_RELIABLE, DDS_SECS(1));
        dds_qset_history(qos, DDS_HISTORY_KEEP_LAST, 10);
        dds_qset_durability(qos, DDS_DURABILITY_VOLATILE);
    }
    return qos;
}

static EntitySlot* allocate_slot(void) {
    for (int i = 0; i < MAX_ENTITIES; i++) {
        if (!g_slots[i].in_use) {
            g_slots[i].id = g_slot_counter++;
            g_slots[i].in_use = true;
            return &g_slots[i];
        }
    }
    return NULL;
}

static EntitySlot* find_slot(int handle) {
    if (handle <= 0) return NULL;
    for (int i = 0; i < MAX_ENTITIES; i++) {
        if (g_slots[i].in_use && g_slots[i].id == handle) {
            return &g_slots[i];
        }
    }
    return NULL;
}

static inline void fill_time(int64_t timestamp_ns, struct builtin_interfaces_msg_dds__Time_* stamp) {
    if (timestamp_ns <= 0) {
        stamp->sec = 0;
        stamp->nanosec = 0;
    } else {
        stamp->sec = (int32_t)(timestamp_ns / 1000000000LL);
        stamp->nanosec = (uint32_t)(timestamp_ns % 1000000000LL);
    }
}

#if defined(_WIN32)
static void get_matching_local_ip(const char* peer_ip, char* local_ip, size_t max_len) {
    if (!peer_ip || !local_ip || max_len == 0) return;
    local_ip[0] = '\0';

    char subnet_prefix[64];
    const char* last_dot = strrchr(peer_ip, '.');
    if (!last_dot) return;
    size_t prefix_len = (size_t)(last_dot - peer_ip + 1);
    if (prefix_len >= sizeof(subnet_prefix)) return;
    strncpy(subnet_prefix, peer_ip, prefix_len);
    subnet_prefix[prefix_len] = '\0';

    ULONG flags = GAA_FLAG_INCLUDE_PREFIX;
    ULONG outBufLen = 15000;
    PIP_ADAPTER_ADDRESSES pAddresses = (IP_ADAPTER_ADDRESSES*)malloc(outBufLen);
    if (!pAddresses) return;

    if (GetAdaptersAddresses(AF_INET, flags, NULL, pAddresses, &outBufLen) == NO_ERROR) {
        for (PIP_ADAPTER_ADDRESSES pCurr = pAddresses; pCurr != NULL; pCurr = pCurr->Next) {
            if (pCurr->OperStatus != IfOperStatusUp) continue;
            for (PIP_ADAPTER_UNICAST_ADDRESS pUnicast = pCurr->FirstUnicastAddress; pUnicast != NULL; pUnicast = pUnicast->Next) {
                if (pUnicast->Address.lpSockaddr->sa_family == AF_INET) {
                    struct sockaddr_in* sa_in = (struct sockaddr_in*)pUnicast->Address.lpSockaddr;
                    char ip_buf[INET_ADDRSTRLEN];
                    if (inet_ntop(AF_INET, &(sa_in->sin_addr), ip_buf, sizeof(ip_buf))) {
                        if (strncmp(ip_buf, subnet_prefix, prefix_len) == 0) {
                            snprintf(local_ip, max_len, "%s", ip_buf);
                            free(pAddresses);
                            return;
                        }
                    }
                }
            }
        }
    }
    free(pAddresses);
}
#endif

CDDS_EXPORT int cdds_init(int domain_id, const char* peer_ip_or_xml) {
    if (g_initialized && g_participant > 0) {
        return 0; // Already initialized
    }

    memset(g_slots, 0, sizeof(g_slots));

    if (peer_ip_or_xml != NULL && strlen(peer_ip_or_xml) > 0) {
        char xml_content[1024];
        if (strstr(peer_ip_or_xml, "<CycloneDDS") != NULL) {
            snprintf(xml_content, sizeof(xml_content), "%s", peer_ip_or_xml);
        } else {
            char local_ip[64] = {0};
#if defined(_WIN32)
            get_matching_local_ip(peer_ip_or_xml, local_ip, sizeof(local_ip));
#endif
            if (strlen(local_ip) > 0) {
                snprintf(xml_content, sizeof(xml_content),
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>"
                    "<CycloneDDS xmlns=\"https://cdds.io/config\">"
                    "<Domain id=\"any\">"
                    "<General>"
                    "<Interfaces><NetworkInterface address=\"%s\" autodetermine=\"false\"/></Interfaces>"
                    "<AllowMulticast>default</AllowMulticast>"
                    "</General>"
                    "<Discovery><Peers><Peer address=\"%s\"/></Peers></Discovery>"
                    "</Domain></CycloneDDS>", local_ip, peer_ip_or_xml);
            } else {
                snprintf(xml_content, sizeof(xml_content),
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>"
                    "<CycloneDDS xmlns=\"https://cdds.io/config\">"
                    "<Domain id=\"any\">"
                    "<General><AllowMulticast>default</AllowMulticast></General>"
                    "<Discovery><Peers><Peer address=\"%s\"/></Peers></Discovery>"
                    "</Domain></CycloneDDS>", peer_ip_or_xml);
            }
        }

#if defined(_WIN32)
        _putenv_s("CYCLONEDDS_URI", xml_content);
        SetEnvironmentVariableA("CYCLONEDDS_URI", xml_content);
#else
        setenv("CYCLONEDDS_URI", xml_content, 1);
#endif
    }

    g_participant = dds_create_participant((dds_domainid_t)domain_id, NULL, NULL);
    if (g_participant < 0) {
        return (int)g_participant;
    }

    g_initialized = true;
    return 0;
}

CDDS_EXPORT void cdds_shutdown(void) {
    if (!g_initialized) return;

    for (int i = 0; i < MAX_ENTITIES; i++) {
        if (g_slots[i].in_use) {
            if (g_slots[i].entity > 0) dds_delete(g_slots[i].entity);
            if (g_slots[i].topic > 0) dds_delete(g_slots[i].topic);
            g_slots[i].in_use = false;
        }
    }

    if (g_participant > 0) {
        dds_delete(g_participant);
        g_participant = 0;
    }
    g_initialized = false;
}

CDDS_EXPORT bool cdds_is_initialized(void) {
    return g_initialized;
}

CDDS_EXPORT int cdds_create_publisher(const char* topic_name, dds_msg_type_t type, dds_qos_preset_t qos_preset) {
    if (!g_initialized || g_participant <= 0 || !topic_name) return -1;
    const dds_topic_descriptor_t* desc = get_descriptor(type);
    if (!desc) return -2;

    EntitySlot* slot = allocate_slot();
    if (!slot) return -3;

    dds_qos_t* qos = create_preset_qos(qos_preset);
    dds_entity_t topic = dds_create_topic(g_participant, desc, topic_name, qos, NULL);
    if (topic < 0) {
        dds_delete_qos(qos);
        slot->in_use = false;
        return (int)topic;
    }

    dds_entity_t writer = dds_create_writer(g_participant, topic, qos, NULL);
    dds_delete_qos(qos);
    if (writer < 0) {
        dds_delete(topic);
        slot->in_use = false;
        return (int)writer;
    }

    slot->is_writer = true;
    slot->type = type;
    slot->entity = writer;
    slot->topic = topic;
    strncpy(slot->topic_name, topic_name, sizeof(slot->topic_name) - 1);
    return slot->id;
}

CDDS_EXPORT int cdds_create_subscriber(const char* topic_name, dds_msg_type_t type, dds_qos_preset_t qos_preset) {
    if (!g_initialized || g_participant <= 0 || !topic_name) return -1;
    const dds_topic_descriptor_t* desc = get_descriptor(type);
    if (!desc) return -2;

    EntitySlot* slot = allocate_slot();
    if (!slot) return -3;

    dds_qos_t* qos = create_preset_qos(qos_preset);
    dds_entity_t topic = dds_create_topic(g_participant, desc, topic_name, qos, NULL);
    if (topic < 0) {
        dds_delete_qos(qos);
        slot->in_use = false;
        return (int)topic;
    }

    dds_entity_t reader = dds_create_reader(g_participant, topic, qos, NULL);
    dds_delete_qos(qos);
    if (reader < 0) {
        dds_delete(topic);
        slot->in_use = false;
        return (int)reader;
    }

    slot->is_writer = false;
    slot->type = type;
    slot->entity = reader;
    slot->topic = topic;
    strncpy(slot->topic_name, topic_name, sizeof(slot->topic_name) - 1);
    return slot->id;
}

CDDS_EXPORT void cdds_destroy_entity(int handle) {
    EntitySlot* slot = find_slot(handle);
    if (!slot) return;
    if (slot->entity > 0) dds_delete(slot->entity);
    if (slot->topic > 0) dds_delete(slot->topic);
    slot->in_use = false;
}

// Raw Memory Access
CDDS_EXPORT int cdds_publish_raw(int pub_handle, const void* sample_data) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer || !sample_data) return -1;
    return dds_write(slot->entity, sample_data);
}

CDDS_EXPORT int cdds_take_raw(int sub_handle, void* out_sample, size_t sample_size) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer || !out_sample) return -1;

    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int valid = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            memcpy(out_sample, samples[0], sample_size);
            valid = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return valid;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_latest_raw(int sub_handle, void* out_buffer, int buffer_size) {
    return cdds_take_raw(sub_handle, out_buffer, (size_t)buffer_size);
}

// Low-Level std_msgs Publishing
CDDS_EXPORT int cdds_publish_bool(int pub_handle, bool value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__Bool_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_byte(int pub_handle, uint8_t value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__Byte_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_char(int pub_handle, uint8_t value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__Char_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_color_rgba(int pub_handle, float r, float g, float b, float a) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__ColorRGBA_ msg;
    msg.r = r; msg.g = g; msg.b = b; msg.a = a;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_empty(int pub_handle) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__Empty_ msg;
    msg.structure_needs_at_least_one_member = 0;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_float(int pub_handle, float value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__Float32_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_double(int pub_handle, double value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__Float64_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_int8(int pub_handle, int8_t value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__Int8_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_int16(int pub_handle, int16_t value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__Int16_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_int32(int pub_handle, int32_t value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__Int32_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_int64(int pub_handle, int64_t value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__Int64_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_uint8(int pub_handle, uint8_t value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__UInt8_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_uint16(int pub_handle, uint16_t value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__UInt16_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_uint32(int pub_handle, uint32_t value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__UInt32_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_uint64(int pub_handle, uint64_t value) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__UInt64_ msg;
    msg.data = value;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_string(int pub_handle, const char* str) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct std_msgs_msg_dds__String_ msg;
    msg.data = (char*)(str ? str : "");
    return dds_write(slot->entity, &msg);
}

// Low-Level geometry_msgs Publishing
CDDS_EXPORT int cdds_publish_point(int pub_handle, double x, double y, double z) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__Point_ msg;
    msg.x = x; msg.y = y; msg.z = z;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_point32(int pub_handle, float x, float y, float z) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__Point32_ msg;
    msg.x = x; msg.y = y; msg.z = z;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_point_stamped(int pub_handle, int64_t timestamp_ns, const char* frame_id, double x, double y, double z) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__PointStamped_ msg;
    fill_time(timestamp_ns, &msg.header.stamp);
    msg.header.frame_id = (char*)(frame_id ? frame_id : "");
    msg.point.x = x; msg.point.y = y; msg.point.z = z;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_vector3(int pub_handle, double x, double y, double z) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__Vector3_ msg;
    msg.x = x; msg.y = y; msg.z = z;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_vector3_stamped(int pub_handle, int64_t timestamp_ns, const char* frame_id, double x, double y, double z) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__Vector3Stamped_ msg;
    fill_time(timestamp_ns, &msg.header.stamp);
    msg.header.frame_id = (char*)(frame_id ? frame_id : "");
    msg.vector.x = x; msg.vector.y = y; msg.vector.z = z;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_quaternion(int pub_handle, double x, double y, double z, double w) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__Quaternion_ msg;
    msg.x = x; msg.y = y; msg.z = z; msg.w = w;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_quaternion_stamped(int pub_handle, int64_t timestamp_ns, const char* frame_id, double x, double y, double z, double w) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__QuaternionStamped_ msg;
    fill_time(timestamp_ns, &msg.header.stamp);
    msg.header.frame_id = (char*)(frame_id ? frame_id : "");
    msg.quaternion.x = x; msg.quaternion.y = y; msg.quaternion.z = z; msg.quaternion.w = w;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_pose(int pub_handle, double px, double py, double pz, double qx, double qy, double qz, double qw) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__Pose_ msg;
    msg.position.x = px; msg.position.y = py; msg.position.z = pz;
    msg.orientation.x = qx; msg.orientation.y = qy; msg.orientation.z = qz; msg.orientation.w = qw;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_pose2d(int pub_handle, double x, double y, double theta) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__Pose2D_ msg;
    msg.x = x; msg.y = y; msg.theta = theta;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_pose_stamped(
    int pub_handle, int64_t timestamp_ns, const char* frame_id,
    double px, double py, double pz,
    double qx, double qy, double qz, double qw
) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__PoseStamped_ msg;
    fill_time(timestamp_ns, &msg.header.stamp);
    msg.header.frame_id = (char*)(frame_id ? frame_id : "");
    msg.pose.position.x = px; msg.pose.position.y = py; msg.pose.position.z = pz;
    msg.pose.orientation.x = qx; msg.pose.orientation.y = qy; msg.pose.orientation.z = qz; msg.pose.orientation.w = qw;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_transform(int pub_handle, double tx, double ty, double tz, double qx, double qy, double qz, double qw) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__Transform_ msg;
    msg.translation.x = tx; msg.translation.y = ty; msg.translation.z = tz;
    msg.rotation.x = qx; msg.rotation.y = qy; msg.rotation.z = qz; msg.rotation.w = qw;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_transform_stamped(
    int pub_handle, int64_t timestamp_ns, const char* frame_id, const char* child_frame_id,
    double tx, double ty, double tz,
    double qx, double qy, double qz, double qw
) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__TransformStamped_ msg;
    fill_time(timestamp_ns, &msg.header.stamp);
    msg.header.frame_id = (char*)(frame_id ? frame_id : "");
    msg.child_frame_id = (char*)(child_frame_id ? child_frame_id : "");
    msg.transform.translation.x = tx; msg.transform.translation.y = ty; msg.transform.translation.z = tz;
    msg.transform.rotation.x = qx; msg.transform.rotation.y = qy; msg.transform.rotation.z = qz; msg.transform.rotation.w = qw;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_twist(int pub_handle, double vx, double vy, double vz, double wx, double wy, double wz) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__Twist_ msg;
    msg.linear.x = vx; msg.linear.y = vy; msg.linear.z = vz;
    msg.angular.x = wx; msg.angular.y = wy; msg.angular.z = wz;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_twist_stamped(
    int pub_handle, int64_t timestamp_ns, const char* frame_id,
    double vx, double vy, double vz,
    double wx, double wy, double wz
) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__TwistStamped_ msg;
    fill_time(timestamp_ns, &msg.header.stamp);
    msg.header.frame_id = (char*)(frame_id ? frame_id : "");
    msg.twist.linear.x = vx; msg.twist.linear.y = vy; msg.twist.linear.z = vz;
    msg.twist.angular.x = wx; msg.twist.angular.y = wy; msg.twist.angular.z = wz;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_accel(int pub_handle, double ax, double ay, double az, double aax, double aay, double aaz) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__Accel_ msg;
    msg.linear.x = ax; msg.linear.y = ay; msg.linear.z = az;
    msg.angular.x = aax; msg.angular.y = aay; msg.angular.z = aaz;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_accel_stamped(
    int pub_handle, int64_t timestamp_ns, const char* frame_id,
    double ax, double ay, double az,
    double aax, double aay, double aaz
) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__AccelStamped_ msg;
    fill_time(timestamp_ns, &msg.header.stamp);
    msg.header.frame_id = (char*)(frame_id ? frame_id : "");
    msg.accel.linear.x = ax; msg.accel.linear.y = ay; msg.accel.linear.z = az;
    msg.accel.angular.x = aax; msg.accel.angular.y = aay; msg.accel.angular.z = aaz;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_wrench(int pub_handle, double fx, double fy, double fz, double tx, double ty, double tz) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__Wrench_ msg;
    msg.force.x = fx; msg.force.y = fy; msg.force.z = fz;
    msg.torque.x = tx; msg.torque.y = ty; msg.torque.z = tz;
    return dds_write(slot->entity, &msg);
}

CDDS_EXPORT int cdds_publish_wrench_stamped(
    int pub_handle, int64_t timestamp_ns, const char* frame_id,
    double fx, double fy, double fz,
    double tx, double ty, double tz
) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer) return -1;
    struct geometry_msgs_msg_dds__WrenchStamped_ msg;
    fill_time(timestamp_ns, &msg.header.stamp);
    msg.header.frame_id = (char*)(frame_id ? frame_id : "");
    msg.wrench.force.x = fx; msg.wrench.force.y = fy; msg.wrench.force.z = fz;
    msg.wrench.torque.x = tx; msg.wrench.torque.y = ty; msg.wrench.torque.z = tz;
    return dds_write(slot->entity, &msg);
}

// Low-Level std_msgs Taking
#define IMPLEMENT_TAKE_PRIMITIVE(FN_NAME, TYPE_ENUM, STRUCT_TYPE, VAL_TYPE) \
CDDS_EXPORT int FN_NAME(int sub_handle, VAL_TYPE* out_value) { \
    if (!out_value) return -1; \
    EntitySlot* slot = find_slot(sub_handle); \
    if (!slot || slot->is_writer) return -1; \
    void* samples[1] = { NULL }; \
    dds_sample_info_t infos[1]; \
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1); \
    if (rc > 0) { \
        int res = 0; \
        if (infos[0].valid_data && samples[0] != NULL) { \
            struct STRUCT_TYPE* msg = (struct STRUCT_TYPE*)samples[0]; \
            *out_value = (VAL_TYPE)msg->data; \
            res = 1; \
        } \
        dds_return_loan(slot->entity, samples, rc); \
        return res; \
    } \
    return 0; \
}

IMPLEMENT_TAKE_PRIMITIVE(cdds_take_bool, DDS_TYPE_STD_MSGS_BOOL, std_msgs_msg_dds__Bool_, bool)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_byte, DDS_TYPE_STD_MSGS_BYTE, std_msgs_msg_dds__Byte_, uint8_t)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_char, DDS_TYPE_STD_MSGS_CHAR, std_msgs_msg_dds__Char_, uint8_t)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_float, DDS_TYPE_STD_MSGS_FLOAT32, std_msgs_msg_dds__Float32_, float)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_double, DDS_TYPE_STD_MSGS_FLOAT64, std_msgs_msg_dds__Float64_, double)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_int8, DDS_TYPE_STD_MSGS_INT8, std_msgs_msg_dds__Int8_, int8_t)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_int16, DDS_TYPE_STD_MSGS_INT16, std_msgs_msg_dds__Int16_, int16_t)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_int32, DDS_TYPE_STD_MSGS_INT32, std_msgs_msg_dds__Int32_, int32_t)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_int64, DDS_TYPE_STD_MSGS_INT64, std_msgs_msg_dds__Int64_, int64_t)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_uint8, DDS_TYPE_STD_MSGS_UINT8, std_msgs_msg_dds__UInt8_, uint8_t)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_uint16, DDS_TYPE_STD_MSGS_UINT16, std_msgs_msg_dds__UInt16_, uint16_t)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_uint32, DDS_TYPE_STD_MSGS_UINT32, std_msgs_msg_dds__UInt32_, uint32_t)
IMPLEMENT_TAKE_PRIMITIVE(cdds_take_uint64, DDS_TYPE_STD_MSGS_UINT64, std_msgs_msg_dds__UInt64_, uint64_t)

CDDS_EXPORT int cdds_take_color_rgba(int sub_handle, float* out_rgba) {
    if (!out_rgba) return -1;
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct std_msgs_msg_dds__ColorRGBA_* msg = (struct std_msgs_msg_dds__ColorRGBA_*)samples[0];
            out_rgba[0] = msg->r; out_rgba[1] = msg->g; out_rgba[2] = msg->b; out_rgba[3] = msg->a;
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_string(int sub_handle, char* out_buf, int max_len) {
    if (!out_buf || max_len <= 0) return -1;
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct std_msgs_msg_dds__String_* msg = (struct std_msgs_msg_dds__String_*)samples[0];
            if (msg->data) {
                strncpy(out_buf, msg->data, (size_t)(max_len - 1));
                out_buf[max_len - 1] = '\0';
            } else {
                out_buf[0] = '\0';
            }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

// Low-Level geometry_msgs Taking
CDDS_EXPORT int cdds_take_point(int sub_handle, double* out_xyz) {
    if (!out_xyz) return -1;
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__Point_* msg = (struct geometry_msgs_msg_dds__Point_*)samples[0];
            out_xyz[0] = msg->x; out_xyz[1] = msg->y; out_xyz[2] = msg->z;
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_point32(int sub_handle, float* out_xyz) {
    if (!out_xyz) return -1;
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__Point32_* msg = (struct geometry_msgs_msg_dds__Point32_*)samples[0];
            out_xyz[0] = msg->x; out_xyz[1] = msg->y; out_xyz[2] = msg->z;
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_point_stamped(int sub_handle, int64_t* out_timestamp_ns, char* out_frame_id, int max_frame_len, double* out_xyz) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__PointStamped_* msg = (struct geometry_msgs_msg_dds__PointStamped_*)samples[0];
            if (out_timestamp_ns) *out_timestamp_ns = (int64_t)msg->header.stamp.sec * 1000000000LL + msg->header.stamp.nanosec;
            if (out_frame_id && max_frame_len > 0) {
                strncpy(out_frame_id, msg->header.frame_id ? msg->header.frame_id : "", (size_t)(max_frame_len - 1));
                out_frame_id[max_frame_len - 1] = '\0';
            }
            if (out_xyz) { out_xyz[0] = msg->point.x; out_xyz[1] = msg->point.y; out_xyz[2] = msg->point.z; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_vector3(int sub_handle, double* out_xyz) {
    if (!out_xyz) return -1;
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__Vector3_* msg = (struct geometry_msgs_msg_dds__Vector3_*)samples[0];
            out_xyz[0] = msg->x; out_xyz[1] = msg->y; out_xyz[2] = msg->z;
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_vector3_stamped(int sub_handle, int64_t* out_timestamp_ns, char* out_frame_id, int max_frame_len, double* out_xyz) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__Vector3Stamped_* msg = (struct geometry_msgs_msg_dds__Vector3Stamped_*)samples[0];
            if (out_timestamp_ns) *out_timestamp_ns = (int64_t)msg->header.stamp.sec * 1000000000LL + msg->header.stamp.nanosec;
            if (out_frame_id && max_frame_len > 0) {
                strncpy(out_frame_id, msg->header.frame_id ? msg->header.frame_id : "", (size_t)(max_frame_len - 1));
                out_frame_id[max_frame_len - 1] = '\0';
            }
            if (out_xyz) { out_xyz[0] = msg->vector.x; out_xyz[1] = msg->vector.y; out_xyz[2] = msg->vector.z; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_quaternion(int sub_handle, double* out_xyzw) {
    if (!out_xyzw) return -1;
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__Quaternion_* msg = (struct geometry_msgs_msg_dds__Quaternion_*)samples[0];
            out_xyzw[0] = msg->x; out_xyzw[1] = msg->y; out_xyzw[2] = msg->z; out_xyzw[3] = msg->w;
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_quaternion_stamped(int sub_handle, int64_t* out_timestamp_ns, char* out_frame_id, int max_frame_len, double* out_xyzw) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__QuaternionStamped_* msg = (struct geometry_msgs_msg_dds__QuaternionStamped_*)samples[0];
            if (out_timestamp_ns) *out_timestamp_ns = (int64_t)msg->header.stamp.sec * 1000000000LL + msg->header.stamp.nanosec;
            if (out_frame_id && max_frame_len > 0) {
                strncpy(out_frame_id, msg->header.frame_id ? msg->header.frame_id : "", (size_t)(max_frame_len - 1));
                out_frame_id[max_frame_len - 1] = '\0';
            }
            if (out_xyzw) { out_xyzw[0] = msg->quaternion.x; out_xyzw[1] = msg->quaternion.y; out_xyzw[2] = msg->quaternion.z; out_xyzw[3] = msg->quaternion.w; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_pose(int sub_handle, double* out_pos_xyz, double* out_rot_xyzw) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__Pose_* msg = (struct geometry_msgs_msg_dds__Pose_*)samples[0];
            if (out_pos_xyz) { out_pos_xyz[0] = msg->position.x; out_pos_xyz[1] = msg->position.y; out_pos_xyz[2] = msg->position.z; }
            if (out_rot_xyzw) { out_rot_xyzw[0] = msg->orientation.x; out_rot_xyzw[1] = msg->orientation.y; out_rot_xyzw[2] = msg->orientation.z; out_rot_xyzw[3] = msg->orientation.w; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_pose2d(int sub_handle, double* out_x, double* out_y, double* out_theta) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__Pose2D_* msg = (struct geometry_msgs_msg_dds__Pose2D_*)samples[0];
            if (out_x) *out_x = msg->x;
            if (out_y) *out_y = msg->y;
            if (out_theta) *out_theta = msg->theta;
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_pose_stamped(
    int sub_handle, int64_t* out_timestamp_ns,
    char* out_frame_id, int max_frame_len,
    double* out_pos_xyz, double* out_rot_xyzw
) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__PoseStamped_* msg = (struct geometry_msgs_msg_dds__PoseStamped_*)samples[0];
            if (out_timestamp_ns) *out_timestamp_ns = (int64_t)msg->header.stamp.sec * 1000000000LL + msg->header.stamp.nanosec;
            if (out_frame_id && max_frame_len > 0) {
                strncpy(out_frame_id, msg->header.frame_id ? msg->header.frame_id : "", (size_t)(max_frame_len - 1));
                out_frame_id[max_frame_len - 1] = '\0';
            }
            if (out_pos_xyz) { out_pos_xyz[0] = msg->pose.position.x; out_pos_xyz[1] = msg->pose.position.y; out_pos_xyz[2] = msg->pose.position.z; }
            if (out_rot_xyzw) { out_rot_xyzw[0] = msg->pose.orientation.x; out_rot_xyzw[1] = msg->pose.orientation.y; out_rot_xyzw[2] = msg->pose.orientation.z; out_rot_xyzw[3] = msg->pose.orientation.w; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_transform(int sub_handle, double* out_trans_xyz, double* out_rot_xyzw) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__Transform_* msg = (struct geometry_msgs_msg_dds__Transform_*)samples[0];
            if (out_trans_xyz) { out_trans_xyz[0] = msg->translation.x; out_trans_xyz[1] = msg->translation.y; out_trans_xyz[2] = msg->translation.z; }
            if (out_rot_xyzw) { out_rot_xyzw[0] = msg->rotation.x; out_rot_xyzw[1] = msg->rotation.y; out_rot_xyzw[2] = msg->rotation.z; out_rot_xyzw[3] = msg->rotation.w; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_transform_stamped(
    int sub_handle, int64_t* out_timestamp_ns,
    char* out_frame_id, int max_frame_len,
    char* out_child_frame_id, int max_child_len,
    double* out_trans_xyz, double* out_rot_xyzw
) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__TransformStamped_* msg = (struct geometry_msgs_msg_dds__TransformStamped_*)samples[0];
            if (out_timestamp_ns) *out_timestamp_ns = (int64_t)msg->header.stamp.sec * 1000000000LL + msg->header.stamp.nanosec;
            if (out_frame_id && max_frame_len > 0) {
                strncpy(out_frame_id, msg->header.frame_id ? msg->header.frame_id : "", (size_t)(max_frame_len - 1));
                out_frame_id[max_frame_len - 1] = '\0';
            }
            if (out_child_frame_id && max_child_len > 0) {
                strncpy(out_child_frame_id, msg->child_frame_id ? msg->child_frame_id : "", (size_t)(max_child_len - 1));
                out_child_frame_id[max_child_len - 1] = '\0';
            }
            if (out_trans_xyz) { out_trans_xyz[0] = msg->transform.translation.x; out_trans_xyz[1] = msg->transform.translation.y; out_trans_xyz[2] = msg->transform.translation.z; }
            if (out_rot_xyzw) { out_rot_xyzw[0] = msg->transform.rotation.x; out_rot_xyzw[1] = msg->transform.rotation.y; out_rot_xyzw[2] = msg->transform.rotation.z; out_rot_xyzw[3] = msg->transform.rotation.w; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_twist(int sub_handle, double* out_linear_xyz, double* out_angular_xyz) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__Twist_* msg = (struct geometry_msgs_msg_dds__Twist_*)samples[0];
            if (out_linear_xyz) { out_linear_xyz[0] = msg->linear.x; out_linear_xyz[1] = msg->linear.y; out_linear_xyz[2] = msg->linear.z; }
            if (out_angular_xyz) { out_angular_xyz[0] = msg->angular.x; out_angular_xyz[1] = msg->angular.y; out_angular_xyz[2] = msg->angular.z; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_twist_stamped(
    int sub_handle, int64_t* out_timestamp_ns,
    char* out_frame_id, int max_frame_len,
    double* out_linear_xyz, double* out_angular_xyz
) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__TwistStamped_* msg = (struct geometry_msgs_msg_dds__TwistStamped_*)samples[0];
            if (out_timestamp_ns) *out_timestamp_ns = (int64_t)msg->header.stamp.sec * 1000000000LL + msg->header.stamp.nanosec;
            if (out_frame_id && max_frame_len > 0) {
                strncpy(out_frame_id, msg->header.frame_id ? msg->header.frame_id : "", (size_t)(max_frame_len - 1));
                out_frame_id[max_frame_len - 1] = '\0';
            }
            if (out_linear_xyz) { out_linear_xyz[0] = msg->twist.linear.x; out_linear_xyz[1] = msg->twist.linear.y; out_linear_xyz[2] = msg->twist.linear.z; }
            if (out_angular_xyz) { out_angular_xyz[0] = msg->twist.angular.x; out_angular_xyz[1] = msg->twist.angular.y; out_angular_xyz[2] = msg->twist.angular.z; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_accel(int sub_handle, double* out_linear_xyz, double* out_angular_xyz) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__Accel_* msg = (struct geometry_msgs_msg_dds__Accel_*)samples[0];
            if (out_linear_xyz) { out_linear_xyz[0] = msg->linear.x; out_linear_xyz[1] = msg->linear.y; out_linear_xyz[2] = msg->linear.z; }
            if (out_angular_xyz) { out_angular_xyz[0] = msg->angular.x; out_angular_xyz[1] = msg->angular.y; out_angular_xyz[2] = msg->angular.z; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_accel_stamped(
    int sub_handle, int64_t* out_timestamp_ns,
    char* out_frame_id, int max_frame_len,
    double* out_linear_xyz, double* out_angular_xyz
) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__AccelStamped_* msg = (struct geometry_msgs_msg_dds__AccelStamped_*)samples[0];
            if (out_timestamp_ns) *out_timestamp_ns = (int64_t)msg->header.stamp.sec * 1000000000LL + msg->header.stamp.nanosec;
            if (out_frame_id && max_frame_len > 0) {
                strncpy(out_frame_id, msg->header.frame_id ? msg->header.frame_id : "", (size_t)(max_frame_len - 1));
                out_frame_id[max_frame_len - 1] = '\0';
            }
            if (out_linear_xyz) { out_linear_xyz[0] = msg->accel.linear.x; out_linear_xyz[1] = msg->accel.linear.y; out_linear_xyz[2] = msg->accel.linear.z; }
            if (out_angular_xyz) { out_angular_xyz[0] = msg->accel.angular.x; out_angular_xyz[1] = msg->accel.angular.y; out_angular_xyz[2] = msg->accel.angular.z; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_wrench(int sub_handle, double* out_force_xyz, double* out_torque_xyz) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__Wrench_* msg = (struct geometry_msgs_msg_dds__Wrench_*)samples[0];
            if (out_force_xyz) { out_force_xyz[0] = msg->force.x; out_force_xyz[1] = msg->force.y; out_force_xyz[2] = msg->force.z; }
            if (out_torque_xyz) { out_torque_xyz[0] = msg->torque.x; out_torque_xyz[1] = msg->torque.y; out_torque_xyz[2] = msg->torque.z; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

CDDS_EXPORT int cdds_take_wrench_stamped(
    int sub_handle, int64_t* out_timestamp_ns,
    char* out_frame_id, int max_frame_len,
    double* out_force_xyz, double* out_torque_xyz
) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer) return -1;
    void* samples[1] = { NULL };
    dds_sample_info_t infos[1];
    dds_return_t rc = dds_take(slot->entity, samples, infos, 1, 1);
    if (rc > 0) {
        int res = 0;
        if (infos[0].valid_data && samples[0] != NULL) {
            struct geometry_msgs_msg_dds__WrenchStamped_* msg = (struct geometry_msgs_msg_dds__WrenchStamped_*)samples[0];
            if (out_timestamp_ns) *out_timestamp_ns = (int64_t)msg->header.stamp.sec * 1000000000LL + msg->header.stamp.nanosec;
            if (out_frame_id && max_frame_len > 0) {
                strncpy(out_frame_id, msg->header.frame_id ? msg->header.frame_id : "", (size_t)(max_frame_len - 1));
                out_frame_id[max_frame_len - 1] = '\0';
            }
            if (out_force_xyz) { out_force_xyz[0] = msg->wrench.force.x; out_force_xyz[1] = msg->wrench.force.y; out_force_xyz[2] = msg->wrench.force.z; }
            if (out_torque_xyz) { out_torque_xyz[0] = msg->wrench.torque.x; out_torque_xyz[1] = msg->wrench.torque.y; out_torque_xyz[2] = msg->wrench.torque.z; }
            res = 1;
        }
        dds_return_loan(slot->entity, samples, rc);
        return res;
    }
    return 0;
}

// Discovery
CDDS_EXPORT int cdds_get_matched_subscriptions(int pub_handle) {
    EntitySlot* slot = find_slot(pub_handle);
    if (!slot || !slot->is_writer || slot->entity <= 0) return 0;
    dds_publication_matched_status_t status;
    dds_return_t rc = dds_get_publication_matched_status(slot->entity, &status);
    return (rc == 0) ? (int)status.current_count : 0;
}

CDDS_EXPORT int cdds_get_matched_publications(int sub_handle) {
    EntitySlot* slot = find_slot(sub_handle);
    if (!slot || slot->is_writer || slot->entity <= 0) return 0;
    dds_subscription_matched_status_t status;
    dds_return_t rc = dds_get_subscription_matched_status(slot->entity, &status);
    return (rc == 0) ? (int)status.current_count : 0;
}
