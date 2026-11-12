using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace UTXR.Devices
{
    /// <summary>Reads physical controller features directly from Unity XR. No Input Actions are owned or required.</summary>
    public sealed class XRControllerStateProvider : IXRDeviceStateProvider
    {
        private sealed class SideSource
        {
            public XRSide Side;
            public InputDevice Device;
            public XRProviderStatus Status;
            public XRDeviceSample Raw;
        }

        private readonly List<InputDevice> _devices = new List<InputDevice>(4);
        private static readonly string[] HandTrackingNames =
        {
            "handinteraction", "handtracking", "metaquesthand", "oculushand", "picohand", "customhand",
            "xrhanddevice", "metaaimhand", "hololenshand", "palmpose"
        };
        private readonly SideSource _left = new SideSource { Side = XRSide.Left };
        private readonly SideSource _right = new SideSource { Side = XRSide.Right };
        private bool _devicesChanged = true, _disposed;
        public XRInputMode Mode => XRInputMode.Controller;
        public XRProviderStatus Status { get; private set; }

        public XRControllerStateProvider()
        {
            InputDevices.deviceConnected += HandleDeviceChange;
            InputDevices.deviceDisconnected += HandleDeviceChange;
            InputDevices.deviceConfigChanged += HandleDeviceChange;
        }

        public void Sample()
        {
            ThrowIfDisposed();
            if (_devicesChanged)
            {
                _devicesChanged = false;
                _devices.Clear();
                InputDevices.GetDevices(_devices);
                Resolve(_left);
                Resolve(_right);
            }
            SampleSide(_left);
            SampleSide(_right);
            Status = !_left.Raw.Status.IsReady ? _left.Raw.Status : _right.Raw.Status;
        }

        private void Resolve(SideSource source)
        {
            source.Device = default;
            source.Status = Error(source.Side, XRProviderStatusCode.DeviceUnavailable,
                "No physical controller is available. Check headset settings and wake the controller.");
            foreach (var device in _devices)
            {
                if (!device.isValid || !MatchesCharacteristics(device.characteristics, source.Side) ||
                    IsHandTrackingDeviceName(device.name)) continue;
                if (source.Device.isValid)
                {
                    source.Status = Error(source.Side, XRProviderStatusCode.AmbiguousSource,
                        "Multiple physical controllers match this side. Disconnect the extra device.");
                    source.Device = default;
                    return;
                }
                source.Device = device;
                source.Status = XRProviderStatus.Ready;
            }
        }

        // Unity XR exposes a device name, not an Input System layout. Keep the characteristics check as the primary gate.
        private static bool IsHandTrackingDeviceName(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return false;
            foreach (string name in HandTrackingNames)
                if (deviceName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool MatchesCharacteristics(InputDeviceCharacteristics characteristics, XRSide side)
        {
            var sideFlag = side == XRSide.Left ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right;
            var otherSide = side == XRSide.Left ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left;
            var required = InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.TrackedDevice | sideFlag;
            var excluded = InputDeviceCharacteristics.HandTracking | InputDeviceCharacteristics.HeadMounted | otherSide;
            return (characteristics & required) == required && (characteristics & excluded) == 0;
        }

        private static void SampleSide(SideSource source)
        {
            if (!source.Status.IsReady)
            {
                source.Raw = new XRDeviceSample(source.Status, InputTrackingState.None, false);
                return;
            }
            var device = source.Device;
            if (!device.isValid)
            {
                source.Raw = new XRDeviceSample(Error(source.Side, XRProviderStatusCode.DeviceUnavailable,
                    "The physical controller is disconnected."), InputTrackingState.None, false);
                return;
            }
            if (!device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) ||
                !device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState tracking))
            {
                source.Raw = new XRDeviceSample(Error(source.Side, XRProviderStatusCode.InvalidData,
                    "The runtime did not provide IsTracked and TrackingState."), InputTrackingState.None, false);
                return;
            }
            var pose = Pose.identity;
            if (((tracking & InputTrackingState.Position) != 0 && !device.TryGetFeatureValue(CommonUsages.devicePosition, out pose.position)) ||
                ((tracking & InputTrackingState.Rotation) != 0 && !device.TryGetFeatureValue(CommonUsages.deviceRotation, out pose.rotation)))
            {
                source.Raw = new XRDeviceSample(Error(source.Side, XRProviderStatusCode.InvalidData,
                    "The runtime marked pose data valid but did not provide it."), tracking, tracked);
                return;
            }
            Vector3? linear = tracked && (tracking & InputTrackingState.Velocity) != 0 &&
                device.TryGetFeatureValue(CommonUsages.deviceVelocity, out var lv) ? lv : (Vector3?)null;
            Vector3? angular = tracked && (tracking & InputTrackingState.AngularVelocity) != 0 &&
                device.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out var av) ? av : (Vector3?)null;
            source.Raw = new XRDeviceSample(XRProviderStatus.Ready, tracking, tracked, pose, linear, angular);
        }

        public XRDeviceSample GetSample(XRSide side)
        {
            ThrowIfDisposed();
            switch (side)
            {
                case XRSide.Left: return _left.Raw;
                case XRSide.Right: return _right.Raw;
                default: throw new ArgumentOutOfRangeException(nameof(side));
            }
        }

        private static XRProviderStatus Error(XRSide side, XRProviderStatusCode code, string message) =>
            new XRProviderStatus(code, $"{side} controller: {message}");
        private void HandleDeviceChange(InputDevice device) { _devicesChanged = true; }
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(XRControllerStateProvider)); }
        public void Dispose()
        {
            if (_disposed) return;
            InputDevices.deviceConnected -= HandleDeviceChange;
            InputDevices.deviceDisconnected -= HandleDeviceChange;
            InputDevices.deviceConfigChanged -= HandleDeviceChange;
            _disposed = true;
        }
    }
}
