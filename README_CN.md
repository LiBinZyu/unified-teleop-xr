<div align="center">

<img src="./doc/UTXRtitle.svg" alt="Unified Teleop XR" width="345" />

**让你的 XR 头显成为机器人的空间运动输入设备**

通过 CycloneDDS 将 Meta Quest 与 PICO 的头显、控制器和身体追踪数据直接流式传输到 ROS 2 — 无需构建任何 Unity 应用程序。

<kbd>OpenXR</kbd> <kbd>CycloneDDS</kbd> <kbd>ROS 2</kbd>

<table align="center">
  <tr>
    <td><img src="./doc/UTXR.jpg" width="600"></td>
    <td><img src="./doc/UTXRSpatialVideo.gif" width="338"></td>
  </tr>
</table>

<p>
  <a href="#quick-start">快速开始 ↗</a> ·
  <a href="#architecture">工作原理 ↗</a> ·
  <a href="#data">数据接口 ↗</a> ·
  <a href="#gpu-reconstructed-spatial-video">空间视频 ↗</a> ·
  <a href="#supported-devices">支持设备 ↗</a> ·
  <a href="./README.md">English ↗</a>
</p>
</div>

> XR → CycloneDDS → ROS 2 通信链路已在基于 CycloneDDS 的 ROS 2 接收端验证。面向 Meta Quest 和 PICO 的稳定独立 APK 安装包正在计划发布中。UTXR (Unified Teleop XR) 是一个开源 Unity 工具包，使开发者无需繁琐配置即可将空间追踪数据直接流式传输到 ROS 2 系统。它基于 OpenXR 和 CycloneDDS 发布位姿、速度、加速度以及交互数据，已广泛用于机器人遥操作、机械臂灵巧抓取、远程协助、人机交互（HRI）研究、第一视角数据采集及深度学习工作流。

<a id="quick-start"></a>
## 快速开始

1. 从本仓库的 **Releases** 页面下载安装 **Meta Quest** 或 **PICO** 的 APK 安装包。
2. 在头显中启动 **Unified Teleop XR**。
3. 将头显和 ROS 2/DDS 接收端连接到同一个可达局域网。网络配置必须允许 DDS 发现与通信流量。
4. 发现并订阅 XR 话题。

在已安装 `rmw_cyclonedds_cpp` 的 ROS 2 系统上的示例命令：

```bash
export RMW_IMPLEMENTATION=rmw_cyclonedds_cpp

ros2 topic list
ros2 topic echo /xr/head/position/
```

**如果看不到话题：** 请确保头显和接收端连接在同一局域网（确认支持组播且未开启 AP 隔离），并确认 `rmw_cyclonedds_cpp` 已正确配置。

<a id="why-unified-teleop-xr"></a>
## 为什么选择 Unified Teleop XR？

机器人开发者不应该为了读取头显位姿、手柄或身体追踪数据，而专门去构建 Unity 工程。

| 你的目标需求 | UTXR 的设计实现 |
| --- | --- |
| 将 XR 追踪接入 ROS 2 | 从头显直接发布可订阅的 XR 话题数据流 |
| 跳过 XR 应用开发 | 接收端直接使用预编译的 Quest/PICO APK，无需构建 Unity 工程 |
| 免除额外的 ROS–TCP 桥接 | 在经过验证的环境中直接通过 CycloneDDS 发布通信 |
| 跨硬件复用机器人输入管线 | 在支持的不同设备间保持一致的话题结构（取决于可用传感器） |
| 更加从容地应对追踪异常 | 暴露追踪有效性标志，并提供可选的 Safeguard 安全后处理 |

无需 Unity 编辑器、ROS–TCP 桥接程序或自定义 ROS 2 消息包。接收端只需安装标准的 ROS 2 基础消息包：
- `builtin_interfaces`
- `geometry_msgs`
- `sensor_msgs`
- `std_msgs`

<a id="architecture"></a>
## 工作原理

同一网络中的机器人、ROS 2 节点、仿真器或原生 DDS 应用程序均可直接订阅发布的数据。

