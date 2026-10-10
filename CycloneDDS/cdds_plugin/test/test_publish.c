#include "cdds_plugin.h"
#include <stdio.h>
#include <stdlib.h>

#if defined(_WIN32)
#include <windows.h>
#define sleep_ms(x) Sleep(x)
#else
#include <unistd.h>
#define sleep_ms(x) usleep((x) * 1000)
#endif

int main(int argc, char* argv[]) {
    const char* peer_ip = "192.168.8.181";
    if (argc > 1) {
        peer_ip = argv[1];
    }

    printf("=== cdds_plugin Standalone Test ===\n");
    printf("Target Peer IP: %s\n", peer_ip);

    int rc = cdds_init(0, peer_ip);
    if (rc != 0) {
        printf("Failed to init cdds: %d\n", rc);
        return 1;
    }
    printf("DDS Initialized successfully.\n");

    // Pure DDS topic creation: if receiver is ROS 2, pass "rt/xr/..."
    int pub_head_pose = cdds_create_publisher("rt/xr/head/pose", DDS_TYPE_GEOMETRY_MSGS_POSE_STAMPED, DDS_QOS_SENSOR_DATA);
    int pub_l_trigger = cdds_create_publisher("rt/xr/l_hand/trigger", DDS_TYPE_STD_MSGS_FLOAT32, DDS_QOS_SENSOR_DATA);
    int pub_focus = cdds_create_publisher("rt/xr/focus", DDS_TYPE_STD_MSGS_BOOL, DDS_QOS_STATE_TRANSIENT);

    printf("Publishing test samples for 5 seconds...\n");
    for (int i = 0; i < 5; i++) {
        double px = 0.1 * i;
        double py = 0.2 * i;
        double pz = 1.6;
        float trigger = 0.1f * i;

        int matched = cdds_get_matched_subscriptions(pub_head_pose);
        printf("[%d/5] Sending pose (%.2f, %.2f, %.2f), trigger=%.2f, matched_subscribers=%d\n",
               i + 1, px, py, pz, trigger, matched);

        cdds_publish_pose_stamped(pub_head_pose, 0, "xr_base", px, py, pz, 0.0, 0.0, 0.0, 1.0);
        cdds_publish_float(pub_l_trigger, trigger);
        cdds_publish_bool(pub_focus, true);

        sleep_ms(1000);
    }

    printf("Shutting down cdds...\n");
    cdds_shutdown();
    printf("Test finished successfully.\n");
    return 0;
}
