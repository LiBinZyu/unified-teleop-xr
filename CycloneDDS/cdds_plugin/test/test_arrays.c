#include "cdds_plugin.h"
#include "dds/dds.h"
#include "std_msgs/msg/dds_/Float32MultiArray_.h"
#include "sensor_msgs/msg/dds_/JointState_.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int checks;
#define CHECK(expr) do { ++checks; if (!(expr)) { \
    fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #expr); exit(1); } } while (0)

static void wait_matched(int writer) {
    for (int i = 0; i < 500; ++i) {
        if (cdds_get_matched_subscriptions(writer) > 0) return;
        dds_sleepfor(DDS_MSECS(10));
    }
    CHECK(!"Discovery timed out");
}

static void take(int reader, void* sample, size_t size) {
    for (int i = 0; i < 500; ++i) {
        int rc = cdds_take_raw(reader, sample, size);
        CHECK(rc >= 0);
        if (rc == 1) return;
        dds_sleepfor(DDS_MSECS(10));
    }
    CHECK(!"Receive timed out");
}

int main(void) {
    CHECK(cdds_sample_size(DDS_TYPE_UNKNOWN) == 0);
    CHECK(cdds_sample_alloc(DDS_TYPE_UNKNOWN) == NULL);
    CHECK(cdds_sample_size(DDS_TYPE_STD_MSGS_FLOAT32_MULTI_ARRAY) == sizeof(std_msgs_msg_dds__Float32MultiArray_));
    CHECK(cdds_sample_size(DDS_TYPE_SENSOR_MSGS_JOINT_STATE) == sizeof(sensor_msgs_msg_dds__JointState_));

    const char* config = "<CycloneDDS><Domain><General><Interfaces><NetworkInterface address=\"127.0.0.1\"/></Interfaces><AllowMulticast>false</AllowMulticast></General></Domain></CycloneDDS>";
    CHECK(cdds_init(197, config) == 0);
    int writer = cdds_create_publisher("array_ownership_test", DDS_TYPE_STD_MSGS_FLOAT32_MULTI_ARRAY, DDS_QOS_DEFAULT);
    int reader = cdds_create_subscriber("array_ownership_test", DDS_TYPE_STD_MSGS_FLOAT32_MULTI_ARRAY, DDS_QOS_DEFAULT);
    CHECK(writer > 0 && reader > 0);
    wait_matched(writer);
    std_msgs_msg_dds__Float32MultiArray_* sample = cdds_sample_alloc(DDS_TYPE_STD_MSGS_FLOAT32_MULTI_ARRAY);
    CHECK(sample != NULL);
    CHECK(cdds_take_raw(reader, sample, sizeof(*sample)) == 0);
    CHECK(cdds_take_raw(reader, sample, sizeof(*sample) - 1) == DDS_RETCODE_BAD_PARAMETER);
    CHECK(cdds_take_raw(writer, sample, sizeof(*sample)) == DDS_RETCODE_BAD_PARAMETER);
    CHECK(cdds_take_raw(-1, sample, sizeof(*sample)) == DDS_RETCODE_BAD_PARAMETER);
    CHECK(cdds_take_raw(reader, NULL, sizeof(*sample)) == DDS_RETCODE_BAD_PARAMETER);
    CHECK(cdds_publish_float32_multi_array(writer, NULL, 1) == DDS_RETCODE_BAD_PARAMETER);
    CHECK(cdds_publish_float32_multi_array(reader, NULL, 0) == DDS_RETCODE_BAD_PARAMETER);

    float values[256];
    for (int i = 0; i < 256; ++i) values[i] = (float)i + 0.25f;
    // Exercise caller-owned buffers growing and shrinking across repeated takes.
    for (uint32_t n = 0; n < 64; ++n) {
        uint32_t count = (n * 37) % 257;
        CHECK(cdds_publish_float32_multi_array(writer, count ? values : NULL, count) == 0);
        take(reader, sample, sizeof(*sample));
        CHECK(sample->layout.dim._length == 0 && sample->layout.data_offset == 0);
        CHECK(sample->data._length == count);
        for (uint32_t j = 0; j < count; ++j) CHECK(sample->data._buffer[j] == values[j]);
    }

    std_msgs_msg_dds__MultiArrayDimension_ dims[2] = {{"hand", 2, 8}, {"joint", 4, 4}};
    std_msgs_msg_dds__Float32MultiArray_ full = {0};
    full.layout.dim._maximum = full.layout.dim._length = 2;
    full.layout.dim._buffer = dims;
    full.layout.data_offset = 1;
    full.data._maximum = full.data._length = 8;
    full.data._buffer = values;
    CHECK(cdds_publish_raw(writer, &full) == 0);
    take(reader, sample, sizeof(*sample));
    CHECK(sample->layout.dim._length == 2 && sample->layout.data_offset == 1);
    CHECK(strcmp(sample->layout.dim._buffer[0].label, "hand") == 0);
    CHECK(sample->layout.dim._buffer[1].size == 4 && sample->layout.dim._buffer[1].stride == 4);
    CHECK(sample->data._length == 8 && sample->data._buffer[7] == 7.25f);

    // A different sample/read must not overwrite the previous sample's nested storage.
    std_msgs_msg_dds__Float32MultiArray_* second = cdds_sample_alloc(DDS_TYPE_STD_MSGS_FLOAT32_MULTI_ARRAY);
    CHECK(second != NULL);
    CHECK(cdds_publish_float32_multi_array(writer, values, 256) == 0);
    take(reader, second, sizeof(*second));
    CHECK(sample->data._length == 8 && sample->data._buffer[7] == 7.25f);
    CHECK(strcmp(sample->layout.dim._buffer[0].label, "hand") == 0);
    cdds_sample_free(DDS_TYPE_STD_MSGS_FLOAT32_MULTI_ARRAY, second);

    int jw = cdds_create_publisher("joint_ownership_test", DDS_TYPE_SENSOR_MSGS_JOINT_STATE, DDS_QOS_DEFAULT);
    int jr = cdds_create_subscriber("joint_ownership_test", DDS_TYPE_SENSOR_MSGS_JOINT_STATE, DDS_QOS_DEFAULT);
    CHECK(jw > 0 && jr > 0);
    wait_matched(jw);
    sensor_msgs_msg_dds__JointState_* joints = cdds_sample_alloc(DDS_TYPE_SENSOR_MSGS_JOINT_STATE);
    CHECK(joints != NULL);
    char* names[2] = {"wrist", "index_proximal"};
    double positions[2] = {1.25, -2.5};
    sensor_msgs_msg_dds__JointState_ joint_msg = {0};
    joint_msg.header.frame_id = "head";
    joint_msg.name._maximum = joint_msg.name._length = 2;
    joint_msg.name._buffer = names;
    joint_msg.position._maximum = joint_msg.position._length = 2;
    joint_msg.position._buffer = positions;
    for (int i = 0; i < 32; ++i) {
        joint_msg.header.frame_id = i % 2 ? "world_coordinate_frame" : "head";
        joint_msg.name._length = joint_msg.position._length = i % 2 ? 2 : 1;
        CHECK(cdds_publish_raw(jw, &joint_msg) == 0);
        take(jr, joints, sizeof(*joints));
        CHECK(strcmp(joints->header.frame_id, joint_msg.header.frame_id) == 0);
        CHECK(joints->name._length == joint_msg.name._length);
        CHECK(strcmp(joints->name._buffer[0], "wrist") == 0);
        CHECK(joints->position._buffer[0] == 1.25);
        if (joints->name._length == 2) {
            CHECK(strcmp(joints->name._buffer[1], "index_proximal") == 0);
            CHECK(joints->position._buffer[1] == -2.5);
        }
    }
    // Existing scalar API and wrong-type validation remain usable.
    int fw = cdds_create_publisher("scalar_regression", DDS_TYPE_STD_MSGS_FLOAT32, DDS_QOS_DEFAULT);
    int fr = cdds_create_subscriber("scalar_regression", DDS_TYPE_STD_MSGS_FLOAT32, DDS_QOS_DEFAULT);
    CHECK(fw > 0 && fr > 0);
    wait_matched(fw);
    CHECK(cdds_publish_float32_multi_array(fw, values, 1) == DDS_RETCODE_BAD_PARAMETER);
    CHECK(cdds_publish_float(fw, 4.5f) == 0);
    float scalar = 0;
    for (int i = 0; i < 500; ++i) {
        if (cdds_take_float(fr, &scalar) == 1) break;
        dds_sleepfor(DDS_MSECS(10));
    }
    CHECK(scalar == 4.5f);

    cdds_shutdown();
    // Nested data survives reader destruction and participant shutdown.
    CHECK(strcmp(sample->layout.dim._buffer[0].label, "hand") == 0);
    CHECK(sample->data._buffer[7] == 7.25f);
    CHECK(strcmp(joints->header.frame_id, "world_coordinate_frame") == 0);
    CHECK(strcmp(joints->name._buffer[1], "index_proximal") == 0);
    CHECK(joints->position._buffer[1] == -2.5);
    cdds_sample_free(DDS_TYPE_STD_MSGS_FLOAT32_MULTI_ARRAY, sample);
    cdds_sample_free(DDS_TYPE_SENSOR_MSGS_JOINT_STATE, joints);
    puts("PASS: arrays, nested strings, buffer reuse, post-shutdown ownership, scalar regression");
    printf("%d checks\n", checks);
    return 0;
}
