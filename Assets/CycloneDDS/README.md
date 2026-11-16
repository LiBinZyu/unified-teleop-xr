# Cyclone DDS for Unity

Native DDS publishing and subscribing in Unity through the Eclipse Cyclone DDS C API. Supports Windows x64 (Editor and standalone) and Android ARM64.

## Quick start

1. Add [TransformPosePublisher](Samples/TransformPosePublisher.cs) to any GameObject in an empty scene.
2. Set **Peer Ip** to the subscriber's IP address and match its DDS domain ID. The default domain is `0`.
3. Enter Play Mode and move the object. The sample publishes its world pose as `geometry_msgs/msg/PoseStamped` on DDS topic `rt/object/pose`, using frame `world` at up to 30 Hz.

To view the pose in ROS 2:

```sh
ros2 topic echo /object/pose
```

Run only one DDS session per Unity process. Disabling the sample component releases its session.

## Runtime API

| File | Purpose |
| --- | --- |
| [CddsNative.cs](Runtime/CddsNative.cs) | Start and stop DDS, create writers and readers, and publish or receive messages. |
| [DdsConfig.cs](Runtime/DdsConfig.cs) | Build Cyclone DDS XML, configure logging, and create timestamps and headers. |
| [UnityRosCoordinates.cs](Runtime/UnityRosCoordinates.cs) | Convert positions and rotations between Unity and ROS, or build geometry message structs. |
| [DdsRawSample.cs](Runtime/DdsRawSample.cs) | Receive raw samples using a reusable buffer that you own. |

## Array messages and tests

`cdds_publish_float32_multi_array` publishes a ROS 2 compatible `Float32MultiArray` with an empty dimension list and zero data offset. Receivers using the old data-only IDL must update. When using `DdsRawSample`, match the reader's message type and copy any nested data you need before the next `Take` or `Dispose`.

Run **Tools > CycloneDDS > Test Array Interop** in Unity with other DDS sessions stopped. The corresponding native CTest is `arrays_and_raw_ownership`.
