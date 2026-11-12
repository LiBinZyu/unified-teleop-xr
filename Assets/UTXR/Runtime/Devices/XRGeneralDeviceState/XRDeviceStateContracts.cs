using System;

namespace UTXR.Devices
{
    /// <summary>Main-thread application API, shared by controller and hand modes.</summary>
    public interface IXRDeviceState
    {
        XRInputMode Mode { get; }
        XRProviderStatus ProviderStatus { get; }
        XRDeviceState GetState(XRSide side);
        XRProviderStatus SetMode(XRInputMode mode);
        XRProviderStatus RefreshProvider();

        event Action<XRInputMode> ModeChanged;
        event Action<XRProviderStatus> ProviderStatusChanged;
        // Tracking/status transitions only. Read the current pose and gesture through GetState.
        event Action<XRSide, XRDeviceState> TrackingChanged;
    }

    /// <summary>
    /// Supplies raw measurements. The session owns/disposes this provider and runs the algorithms.
    /// Unity devices/subsystems are borrowed; disposing must not stop or destroy them.
    /// </summary>
    public interface IXRDeviceStateProvider : IDisposable
    {
        XRInputMode Mode { get; }
        XRProviderStatus Status { get; }
        void Sample();
        XRDeviceSample GetSample(XRSide side);
    }
}
