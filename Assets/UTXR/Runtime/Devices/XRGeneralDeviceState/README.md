# XR Device State

Manual, bilateral Controller / Hand selection with shared tracking and gesture algorithms.

## Architecture

`Provider → XRDeviceSample → XRDeviceStateCore → XRDeviceState → Application`

- **Providers:** `XRControllerStateProvider` reads Unity XR device features; `XRHandStateProvider` reads XR Hands root pose and wrist velocity. Neither requires Input Actions.
- **Core:** 6DoF validation, debounce, kinematic checks, warning hold, Swing / TripleSwing.
- **Session:** provider ownership, switching, sampling, and notifications.
- **Monitor:** Unity lifecycle, Inspector configuration, interaction roots, and event forwarding.
- **Contracts:** `IXRDeviceState` for consumers; `IXRDeviceStateProvider` for backends.

## Usage

```csharp
IXRDeviceState devices = assignedMonitor;
var status = devices.SetMode(XRInputMode.Hand);
if (!status.IsReady) Debug.LogError(status.ToString());

var left = devices.GetState(XRSide.Left);
if (left.IsTracked)
{
    Pose pose = left.Pose;
    XRGesture gestures = left.Gestures;
}
```

## Rules

- Monitor needs no tracking or velocity Action references. XRI interaction and UI actions remain separate.
- Headset Controllers / Hands / Both settings are separate; prefer Both. Mode selection is manual. Controller device changes refresh the physical device selection. Missing or ambiguous sources report errors; modes never substitute for each other.
- Controllers must have Controller, TrackedDevice, and the correct side flags. HandTracking and HMD devices are excluded; known hand device names provide an additional exclusion. Unity XR device names are not Input System layouts. Hand mode uses only the active loader's XRHandSubsystem; its owner controls startup/shutdown. Use `RefreshProvider()` after changing the loader.
- Poses use XR tracking space. `Raw` preserves source data; missing runtime velocity is `null`. Kinematic filtering requires runtime linear velocity.
- Velocity is read only when valid. No velocity is synthesized or replaced with acceleration. `KinematicDrift` / `Combined` report an error when runtime velocity is unavailable; explicitly select another filter if needed.
- Legacy tracking rules: `KinematicDrift` / `Combined` reject linear speed below **0.001 m/s for more than 0.1 s**, speed above **8 m/s**, or a position jump above **1.5 cm and 4 m/s** (sample gap under 1 s). Stationary rejection warns even when hardware remains tracked. `None` / `BitwiseDebounce` do not test velocity.
- Valid hardware tracking is accepted on its first sample. Invalid hardware tracking or filter rejection raises a warning; warning stays visible for **2 s** after recovery. Missing providers report errors and clear tracking.
- Core rejects non-finite data and velocity sentinel values even with filtering disabled. Raw values remain available for diagnosis. Logs include status details and raw velocities.
- Swing uses position change / delta time, with a default threshold of **1.5 m/s**. Three swings within the configured window produce TripleSwing. Acceleration is unused.
- `TrackingChanged` reports tracking/status transitions. Read current pose and gesture pulses through `GetState()` each update.
- Settings are captured when the session is created. Re-enable the monitor to apply Inspector changes. Switching, refresh, and tracking rejection reset gesture history.
- Sessions own providers; providers borrow Unity resources. Dispose only owned resources. Keep the monitor outside interaction roots.

Tests: **UTXR > Tests > Run XR Device State Contract Tests**.
