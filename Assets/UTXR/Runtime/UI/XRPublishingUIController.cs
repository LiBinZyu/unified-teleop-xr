using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using CycloneDDS;
using Network.InterfaceInfo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UTXR.Prefs;
using UTXR.Publishing;
using Logger = UTXR.Core.Logger;

namespace UTXR.UI
{
    /// <summary>Explicit publishing controls. Does not select input modes or own the DDS runtime.</summary>
    [DisallowMultipleComponent]
    public sealed class XRPublishingUIController : MonoBehaviour
    {
        public XRInputDdsRouter router;
        public Toggle serviceToggle;
        public TMP_InputField targetIpInputField;
        public TMP_Dropdown interfaceDropdown;
        [Tooltip("Optional. Call RefreshPeers from a scan button; entry 0 keeps the typed target.")]
        public TMP_Dropdown ipDropdown;
        public TMP_Text selfIpText;
        public TMP_Text statusText;

        private const string TargetKey = "Dds_TargetIp";
        private const string InterfaceKey = "Dds_InterfaceId";
        private readonly List<string> _peers = new();
        private string _interfaceId, _error;
        private bool _initialized, _scanning;
        private bool _ipInteractable, _interfaceInteractable, _peersInteractable;
        private int _generation;

        private void OnEnable()
        {
            try
            {
                if (router == null || serviceToggle == null || targetIpInputField == null || interfaceDropdown == null)
                    throw new InvalidOperationException("Assign Router, Service Toggle, Target IP Input Field and Interface Dropdown.");
                if (ipDropdown == interfaceDropdown)
                    throw new InvalidOperationException("Target and interface dropdowns must be distinct.");
                if (!_initialized)
                {
                    _ipInteractable = targetIpInputField.interactable;
                    _interfaceInteractable = interfaceDropdown.interactable;
                    _peersInteractable = ipDropdown != null && ipDropdown.interactable;
                    targetIpInputField.SetTextWithoutNotify(PlayerPrefsManager.GetString(TargetKey, targetIpInputField.text));
                    _initialized = true;
                }
                router.PublishingChanged += HandlePublishingChanged;
                serviceToggle.onValueChanged.AddListener(SetPublishing);
                targetIpInputField.onEndEdit.AddListener(CommitTarget);
                if (ipDropdown != null) ipDropdown.onValueChanged.AddListener(SelectPeer);
                RefreshNetworkInterfaces();
                RebuildPeers();
                Synchronize(); // Reflect the router; never restore a saved "running" flag.
            }
            catch (Exception error) { Report(error); enabled = false; }
        }

        public void SetPublishing(bool publish)
        {
            bool calledRouter = false;
            try
            {
                _error = null;
                if (publish)
                {
                    if (router.IsPublishing) throw new InvalidOperationException("Publishing is already running.");
                    string target = ValidateAddress(targetIpInputField.text);
                    var binding = interfaceDropdown.GetComponent<NetworkInterfacePrefBridge>();
                    if (binding == null || !binding.TryGetSelection(out var nic, out string local))
                        throw new InvalidOperationException("The selected network interface is unavailable.");
                    string xml = DdsConfig.BuildCycloneDdsXml(local, new[] { target },
                        bindMatchingInterface: true, allowMulticast: true, autodetermine: false);
                    PlayerPrefsManager.SetString(TargetKey, target);
                    calledRouter = true;
                    router.StartPublishing(router.DomainId, xml, nic.Id);
                }
                else { calledRouter = true; router.StopPublishing(); }
            }
            catch (Exception error)
            {
                _error = error.Message;
                if (!calledRouter) Logger.LogFile($"[XRPublishingUI] {error}", LogType.Error);
            }
            finally { Synchronize(); }
        }

        public void StartPublishing() => SetPublishing(true);
        public void StopPublishing() => SetPublishing(false);

        public void RefreshNetworkInterfaces()
        {
            try
            {
                NetworkInterfacePrefBridge.BindDropdown(interfaceDropdown, InterfaceKey, _interfaceId,
                    HandleInterfaceChanged, targetIpInputField.text);
            }
            catch (Exception error) { Report(error); }
        }

