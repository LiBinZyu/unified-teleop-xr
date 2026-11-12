using System;
using CycloneDDS;
using Network.InterfaceInfo;
using Logger = UTXR.Core.Logger;
using UTXR.Devices;
using UnityEngine;

namespace UTXR.Publishing
{
    /// <summary>Selects a plan at Start. Mode changes stop publishing; restarting is always explicit.</summary>
    [DisallowMultipleComponent]
    public sealed class XRInputDdsRouter : MonoBehaviour
    {
        [SerializeField] private XRGeneralDeviceStateMonitor deviceState;
        [SerializeField] private XRPublishConfiguration configuration;
        [SerializeField] private int domainId;
        [SerializeField, Min(1)] private int publishRateHz = 60;
        private readonly XRPublishSession _session = new();
        private NetworkInfoService _networkService;
        private string _interfaceId, _localIp;
        private string _configurationXml;
        private int _activeDomain;
        private bool _messagesDirty;
        private XRInputMode _publishingMode;
        private double _nextPublishTime;
        private double _publishInterval;
        public bool IsPublishing => _session.IsPublishing;
        public XRInputMode SelectedMode => deviceState != null ? deviceState.Mode : throw new InvalidOperationException("Assign the device monitor.");
        public XRInputMode? PublishingMode => IsPublishing ? _publishingMode : null;
        public string LastError { get; private set; }
        public int DomainId => domainId;
        public event Action<bool> PublishingChanged;

        private void OnEnable()
        {
            if (deviceState == null || configuration == null)
            {
                RecordError(new InvalidOperationException("Assign the device monitor and publishing configuration."));
                enabled = false;
                return;
            }
            deviceState.ModeChanged += HandleModeChanged;
            configuration.MessagesChanged += HandleMessagesChanged;
            SyncNetworkSubscription();
        }

        public void StartPublishing(int domain, string configurationXml, string interfaceId)
        {
            try
            {
                if (IsPublishing) throw new InvalidOperationException("Publishing is already running. Stop it before starting again.");
                if (!isActiveAndEnabled || deviceState == null || !deviceState.isActiveAndEnabled || configuration == null)
                    throw new InvalidOperationException("Enable the router and monitor, and assign the publishing configuration.");
                if (publishRateHz <= 0) throw new InvalidOperationException("Publish rate must be positive.");
                SyncNetworkSubscription();
                if (_networkService == null || !_networkService.isActiveAndEnabled || string.IsNullOrEmpty(interfaceId) ||
                    !_networkService.CurrentSnapshot.TryFindInterfaceById(interfaceId, out _, out var nic, out var localIp) ||
                    !nic.IsUp || nic.IsLoopback || string.IsNullOrEmpty(localIp))
                    throw new InvalidOperationException("The selected network interface is unavailable.");
                _interfaceId = nic.Id;
                _localIp = localIp;
                _publishingMode = deviceState.Mode;
                var plan = configuration.CreatePlan(_publishingMode);
                // Log before acquisition, so failed starts also identify the requested configuration.
                Logger.LogFile($"[XRInputDdsRouter] Starting {_publishingMode}, domain={domain}, rate={publishRateHz} Hz.");
                foreach (var route in plan.Routes)
                    Logger.LogFile($"[XRInputDdsRouter] {route.Source.Name} -> {route.Channel.Topic}; type={route.Channel.Type}; QoS={route.Channel.Qos}; frame={route.Channel.FrameId}; convert={route.Channel.ConvertCoordinates}; inputs={route.Inputs}");
                _publishInterval = 1d / publishRateHz;
                _session.Start(plan, new XRCycloneDdsTransport(domain, configurationXml));
                _configurationXml = configurationXml;
                _activeDomain = domain;
                _messagesDirty = false;
                _nextPublishTime = Time.unscaledTimeAsDouble;
                LastError = null;
                Logger.LogFile($"[XRInputDdsRouter] Started {_publishingMode} publishing.");
            }
            catch (Exception error)
            {
                RecordError(error);
                if (!IsPublishing) PublishingChanged?.Invoke(false);
                throw;
            }
            PublishingChanged?.Invoke(true);
        }

