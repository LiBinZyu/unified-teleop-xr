# CycloneDDS Native Plugin for Unity

高性能、零 GC、标准 OMG DDS 通信插件，基于原生 [Eclipse CycloneDDS](https://github.com/eclipse-cyclonedds/cyclonedds) C API 封装。

支持平台：
- **Windows x64**（Unity Editor 调试与 PC 独立运行）
- **Android ARM64-v8a**（XR 一体机头显，如 PICO 4 Ultra / Meta Quest）

---

## 目录分层架构（标准 UPM 插件结构）

```text
Assets/CycloneDDS/
├── package.json                   # Unity UPM 插件清单
├── README.md                      # 本说明文档
├── Plugins/                       # 原生动态链接库
│   ├── Windows/x86_64/            # Windows 64 位平台
│   │   ├── ddsc.dll               # CycloneDDS 核心底层动态库
│   │   └── cdds_plugin.dll        # DDS C API 原生封装库
│   └── Android/arm64-v8a/         # Android 64 位平台 (PICO / Quest)
│       ├── libddsc.so             # CycloneDDS Android 核心库
│       └── libcdds_plugin.so      # Android 原生封装库
├── Runtime/                       # 核心底层运行时 (Assembly: CycloneDDS.Runtime)
│   ├── CycloneDDS.Runtime.asmdef  # 运行时程序集定义
│   ├── CddsNative.cs              # 纯底层 C P/Invoke 原语与原生生命周期
│   ├── CddsMessages.cs            # std_msgs 与 geometry_msgs 结构体 (Blittable)
│   ├── DdsLog.cs                  # 独立分级日志器 (None/Error/Warn/Info/Debug)
│   └── DdsConfig.cs               # 应用层静态配置与辅助工具 (XML构建/IP检测/ENU坐标转换)
├── Samples/                       # 示例与演示 (Assembly: CycloneDDS.Samples)
│   ├── CycloneDDS.Samples.asmdef  # 示例程序集定义
│   └── DdsPublisher.cs            # 空间位姿与离散量发布 Sample 脚本
└── Editor/                        # 编辑器调试扩展 (Assembly: CycloneDDS.Editor)
    ├── CycloneDDS.Editor.asmdef   # 编辑器程序集定义
    └── DdsPublisherEditor.cs      # Sample 专属 Inspector 实时监控与手动控制面板
```

---

## 核心底层方法与辅助方法职责划分

### 1. 核心底层原语：`CddsNative.cs`
仅包含无副作用的底层 P/Invoke 接口，不含任何 Update 循环或托管派发逻辑：
- `cdds_init(int domain_id, string config_str)` / `cdds_shutdown()`
- `cdds_create_publisher(string topic, DdsMessageType type, DdsQosPreset qos)`
- `cdds_create_subscriber(string topic, DdsMessageType type, DdsQosPreset qos)`
- `cdds_destroy_entity(int handle)`
- `cdds_publish_*(int handle, ...)` / `cdds_take_*(int handle, ...)`

### 2. 应用层静态辅助工具：`DdsConfig.cs`
将所有配置、XML 构建、日志模式控制、数学转换集中于此：
- **日志模式与调试开关**：
  - `DdsConfig.SetLogLevel(DdsLogLevel.Debug)`
  - `DdsConfig.SetDebugLog(true / false)`
  - `DdsConfig.ToggleDebugLog()`
- **网络与 XML 配置生成**：
  - `DdsConfig.BuildCycloneDdsXml(targetIp)`：自动寻找同网段本地 IP 绑定并配置 Peer。
  - `DdsConfig.GetMatchingLocalIp(targetIp)`：根据对端 IP 自动匹配本地网卡地址。
- **时间戳与坐标系转换**：
  - `DdsConfig.GetCurrentTimestampNs()`：纳秒级 UTC Unix 时间戳。
  - `DdsConfig.CreateHeader(frameId)`：标准 Header 结构生成。
  - `DdsConfig.UnityToRos(...)` / `DdsConfig.RosToUnity(...)`：Unity 左手系与机器人 ROS 坐标系 (右手系 ENU: X前, Y左, Z上)转换。
  - `DdsConfig.ToGeometryPoint(pos)` / `DdsConfig.ToGeometryPose(pos, rot)`

### 3. 示例代码：`Samples/DdsPublisher.cs`
展示如何将 Unity XR 跟踪数据通过 `DdsConfig` 与 `CddsNative` 组装并发布至 DDS 网络。
