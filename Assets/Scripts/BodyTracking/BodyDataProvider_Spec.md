# IBodyDataProvider Specification

This document defines the standard specification for body tracking providers in the `Apex.BodyTracking` namespace. All motion capture devices (e.g., Pico Trackers, VRIK, Manus) must implement this interface to connect to the `BodyTrackingCoordinateSystem`.

## Core

| Method / Property | Lifecycle & Timing | Developer Responsibilities |
|---|---|---|
| `IsTrackingDataValid` | Polled every frame by Coordinator | Return `false` if tracking is unavailable or dropping frames. The Coordinator uses this to auto-hide/show the skeleton. |
| `HasJointVelocities` | Polled as needed | Return `true` if linear and angular joint velocities are supported, enabled, and currently valid. |
| `HasJointAccelerations` | Polled as needed | Return `true` if linear and angular joint accelerations are supported, enabled, and currently valid. |
| `StartProvider()` | Called when the mode switches to this Provider | Initialize the SDK and start event listeners. **Do NOT** auto-calibrate here; only prepare for data acquisition. |
| `StopProvider()` | Called when switching away from this mode or on destroy | Stop the SDK, unregister events, and release memory/resources. |
| `TriggerCalibration()`| Called via user UI or calibration hotkey | Execute the device-specific calibration logic (e.g., VRIK height measurement, Pico T-Pose calibration). |
| `UpdateSkeleton(...)` | Called every frame in `LateUpdate` | The core data entry point. Retrieve the underlying tracking data and write it directly to `factory.generatedJoints`. |
| `TryGetJointVelocity(...)` | Called on demand per joint index (0..23) | Retrieve `Vector3 linearVelocity` ($m/s$) and `Vector3 angularVelocity` ($rad/s$) for the requested joint index. |
| `TryGetJointAcceleration(...)` | Called on demand per joint index (0..23) | Retrieve `Vector3 linearAcceleration` ($m/s^2$) and `Vector3 angularAcceleration` ($rad/s^2$) for the requested joint index. |

## How to Write a New Provider

When integrating a new motion capture suit, follow these steps:

1. Create a new script inheriting from `MonoBehaviour` and implementing `IBodyDataProvider`.
2. Because the Coordinator actively calls `UpdateSkeleton()` to pull data, you **must avoid** using Unity's `Update/LateUpdate` to modify bones inside your own script. This prevents execution order conflicts and IK jitter.
3. In the `UpdateSkeleton` method, convert your SDK data to Unity's coordinate system and assign them one by one to `factory.generatedJoints[i].position` and `rotation`.
4. In the Unity Inspector, go to `BodyTrackingCoordinateSystem` -> `Providers` list, increase the size, and drag your new script in. Finally, trigger `EnableProvider(your_index)` via UI events.

## Data Topology & Skeleton Node Specification

### Node Topology Diagram

Below is the standard 24-bone layout map required by the system:

```
                              [15] Head
                                  |
                                  |
                              [12] Neck
                                  |
                                  |
            [13] L_Collar --- [9] Spine3 --- [14] R_Collar
                  |               |                |
                  |               |                |
     [16] L_Shoulder          [6] Spine2       [17] R_Shoulder
              |                   |                |
              |                   |                |
         [18] L_Elbow         [3] Spine1      [19] R_Elbow
              |                   |                |
              |                   |                |
         [20] L_Wrist         [0] Pelvis      [21] R_Wrist
              |                /      \            |
              |               /        \           |
         [22] L_Hand      [1] L_Hip  [2] R_Hip [23] R_Hand
                              |          |
                              |          |
                         [4] L_Knee  [5] R_Knee
                              |          |
                              |          |
                        [7] L_Ankle  [8] R_Ankle
                              |          |
                              |          |
                         [10] L_Foot [11] R_Foot
```

### Frequently Asked Questions

* **Do the nodes have a parent-child hierarchy?**
  **NO.** The 24 `generatedJoints` are a flat array of GameObjects. They are all siblings under the `BodyTrackingSkeletonFactory`. They do not inherit transforms from one another.
* **Are they World Poses or Local Poses?**
  They are **World Poses** in Unity space. You must assign absolute world `position` and world `rotation` to each joint.
* **Are all nodes required?**
  **Yes.** You must provide valid data for all 24 nodes. If a joint is not tracked by your hardware, you should estimate it or hold its last known pose.
* **What happens if the input data is wrong?**
  If incorrect poses are passed, the visual skeleton and IK will twist unnaturally. If `NaN` or `Infinity` is passed, it can cause the UDP Publisher to send corrupted packets, which may cause catastrophic failures on the remote robot side.