        private void HandleInterfaceChanged(NetworkInterfaceInfo info, string localIp)
        {
            _interfaceId = info.Id;
            if (selfIpText != null) selfIpText.text = localIp ?? "No active network interface";
        }

        private void CommitTarget(string value)
        {
            try { RequireStopped(); PlayerPrefsManager.SetString(TargetKey, ValidateAddress(value)); RefreshNetworkInterfaces(); _error = null; Synchronize(); }
            catch (Exception error) { Report(error); }
        }

        public async void RefreshPeers()
        {
            int generation = _generation;
            bool ownsScan = false;
            try
            {
                RequireStopped();
                if (ipDropdown == null) throw new InvalidOperationException("Assign the optional IP Dropdown before scanning peers.");
                if (_scanning) throw new InvalidOperationException("A peer scan is already running.");
                _scanning = true;
                ownsScan = true;
                var peers = await DdsNetworkHelper.ProbeActiveSubnetIpsAsync();
                if (generation != _generation || !isActiveAndEnabled || router.IsPublishing) return;
                _peers.Clear(); _peers.AddRange(peers); RebuildPeers();
            }
            catch (Exception error) { if (generation == _generation && isActiveAndEnabled) Report(error); }
            finally { if (ownsScan && generation == _generation) _scanning = false; }
        }

        private void RebuildPeers()
        {
            if (ipDropdown == null) return;
            var labels = new List<string> { "Use typed target IP" }; labels.AddRange(_peers);
            ipDropdown.ClearOptions(); ipDropdown.AddOptions(labels); ipDropdown.SetValueWithoutNotify(0);
        }

        private void SelectPeer(int index)
        {
            try
            {
                RequireStopped();
                if (index == 0) return;
                if (index < 1 || index > _peers.Count) throw new InvalidOperationException("Invalid peer selection.");
                string target = ValidateAddress(_peers[index - 1]);
                targetIpInputField.SetTextWithoutNotify(target); CommitTarget(target);
            }
            catch (Exception error) { Report(error); }
        }

        public static string ValidateAddress(string text)
        {
            string value = text?.Trim();
            if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
                ip.GetAddressBytes()[0] == 0 || ip.Equals(IPAddress.Broadcast) || ip.GetAddressBytes()[0] >= 224)
                throw new ArgumentException("Enter a unicast IPv4 target address.");
            return ip.ToString();
        }

        private void RequireStopped()
        {
            if (router.IsPublishing) throw new InvalidOperationException("Stop publishing before changing network settings.");
        }

        private void HandlePublishingChanged(bool _)
        {
            _error = string.IsNullOrEmpty(router.LastError) ? null : router.LastError.Split('\n')[0].Trim();
            Synchronize();
        }
        private void Synchronize()
        {
            if (!_initialized || router == null || serviceToggle == null || targetIpInputField == null || interfaceDropdown == null) return;
            bool running = router.IsPublishing;
            ToggleImageHelper.SetIsOnWithoutNotify(serviceToggle, running);
            targetIpInputField.interactable = !running && _ipInteractable;
            interfaceDropdown.interactable = !running && _interfaceInteractable;
            if (ipDropdown != null) ipDropdown.interactable = !running && _peersInteractable;
            if (statusText != null) statusText.text = !string.IsNullOrEmpty(_error) ? $"Error: {_error}" : running ? $"Publishing: {router.PublishingMode}" : "Stopped";
        }

        private void Report(Exception error)
        {
            _error = error.Message; Logger.LogFile($"[XRPublishingUI] {error}", LogType.Error); Synchronize();
        }

        private void OnDisable()
        {
            ++_generation; _scanning = false;
            if (router != null) router.PublishingChanged -= HandlePublishingChanged;
            if (serviceToggle != null) serviceToggle.onValueChanged.RemoveListener(SetPublishing);
            if (targetIpInputField != null) targetIpInputField.onEndEdit.RemoveListener(CommitTarget);
            NetworkInterfacePrefBridge.UnbindDropdown(interfaceDropdown);
            if (ipDropdown != null) ipDropdown.onValueChanged.RemoveListener(SelectPeer);
        }
    }
}
