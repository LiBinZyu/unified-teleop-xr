# XR Publishing over DDS

## Components

- `XRPublishConfiguration.cs` defines the message list, enabled flags, topics, types, QoS, and data sources. It builds a publish plan for the active input mode.
- `XRInputDdsRouter.cs` starts and stops publishing and sets the publish rate. It rebuilds the session when message selections change and stops publishing when the input mode changes or the network disconnects.
- `XRInputActionDataSource.cs` reads the Input Actions required by enabled messages and caches head, eye, controller, and button data.
- `XRHandJointDataSource.cs` captures joint poses, tracking state, and wrist velocity for each enabled hand.
- `XRBodyPoseDataSource.cs` reads world-space poses from the initialized body skeleton.
- `XRDataFrames.cs` defines the capture interfaces and raw hand and body frames.
- `XRPublishPlan.cs` defines the sources, routes, and topic set for a session.
- `XRPublishContracts.cs` defines message channels, outgoing samples, and the transport interface.
- `XRPublishSession.cs` updates data sources, runs routes, sends samples, and releases resources.
- `XRPublishEncoding.cs` packs hand and body arrays and converts coordinates.
- `XRCycloneDdsTransport.cs` manages the DDS runtime and writers and sends standard ROS 2 messages.

[XRPublishingUIController.cs](../UI/XRPublishingUIController.cs) handles the publishing toggle, destination IP, and network interface selection. [XRPublishMessageListUI.cs](../UI/XRPublishMessageListUI.cs) generates the message rows in the editor and keeps the labels, toggles, and enabled flags in sync at runtime. The configuration has no dependency on UI controls.

## Data flow

```text
Inspector enabled flag or message toggle
    → Configuration.MessagesChanged → Router rebuilds the plan and session

Input Actions / XR Hands / Body
    → Capture snapshot → Route builds a sample → Transport → Cyclone DDS → ROS 2
```

Each data source updates once per publish cycle. Routes share the captured snapshot. Disabling a message skips writer creation, encoding, and transmission for that message. Data sources only capture what the enabled messages need. With every message disabled, the session skips DDS runtime initialization.

Hand and controller publishing are mutually exclusive. Switching modes stops publishing and requires a manual restart. The UI lists all 19 message settings in either mode. Body messages are disabled by default.

## Scene and UI setup

1. Enable the desired messages in `XRPublishConfiguration` and assign the data sources and Input Actions they use.
2. Add `XRPublishMessageListUI` to the list container or another GameObject outside the template. Assign the configuration and `itemTemplate`.
3. Put a `TMP_Text` and a Unity UI `Toggle` on the template or its children. The script finds the controls automatically.
4. Run `Generate Message Items (Editor Only)` from the component context menu. It hides the template and generates the message rows next to it. Save the scene.

At runtime the component binds the generated rows. Changes in the Inspector and the UI toggles update the same enabled flags. Regenerate the rows after changing the message definitions in code.

Assign the device state monitor and publish configuration to the router. Assign the router, publishing toggle, destination IP field, and network interface dropdown to the publishing UI. The networking module manages interface selection and the router listens for changes. Only one component can own the DDS runtime in a Unity process. Stop any other DDS sessions before starting this module.

## Message format and tests

A hand sample contains 208 `float32` values, eight for each of the 26 joints in the order `[state, x, y, z, qx, qy, qz, qw]`. Whole-hand tracking state and wrist velocity use separate topics. See [Data and topics](../../../../README.md#data) for topic names and array indices.

Run the tests from `UTXR > Tests > Run XR Publishing Tests` in Unity.