<img src="./doc/UnifiedTeleopXR.svg" style="display:block; margin:auto; width:100%; max-width:600px;">

<a id="data"></a>
<a id="data--topics"></a>
## 数据接口

### 原始输出

<details>
<summary><b>头部追踪</b></summary>
<pre>
/xr/head/is_tracked                         std_msgs/msg/Bool<br>/xr/head/position                           geometry_msgs/msg/PointStamped<br>/xr/head/rotation                           geometry_msgs/msg/QuaternionStamped
</pre>
</details>

<details>
<summary><b>左右手 / 控制器追踪</b></summary>
<pre>
/xr/left/is_tracked                         std_msgs/msg/Bool<br>/xr/left/position                           geometry_msgs/msg/PointStamped<br>/xr/left/rotation                           geometry_msgs/msg/QuaternionStamped<br>/xr/left/linear_velocity                    geometry_msgs/msg/Vector3Stamped<br>/xr/left/angular_velocity                   geometry_msgs/msg/Vector3Stamped<br>/xr/left/linear_acceleration                geometry_msgs/msg/Vector3Stamped<br>/xr/left/angular_acceleration               geometry_msgs/msg/Vector3Stamped<br>/xr/right/is_tracked                        std_msgs/msg/Bool<br>/xr/right/position                          geometry_msgs/msg/PointStamped<br>/xr/right/rotation                          geometry_msgs/msg/QuaternionStamped<br>/xr/right/linear_velocity                   geometry_msgs/msg/Vector3Stamped<br>/xr/right/angular_velocity                  geometry_msgs/msg/Vector3Stamped<br>/xr/right/linear_acceleration               geometry_msgs/msg/Vector3Stamped<br>/xr/right/angular_acceleration              geometry_msgs/msg/Vector3Stamped
</pre>
</details>

<details>
<summary><b>控制器按键与摇杆输入</b></summary>
<pre>
/xr/left/activate_value                     std_msgs/msg/Float32<br>/xr/left/select_value                       std_msgs/msg/Float32<br>/xr/left/thumbstick                         geometry_msgs/msg/Twist<br>/xr/left/thumbstick_click                   std_msgs/msg/Bool<br>/xr/left/primary_button                     std_msgs/msg/Bool<br>/xr/left/secondary_button                   std_msgs/msg/Bool<br>/xr/right/activate_value                    std_msgs/msg/Float32<br>/xr/right/select_value                      std_msgs/msg/Float32<br>/xr/right/thumbstick                        geometry_msgs/msg/Twist<br>/xr/right/thumbstick_click                  std_msgs/msg/Bool<br>/xr/right/primary_button                    std_msgs/msg/Bool<br>/xr/right/secondary_button                  std_msgs/msg/Bool
</pre>
</details>

<details>
<summary><b>手部关节追踪</b></summary>
<pre>
[待完善]
</pre>
</details>

<details>
<summary><b>身体关节</b></summary>
<pre>
/xr/chest/pose                              geometry_msgs/msg/PoseStamped<br>/xr/left_shoulder/pose                      geometry_msgs/msg/PoseStamped<br>/xr/right_shoulder/pose                     geometry_msgs/msg/PoseStamped<br>/xr/left_elbow/pose                         geometry_msgs/msg/PoseStamped<br>/xr/right_elbow/pose                        geometry_msgs/msg/PoseStamped<br>/xr/hip/pose                                geometry_msgs/msg/PoseStamped<br>/xr/left_knee/pose                          geometry_msgs/msg/PoseStamped<br>/xr/right_knee/pose                         geometry_msgs/msg/PoseStamped<br>/xr/left_ankle/pose                         geometry_msgs/msg/PoseStamped<br>/xr/right_ankle/pose                        geometry_msgs/msg/PoseStamped<br>/xr/left_foot/pose                          geometry_msgs/msg/PoseStamped<br>/xr/right_foot/pose                         geometry_msgs/msg/PoseStamped
</pre>
</details>

