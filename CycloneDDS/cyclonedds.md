# CycloneDDS 原生跨平台通用通信插件（cdds_plugin）技术文档与架构设计

## 1. 核心架构与设计定位

1. **纯粹 DDS 底层规范**：
   - 本插件定位为 **Unity 跨平台的通用原生 CycloneDDS 通信插件**，通过标准 OMG Data Distribution Service 规范在局域网内实现微秒级低延迟数据发布与订阅。
   - 彻底摒弃任何重型第三方通信封装或外部中转 Proxy，直接基于官方 [eclipse-cyclonedds](https://github.com/eclipse-cyclonedds/cyclonedds) 原生 C 运行时（`libddsc`）构建。

2. **零硬编码互通性与边界划分**：
   - **底层纯粹性**：C 原生层与 C# 基础通信层仅负责纯粹的 DDS 基础设施（DomainParticipant、Topic、DataWriter、DataReader、QoS）。C 原生代码内不硬编码任何特定上层框架的前缀或特定业务字段。
   - **对端为 ROS 2 时的天然直连**：由于 ROS 2 核心通信层本质上就是基于标准 DDS（CycloneDDS / FastDDS），因此纯 DDS 发布端只要使用对应的 OMG IDL 数据描述符，并按照 ROS 2 协议约定的 DDS 话题命名规范（如 `rt/<topic_name>`）在 C# 业务层或 Inspector 中指定话题，两端即可在网络层天然直连互通，无需引入任何 ROS 2 客户端库（无 `rcl`、无 `ros2-for-unity`）。

3. **跨平台交付标准**：
   - 统一由 `cdds_plugin` 项目维护，输出多平台原生二进制库：
     - **Windows x64**：`cdds_plugin.dll` + `ddsc.dll`（用于 Unity Editor 调试及 PC 独立应用）。
     - **Android ARM64-v8a**：`libcdds_plugin.so` + `libddsc.so`（用于 PICO 4 Ultra / Meta Quest 等 XR 头显一体机）。
   - 全部代码与资源集中于 Unity 标准插件目录 `Assets/CycloneDDS/`，零冗余托管 DLL。

---

## 2. 核心参考资料与本地工程索引

| 分类 | 路径与说明 |
|---|---|
| **C 原生插件工程** | [Tools/cdds_plugin](file:///D:/apex_teleop/xr-dds-teleop/CycloneDDS/cdds_plugin) <br> 包含通用 C 头文件 `cdds_plugin.h`、实现源码 `cdds_plugin.c` 及 CMakeLists.txt |
| **Unity 标准插件包** | [Assets/CycloneDDS](file:///D:/apex_teleop/xr-dds-teleop/Assets/CycloneDDS) <br> 符合 UPM 规范的完整插件目录，包含 Plugins/、Runtime/、Editor/ |
| **CycloneDDS 源码仓库** | [cyclonedds](file:///D:/apex_teleop/km_apex_openxr/cyclonedds) <br> 本地已同步的 Eclipse CycloneDDS 官方源码 |
| **Android ARM64 交叉编译产物** | [cyclonedds/install_android_arm64](file:///D:/apex_teleop/km_apex_openxr/cyclonedds/install_android_arm64) <br> 基于 Unity 6000 NDK 编译的 Android API 29 CycloneDDS 核心库 |
| **Unity 6000 NDK 工具链** | [AndroidPlayer NDK](file:///C:/Program%20Files/Unity/Hub/Editor/6000.0.69f1/Editor/Data/PlaybackEngines/AndroidPlayer/NDK) <br> Unity 内置 Clang 18 与 `android.toolchain.cmake` |

---

## 3. 标准数据类型支持清单

底层 C 插件注册了通用的 OMG IDL 描述符，上层业务层（C#）可根据需要按枚举类型自由创建对应话题的 Publisher 或 Subscriber：

| 消息类型枚举 | 对应 IDL 类型描述符 | 常用业务场景 | 典型发布接口 |
|---|---|---|---|
| `StdBool` (1) | `std_msgs/msg/Bool` | 运行状态、焦点激活、布尔控制信号 | `cdds_publish_bool` |
| `StdFloat32` (2) | `std_msgs/msg/Float32` | 扳机开度、模拟量、单一传感器读数 | `cdds_publish_float` |
| `StdFloat32MultiArray` (3) | `std_msgs/msg/Float32MultiArray` | 多自由度浮点数组、离散浮点流 | `cdds_publish_raw` |
| `StdInt32` (4) | `std_msgs/msg/Int32` | 模式枚举、离散状态码 | `cdds_publish_int32` |
| `GeometryPoseStamped` (10) | `geometry_msgs/msg/PoseStamped` | 头部、手部等空间位姿与时间戳 | `cdds_publish_pose_stamped` |
| `GeometryTwistStamped` (11) | `geometry_msgs/msg/TwistStamped` | 线速度与角速度 | `cdds_publish_twist_stamped` |
| `GeometryAccelStamped` (12) | `geometry_msgs/msg/AccelStamped` | 线加速度与角加速度 | `cdds_publish_accel_stamped` |

---

## 4. 通用 C API 导出层设计 (`cdds_plugin.h`)

API 采用句柄化（Handle-based）设计，函数名全部以 `cdds_` 规范命名，避免与 CycloneDDS 内部符号产生链接冲突：

```c
#ifndef CDDS_PLUGIN_H
#define CDDS_PLUGIN_H

#include <stdint.h>
#include <stdbool.h>

#if defined(_WIN32)
#define CDDS_EXPORT __declspec(dllexport)
#else
#define CDDS_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// 消息类型
typedef enum {
    DDS_TYPE_UNKNOWN = 0,
    DDS_TYPE_STD_MSGS_BOOL = 1,
    DDS_TYPE_STD_MSGS_FLOAT32 = 2,
    DDS_TYPE_STD_MSGS_FLOAT32_MULTI_ARRAY = 3,
    DDS_TYPE_STD_MSGS_INT32 = 4,
    DDS_TYPE_GEOMETRY_MSGS_POSE_STAMPED = 10,
    DDS_TYPE_GEOMETRY_MSGS_TWIST_STAMPED = 11,
    DDS_TYPE_GEOMETRY_MSGS_ACCEL_STAMPED = 12
} dds_msg_type_t;

// QoS 策略预设
typedef enum {
    DDS_QOS_SENSOR_DATA = 0,    // BestEffort + KeepLast(1)，适用于高频空间位姿与遥操作流
    DDS_QOS_DEFAULT = 1,        // Reliable + KeepLast(10)，通用可靠通信
    DDS_QOS_STATE_TRANSIENT = 2 // Reliable + TransientLocal(1)，适用于状态与信令
} dds_qos_preset_t;

// 生命周期管理 (支持传入对端 IP 或完整 CycloneDDS XML 配置)
CDDS_EXPORT int cdds_init(int domain_id, const char* peer_ip_or_xml);
CDDS_EXPORT void cdds_shutdown(void);
CDDS_EXPORT bool cdds_is_initialized(void);

// 实体创建 (话题名由外部参数直接传入，内部零篡改)
CDDS_EXPORT int cdds_create_publisher(const char* topic_name, dds_msg_type_t type, dds_qos_preset_t qos_preset);
CDDS_EXPORT int cdds_create_subscriber(const char* topic_name, dds_msg_type_t type, dds_qos_preset_t qos_preset);
CDDS_EXPORT void cdds_destroy_entity(int handle);

// 数据发布接口
CDDS_EXPORT int cdds_publish_bool(int pub_handle, bool value);
CDDS_EXPORT int cdds_publish_float(int pub_handle, float value);
CDDS_EXPORT int cdds_publish_int32(int pub_handle, int32_t value);
CDDS_EXPORT int cdds_publish_pose_stamped(
    int pub_handle, int64_t timestamp_ns, const char* frame_id,
    double px, double py, double pz,
    double qx, double qy, double qz, double qw
);

// 连接状态探测
CDDS_EXPORT int cdds_get_matched_subscriptions(int pub_handle);
CDDS_EXPORT int cdds_get_matched_publications(int sub_handle);

#ifdef __cplusplus
}
#endif

#endif // CDDS_PLUGIN_H
```

---

## 5. 跨平台编译与构建流程

### 5.1 Windows x64 编译 (MSVC 2022)

在 Windows 平台通过 MSVC 编译生成 `cdds_plugin.dll`：

```powershell
cd D:\apex_teleop\xr-dds-teleop\CycloneDDS\cdds_plugin
cmake -B build -G "Visual Studio 17 2022" -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release
```

输出文件：
- `build/Release/cdds_plugin.dll`
- `build/Release/test_publish.exe`

将动态库同步至：`Assets/CycloneDDS/Plugins/Windows/x86_64/`。

### 5.2 Android ARM64-v8a 交叉编译 (Unity NDK)

利用 Unity 内置 NDK（Clang 18.0.3 + Ninja）直接生成 Android 原生动态库：

```powershell
$NDK = "C:/Program Files/Unity/Hub/Editor/6000.0.69f1/Editor/Data/PlaybackEngines/AndroidPlayer/NDK"
$TOOLCHAIN = "$NDK/build/cmake/android.toolchain.cmake"
$NINJA = "C:/Program Files/Microsoft Visual Studio/2022/Community/Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe"
$CDDS_ARM64 = "D:/apex_teleop/km_apex_openxr/cyclonedds/install_android_arm64"

cmake -B build_android_arm64 -G Ninja `
  -DCMAKE_MAKE_PROGRAM=$NINJA `
  -DCMAKE_TOOLCHAIN_FILE=$TOOLCHAIN `
  -DANDROID_ABI=arm64-v8a `
  -DANDROID_PLATFORM=android-29 `
  -DANDROID_STL=c++_static `
  -DCMAKE_BUILD_TYPE=Release `
  -DCMAKE_PREFIX_PATH=$CDDS_ARM64

cmake --build build_android_arm64
```

输出文件：
- `build_android_arm64/libcdds_plugin.so` (约 88 KB)

将动态库部署至：`Assets/CycloneDDS/Plugins/Android/arm64-v8a/`，并确保配套包含 `libddsc.so`。

---

## 6. Unity 插件标准化组织 (Assets/CycloneDDS)

```text
Assets/CycloneDDS/
├── package.json                   # UPM 包声明清单
├── README.md                      # 插件使用指南
├── Plugins/
│   ├── Windows/x86_64/
│   │   ├── ddsc.dll               # CycloneDDS 官方底层运行时
│   │   └── cdds_plugin.dll        # Windows 原生插件动态库
│   └── Android/arm64-v8a/
│       ├── libddsc.so             # Android 平台 CycloneDDS 核心库
│       └── libcdds_plugin.so      # Android 原生插件动态库
├── Runtime/
│   ├── CycloneDDS.Runtime.asmdef  # 独立程序集定义
│   ├── CddsNative.cs              # P/Invoke 接口声明与 Windows 预加载管理
│   └── DdsPublisher.cs            # 空间位姿与控制量发布 MonoBehaviour
└── Editor/
    ├── CycloneDDS.Editor.asmdef   # 编辑器程序集定义
    └── DdsPublisherEditor.cs      # Inspector 实时监视与网络状态测试面板
```

---

## 7. 联调与网络验证流程

### 7.1 Windows 独立测试程序验证
在命令行运行独立测试程序（无需打开 Unity）：
```powershell
cd D:\apex_teleop\xr-dds-teleop\CycloneDDS\cdds_plugin\build\Release
.\test_publish.exe 192.168.8.181
```

### 7.2 Unity 运行与监视验证
1. 打开 Unity 编辑器，挂载 `DdsPublisher` 组件。
2. 填入目标主机 IP（如 Orin IP `192.168.8.181`）与 DDS Domain ID（默认 `0`）。
3. 检查 Inspector 中配置的话题名称：
   - 若对端为标准 DDS 节点：输入纯 DDS 话题名（如 `xr/head/pose`）。
   - 若对端通过 ROS 2 监听：输入 ROS 2 DDS 传输层约定的话题名（如 `rt/xr/head/pose`）。
4. 点击 Unity **Play** 按钮。
5. 观察 Inspector 面板中的 `Matched Subscribers` 数量：
   - 当局域网对端存在订阅者匹配时，计数值将从 `0` 变为 `1` 或更高，同时发布帧数持续递增。