        public void StopPublishing()
        {
            bool wasPublishing = IsPublishing;
            try { _session.Stop(); }
            catch (Exception error) { RecordError(error); throw; }
            finally
            {
                _messagesDirty = false;
                if (wasPublishing)
                {
                    Logger.LogFile("[XRInputDdsRouter] Publishing stopped.");
                    PublishingChanged?.Invoke(false);
                }
            }
        }

        private void HandleModeChanged(XRInputMode mode)
        {
            if (!IsPublishing || mode == _publishingMode) return;
            Fail(new InvalidOperationException("Mode changed during publishing. Session stopped; select a mode and restart explicitly."));
        }

        private void Update()
        {
            if (_networkService != NetworkInfoService.Instance) SyncNetworkSubscription();
            if (!IsPublishing) return;
            try
            {
                if (deviceState == null || !deviceState.isActiveAndEnabled)
                    throw new InvalidOperationException("The device monitor was disabled during publishing.");
                if (deviceState.Mode != _publishingMode)
                    throw new InvalidOperationException("Publishing mode changed without stopping the session.");
                if (configuration == null || !configuration.isActiveAndEnabled)
                    throw new InvalidOperationException("The publishing configuration was disabled.");
                if (_messagesDirty)
                {
                    _messagesDirty = false;
                    var plan = configuration.CreatePlan(_publishingMode);
                    _session.Stop();
                    _session.Start(plan, new XRCycloneDdsTransport(_activeDomain, _configurationXml));
                    Logger.LogFile($"[XRInputDdsRouter] Message selection updated: {plan.Routes.Count} active topics.");
                }
                double now = Time.unscaledTimeAsDouble;
                if (now < _nextPublishTime) return;
                _nextPublishTime += _publishInterval;
                if (_nextPublishTime <= now) _nextPublishTime = now + _publishInterval;
                _session.Publish(DdsConfig.GetCurrentTimestampNs());
            }
            catch (Exception error) { Fail(error); }
        }

        private void Fail(Exception error)
        {
            // Publish may already have rolled back. Emit one stopped notification in either case.
            try { _session.Stop(); }
            catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            RecordError(error);
            PublishingChanged?.Invoke(false);
        }

        private void RecordError(Exception error)
        {
            LastError = error.ToString();
            Logger.LogFile($"[XRInputDdsRouter] {LastError}", LogType.Error);
        }

        private void OnDisable()
        {
            if (deviceState != null) deviceState.ModeChanged -= HandleModeChanged;
            if (configuration != null) configuration.MessagesChanged -= HandleMessagesChanged;
            if (_networkService != null) _networkService.OnInterfacesChanged -= HandleNetworkChanged;
            _networkService = null;
            StopPublishing();
        }

        private void HandleMessagesChanged() => _messagesDirty = IsPublishing;

        private void SyncNetworkSubscription()
        {
            var current = NetworkInfoService.Instance;
            if (_networkService == current) return;
            if (_networkService != null) _networkService.OnInterfacesChanged -= HandleNetworkChanged;
            _networkService = current;
            if (_networkService != null) _networkService.OnInterfacesChanged += HandleNetworkChanged;
            HandleNetworkChanged(0);
        }

        private void HandleNetworkChanged(int _)
        {
            if (!IsPublishing) return;
            var snapshot = _networkService != null ? _networkService.CurrentSnapshot : NetworkSnapshot.Empty;
            if (!snapshot.TryFindInterfaceById(_interfaceId, out _, out var nic, out var localIp) ||
                !nic.IsUp || nic.IsLoopback || localIp != _localIp)
                Fail(new InvalidOperationException("The DDS network interface disconnected or its IPv4 address changed."));
        }
    }
}
