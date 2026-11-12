using System;
using UnityEngine.XR;

namespace UTXR.Devices
{
    /// <summary>
    /// Owns the selected provider; independent of scenes and Unity lifecycle callbacks.
    /// The caller supplies the factory and sample clock. There are no automatic retries or mode changes.
    /// Notifications describe committed snapshots: both sides and status are readable before any event.
    /// Reentrant switching from notification handlers is a caller error; schedule it for the next update.
    /// </summary>
    public sealed class XRDeviceStateSession : IXRDeviceState, IDisposable
    {
        private readonly Func<XRInputMode, IXRDeviceStateProvider> _factory;
        private IXRDeviceStateProvider _provider;
        private XRDeviceState _left, _right;
        private readonly XRDeviceStateCore _leftCore, _rightCore;
        private bool _disposed, _publishing;
        public XRInputMode Mode { get; private set; }
        public XRProviderStatus ProviderStatus { get; private set; }
        public event Action<XRInputMode> ModeChanged;
        public event Action<XRProviderStatus> ProviderStatusChanged;
        public event Action<XRSide, XRDeviceState> TrackingChanged;

        public XRDeviceStateSession(Func<XRInputMode, IXRDeviceStateProvider> factory, XRInputMode initialMode,
            XRDeviceStateSettings settings = null)
        {
            ValidateMode(initialMode);
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            settings = settings ?? new XRDeviceStateSettings();
            _leftCore = new XRDeviceStateCore(settings);
            _rightCore = new XRDeviceStateCore(settings);
            Mode = initialMode;
            ProviderStatus = new XRProviderStatus(XRProviderStatusCode.Disabled, "Session has not been initialized. Call RefreshProvider().");
            _left = _right = new XRDeviceState(new XRDeviceSample(ProviderStatus, InputTrackingState.None, false), false);
        }

        public XRProviderStatus SetMode(XRInputMode mode)
        {
            EnsureMutable();
            ValidateMode(mode);
            if (Mode == mode) return ProviderStatus;
            return Bind(mode, true);
        }
        public XRProviderStatus RefreshProvider() { EnsureMutable(); return Bind(Mode, false); }

        private XRProviderStatus Bind(XRInputMode mode, bool modeChanged)
        {
            XRDeviceState previousLeft = _left, previousRight = _right;
            XRProviderStatus previousStatus = ProviderStatus;
            Mode = mode;
            ClearSnapshot(new XRProviderStatus(XRProviderStatusCode.Disabled, "Rebinding selected provider."));
            try
            {
                ReleaseProvider();
                _provider = _factory(mode);
                if (_provider == null || _provider.Mode != mode)
                    throw new InvalidOperationException("Provider factory must return a non-null provider of the requested mode.");
                ReadProvider(0f);
            }
            catch (Exception exception)
            {
                RecordFault(exception);
            }
            Publish(previousStatus, previousLeft, previousRight, modeChanged, true);
            return ProviderStatus;
        }

        public void Sample(float deltaTime)
        {
            EnsureMutable();
            if (!XRDeviceStateCore.IsFinite(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (_provider == null) return; // A failed bind stays failed until an explicit RefreshProvider/SetMode.
            XRDeviceState previousLeft = _left, previousRight = _right;
            XRProviderStatus previousStatus = ProviderStatus;
            try { ReadProvider(deltaTime); }
            catch (Exception exception) { RecordFault(exception); }
            Publish(previousStatus, previousLeft, previousRight, false, false);
        }

        private void ReadProvider(float deltaTime)
        {
            _provider.Sample();
            // Read the complete sample before committing any of it.
            var left = _leftCore.Process(_provider.GetSample(XRSide.Left), deltaTime);
            var right = _rightCore.Process(_provider.GetSample(XRSide.Right), deltaTime);
            ProviderStatus = !_provider.Status.IsReady ? _provider.Status :
                !left.Status.IsReady ? left.Status : right.Status;
            _left = left;
            _right = right;
        }

        private void RecordFault(Exception exception)
        {
            // Capture the original error and any teardown error. Never continue with another provider.
            try { ReleaseProvider(); }
            catch (Exception disposeException) { exception = new AggregateException(exception, disposeException); }
            var code = exception is ArgumentException ? XRProviderStatusCode.InvalidConfiguration : XRProviderStatusCode.ProviderFaulted;
            ClearSnapshot(new XRProviderStatus(code, exception.ToString()));
        }
        private void ReleaseProvider()
        {
            var owned = _provider;
            _provider = null;
            owned?.Dispose();
        }
        private void ClearSnapshot(XRProviderStatus status)
        {
            ProviderStatus = status;
            _left = _right = new XRDeviceState(new XRDeviceSample(status, InputTrackingState.None, false), false);
            _leftCore.Reset();
            _rightCore.Reset();
        }

        private void Publish(XRProviderStatus previousStatus, XRDeviceState previousLeft,
            XRDeviceState previousRight, bool modeChanged, bool force)
        {
            _publishing = true;
            try
            {
                if (modeChanged) ModeChanged?.Invoke(Mode);
                if (force || !previousStatus.Equals(ProviderStatus)) ProviderStatusChanged?.Invoke(ProviderStatus);
                if (force || !previousLeft.HasSameTrackingState(_left)) TrackingChanged?.Invoke(XRSide.Left, _left);
                if (force || !previousRight.HasSameTrackingState(_right)) TrackingChanged?.Invoke(XRSide.Right, _right);
            }
            finally { _publishing = false; }
        }

        public XRDeviceState GetState(XRSide side)
        {
            switch (side)
            {
                case XRSide.Left: return _left;
                case XRSide.Right: return _right;
                default: throw new ArgumentOutOfRangeException(nameof(side));
            }
        }
        private static void ValidateMode(XRInputMode mode)
        {
            if (mode != XRInputMode.Controller && mode != XRInputMode.Hand) throw new ArgumentOutOfRangeException(nameof(mode));
        }
        private void EnsureMutable()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(XRDeviceStateSession));
            if (_publishing) throw new InvalidOperationException("Do not change/sample the session inside its event callbacks.");
        }
        public void Dispose()
        {
            if (_disposed) return;
            EnsureMutable();
            _disposed = true;
            var previousStatus = ProviderStatus;
            var previousLeft = _left;
            var previousRight = _right;
            try { ReleaseProvider(); }
            finally
            {
                ClearSnapshot(new XRProviderStatus(XRProviderStatusCode.Disabled, "Session disposed."));
                Publish(previousStatus, previousLeft, previousRight, false, true);
            }
        }
    }
}
