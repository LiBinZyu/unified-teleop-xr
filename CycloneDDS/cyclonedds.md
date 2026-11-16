# Cyclone DDS native plugin

`cdds_plugin` provides native DDS communication for Unity on Windows x64 and Android ARM64. The C library uses the Eclipse Cyclone DDS runtime for discovery, publishers, subscribers, and QoS. Unity calls the library through P/Invoke. The application supplies DDS topic names, which the plugin passes through unchanged.

## ROS 2 interoperability

A DDS publisher can communicate directly with a ROS 2 node using a DDS-based middleware implementation. Both ends must use the same DDS domain ID and compatible topic names, message types, and QoS. For example, the ROS 2 topic `/xr/head/pose` maps to `rt/xr/head/pose` on the DDS wire.

- [Native plugin source](cdds_plugin/) contains the C API, generated IDL descriptors, and CMake targets.
- [Unity package](../Assets/CycloneDDS/) contains the managed bindings, examples, and prebuilt libraries.
- [Eclipse Cyclone DDS](https://github.com/eclipse-cyclonedds/cyclonedds) provides the DDS runtime.

## Native API

See [cdds_plugin.h](cdds_plugin/include/cdds_plugin.h) for the exported signatures and complete list of message types. Use `cdds_init` to start a session with a peer IP or a Cyclone DDS XML configuration. `cdds_create_publisher` and `cdds_create_subscriber` return handles used by the publish, take, and discovery functions. Use `cdds_destroy_entity` to release individual entities and `cdds_shutdown` to end the session.

| Message type | Type ID | Publish function |
| --- | ---: | --- |
| `std_msgs/msg/Bool` | 1 | `cdds_publish_bool` |
| `std_msgs/msg/Float32` | 6 | `cdds_publish_float` |
| `std_msgs/msg/Int32` | 10 | `cdds_publish_int32` |
| `std_msgs/msg/Float32MultiArray` | 17 | `cdds_publish_float32_multi_array` or `cdds_publish_raw` |
| `geometry_msgs/msg/PoseStamped` | 29 | `cdds_publish_pose_stamped` |
| `geometry_msgs/msg/TwistStamped` | 33 | `cdds_publish_twist_stamped` |
| `geometry_msgs/msg/AccelStamped` | 35 | `cdds_publish_accel_stamped` |

| QoS preset | Reliability | Keep-last depth | Durability |
| --- | --- | ---: | --- |
| `DDS_QOS_SENSOR_DATA` | Best effort | 1 | Volatile |
| `DDS_QOS_DEFAULT` | Reliable | 10 | Volatile |
| `DDS_QOS_STATE_TRANSIENT` | Reliable | 1 | Transient local |

The [native plugin README](cdds_plugin/README.md) describes the array wire format, raw sample ownership, and the corresponding tests.

## Build the native plugin

The commands below assume you are at the repository root. Set `CycloneDDS_DIR` to the directory containing `CycloneDDSConfig.cmake` for the target platform.

### Windows x64

Use CMake with Visual Studio 2022 and a Windows x64 build of Cyclone DDS.

```powershell
$cycloneWindows = 'C:/deps/cyclonedds/lib/cmake/CycloneDDS'
cmake -S CycloneDDS/cdds_plugin -B CycloneDDS/cdds_plugin/build -G 'Visual Studio 17 2022' -A x64 "-DCycloneDDS_DIR=$cycloneWindows"
cmake --build CycloneDDS/cdds_plugin/build --config Release
```

The `build/Release` directory contains `cdds_plugin.dll` and `test_publish.exe`. To update the Unity package, copy `cdds_plugin.dll` and the matching `ddsc.dll` to `Assets/CycloneDDS/Plugins/Windows/x86_64/`.

### Android ARM64

Use the NDK installed with Unity's Android Build Support, make Ninja available on `PATH`, and point CMake at a Cyclone DDS build for Android ARM64. The example targets API level 29.

```powershell
$unityNdk = 'C:/path/to/Unity/Editor/Data/PlaybackEngines/AndroidPlayer/NDK'
$cycloneAndroid = 'C:/deps/cyclonedds-android-arm64/lib/cmake/CycloneDDS'
cmake -S CycloneDDS/cdds_plugin -B CycloneDDS/cdds_plugin/build_android_arm64 -G Ninja `
  "-DCMAKE_TOOLCHAIN_FILE=$unityNdk/build/cmake/android.toolchain.cmake" `
  "-DCycloneDDS_DIR=$cycloneAndroid" `
  -DANDROID_ABI=arm64-v8a `
  -DANDROID_PLATFORM=android-29 `
  -DANDROID_STL=c++_static `
  -DCMAKE_BUILD_TYPE=Release `
  -DBUILD_TESTING=OFF
cmake --build CycloneDDS/cdds_plugin/build_android_arm64
```

Copy `build_android_arm64/libcdds_plugin.so` and the matching `libddsc.so` to `Assets/CycloneDDS/Plugins/Android/arm64-v8a/` for Quest or PICO builds.

### Linux and macOS

Install Git, CMake, and a C/C++ toolchain. GCC or Clang works on Linux. On macOS, run `xcode-select --install` if you need Apple's Command Line Tools. Build Cyclone DDS and the plugin for the same architecture. On macOS, use a native shell for Apple silicon (`arm64`) or Intel (`x86_64`), as appropriate.

From the repository root, build Cyclone DDS 0.10.4 once per platform and architecture, then build and test the plugin against it.

```sh
build_dir="$PWD/CycloneDDS/cdds_plugin/build_desktop"
mkdir -p "$build_dir"
git clone --depth 1 --branch 0.10.4 https://github.com/eclipse-cyclonedds/cyclonedds.git "$build_dir/cyclone-src"

cmake -S "$build_dir/cyclone-src" -B "$build_dir/cyclone" \
  -DCMAKE_BUILD_TYPE=Release -DBUILD_TESTING=OFF
cmake --build "$build_dir/cyclone" --parallel
cmake --install "$build_dir/cyclone" --prefix "$build_dir/cyclone-install"

cmake -S CycloneDDS/cdds_plugin -B "$build_dir/plugin" \
  -DCMAKE_BUILD_TYPE=Release -DBUILD_TESTING=ON \
  -DCMAKE_PREFIX_PATH="$build_dir/cyclone-install"
cmake --build "$build_dir/plugin" --parallel
ctest --test-dir "$build_dir/plugin" --output-on-failure
```

The plugin is `build_desktop/plugin/libcdds_plugin.so` on Linux and `build_desktop/plugin/libcdds_plugin.dylib` on macOS. These builds can be used by the [XR Subscriber](subscriber/README.md#build-the-gateway-on-linux-or-macos). The Unity package ships native libraries for Windows x64 and Android ARM64.

## Unity package layout

| Directory | Contents |
| --- | --- |
| `Plugins/` | The native plugin and its Cyclone DDS runtime for each platform. |
| `Runtime/` | `CycloneDDS.Runtime` and the managed P/Invoke bindings in `CddsNative.cs`. |
| `Samples/` | `DdsPublisher.cs` and its input channel examples. |
| `Editor/` | `CycloneDDS.Editor` and the array interoperability test in `CddsArrayTests.cs`. |

## Verify the connection

### Standalone publisher on Windows

Make the matching `ddsc.dll` available to Windows, then run the executable from the repository root with your receiver's IP address. The test runs on DDS domain `0` and reports the number of matched subscribers while sending sample messages.

```powershell
.\CycloneDDS\cdds_plugin\build\Release\test_publish.exe 192.168.8.181
```

### Unity

1. Add `DdsPublisher` to a GameObject and configure its input channels.
2. Set the receiver IP and match its DDS domain ID. The default domain is `0`.
3. Set each channel's DDS topic to match the receiver. For example, use `xr/head/pose` if that is the name on a native DDS reader, or `rt/xr/head/pose` for the ROS 2 topic `/xr/head/pose`.
4. Enter Play Mode and check the publisher Inspector. The matched subscriber count increases when the receiving endpoint connects, and the published frame count advances as samples are sent.
