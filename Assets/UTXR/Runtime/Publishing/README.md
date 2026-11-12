# XR DDS 发布模块

## 代码职责

- `XRPublishConfiguration.cs`：定义固定消息列表、启用值、topic、类型、QoS 和数据源，按输入模式生成发布计划。
- `XRInputDdsRouter.cs`：控制启停和频率；消息开关改变时重建会话，模式改变或网络断开时停止。
- `XRInputActionDataSource.cs`：采集启用消息需要的 Input Actions，缓存头部、眼睛、手柄和按键值。
- `XRHandJointDataSource.cs`：按左右手开关采集关节、整手状态和腕部速度。
- `XRBodyPoseDataSource.cs`：读取已初始化身体骨架的世界位姿。
- `XRDataFrames.cs`：定义 capture 接口和手部、身体原始数据帧。
- `XRPublishPlan.cs`：定义 Source、Route 和本次会话的固定话题集合。
- `XRPublishContracts.cs`：定义消息通道、待发送样本和传输接口。
- `XRPublishSession.cs`：刷新数据源、执行 Route、发送样本，并负责资源释放。
- `XRPublishEncoding.cs`：打包手部、身体数组并转换坐标。
- `XRCycloneDdsTransport.cs`：管理 DDS runtime/writer，写入标准 ROS 2 消息。

相关 UI 脚本位于 `../UI/`：

- [XRPublishingUIController.cs](../UI/XRPublishingUIController.cs)：服务 toggle、目标 IP 和网卡选择结果。
- [XRPublishMessageListUI.cs](../UI/XRPublishMessageListUI.cs)：独立的消息配置 UI；在编辑器生成配置项，运行时同步文本、Toggle 和启用值。Configuration 不引用 UI 控件。

## 数据流

```text
Inspector enabled / 消息 UI Toggle
    → Configuration.MessagesChanged → Router 重建 Plan 和 Session

Input Actions / XR Hands / Body
    → Capture 快照 → Route 组装 Sample → Transport → CycloneDDS → ROS 2
```

每个 Source 每轮只刷新一次，多条 Route 共用快照。关闭消息后不创建其 writer、不执行其打包与发送；数据源只采集其它启用消息仍需要的共享依赖。全部关闭时不创建 DDS runtime。

Hand 与 Controller 发布互斥；切换模式会停止发布，需手动重启。UI 始终列出全部 19 条消息配置，不随模式增删。身体消息默认关闭。

## 配置消息 UI

1. 在 Configuration 勾选各 Message 的 `enabled`，绑定启用消息依赖的数据源和 Action。
2. 在列表父节点或其它模板外的节点添加 `XRPublishMessageListUI`，引用 Configuration 和 `itemTemplate`。
3. 模板 GameObject 内放一个 `TMP_Text` 和一个 Unity UI `Toggle`（可使用 TMP 标签）；无需逐项拖控件引用。
4. 在组件菜单执行 **Generate Message Items (Editor Only)**。脚本隐藏模板，按固定消息列表生成其同级节点，然后保存场景。

运行时只绑定已生成的行，不创建节点。Inspector 勾选会同步 UI，UI Toggle 会反向更新启用值。只有修改代码中的消息列表后，才需要在编辑器重新生成。后续调试显示可继续扩展该 UI 脚本。

Router 需绑定设备状态 Monitor 和 Configuration；发布 UI 需绑定 Router、service toggle、目标 IP 和网卡下拉框。网卡由网络模块管理，Router 订阅网络变化。DDS runtime 同时只能有一个拥有者，运行前停止旧 `DdsPublisher`。

## 消息格式与验证

Hand 数组为 26 × 8 = 208 个 float32，每个关节为 `[state,x,y,z,qx,qy,qz,qw]`；整手状态和腕部速度各用独立话题发送。话题及数组下标见根目录 [中文 README](../../../../README_CN.md#data)。

测试入口：`UTXR > Tests > Run XR Publishing Tests`。