<details>
<summary><b>眼动追踪</b></summary>
<pre>
/xr/eyes/is_tracked                         std_msgs/msg/Bool<br>/xr/eyes/gaze_position                      geometry_msgs/msg/PointStamped<br>/xr/eyes/gaze_rotation                      geometry_msgs/msg/QuaternionStamped
</pre>
</details>

<details>
<summary><b>交互数据</b></summary>
<pre>
/xr/left/aim_position                       geometry_msgs/msg/PointStamped<br>/xr/left/aim_rotation                       geometry_msgs/msg/QuaternionStamped<br>/xr/right/aim_position                      geometry_msgs/msg/PointStamped<br>/xr/right/aim_rotation                      geometry_msgs/msg/QuaternionStamped<br>/xr/left/select                             std_msgs/msg/Bool<br>/xr/right/select                            std_msgs/msg/Bool<br>/xr/left/select_value                       std_msgs/msg/Float32<br>/xr/right/select_value                      std_msgs/msg/Float32<br>/xr/left/activate                           std_msgs/msg/Bool<br>/xr/right/activate                          std_msgs/msg/Bool<br>/xr/left/activate_value                     std_msgs/msg/Float32<br>/xr/right/activate_value                    std_msgs/msg/Float32<br>/xr/left/ui_press                           std_msgs/msg/Bool<br>/xr/right/ui_press                          std_msgs/msg/Bool<br>/xr/left/ui_scroll                          geometry_msgs/msg/Vector3<br>/xr/right/ui_scroll                         geometry_msgs/msg/Vector3
</pre>
</details>

<div style="display:flex; gap:10px;">
  <img src="./doc/rqt1.jpeg" width="48%" style="border-radius: 12px; border: 1px solid #E4E4E8;">
  <img src="./doc/rqt2.jpeg" width="48%" style="border-radius: 12px; border: 1px solid #E4E4E8;">
</div>

### Safeguard 安全模式

Safeguard 模式会对原始 OpenXR 输入数据进行安全后处理，主要作用于左右控制器/手部姿态。用于减少以下情况下的不安全或无效输出：

- 操作过程中控制器或手部追踪丢失。
- 控制器掉落到地面。
- 手部/控制器运动超过设定的人体运动限制。
- 控制器被静止放置在某处，不再被判定为手持状态。

<a id="gpu-reconstructed-spatial-video"></a>
<a id="video"></a>
## GPU 重建空间视频

iPhone Pro 的 RGB-D 数据流可通过 Wi-Fi 传输，并在 XR 中重建为空间视频，以点云或三维网格（Mesh）形式进行可视化渲染。重建计算完全在 GPU Compute Shader 中运行。

将 iPhone Pro 放置在远程端，可共享具有真实空间尺度感的环境视角，保留相机与周围物体之间的距离感。这能显著提升遥操作过程中的空间环境感知能力。

该输入源依赖 Marek Simonik 开发的 [Record3D App](https://record3d.app/)。Wi-Fi 传输是该 iOS 应用中的付费功能。要在 UTXR 中使用此功能，需要在苹果应用商店（[App Store](https://apps.apple.com/us/app/record3d-3d-videos/id1477716895?ls=1)）购买该功能。

<div align="center">
  <img src="./doc/UTXRSpatialVideo.gif" style="width:100%; max-width:600px; border-radius: 16px; border: 1px solid #E4E4E8;">
</div>

<a id="supported-devices"></a>
<a id="devices--compatibility"></a>
## 支持设备

- Meta Quest 3
- Meta Quest 3S
- Meta Quest Pro
- Meta Quest 2
- PICO 4 Ultra
- PICO 4 / PICO 4 Pro / Enterprise

<details><summary>后续计划支持</summary>
PCVR<br>
Meta VR 眼镜<br>
PICO Space Pro<br>
SteamVR<br>
HTC VIVE Focus<br>
HTC VIVE XR Elite<br>
Valve Index<br>
HTC VIVE Pro / Pro 2<br>
PS VR2<br>
</details>

如有问题、建议或需要支持，请在仓库提交 Issue，或发送邮件至 [rob.bingru.li@outlook.com](mailto:rob.bingru.li@outlook.com)。
