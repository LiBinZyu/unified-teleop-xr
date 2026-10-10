using System;
using Network.InterfaceInfo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UTXR.UI
{
    /// <summary>
    /// Component holding field references and presentation logic for a single network interface item.
    /// Can be used as a template for list instantiations or bound directly to a fixed UI element.
    /// Manages real-time RX/TX traffic monitoring independently from cold-path interface discovery.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkItemView : MonoBehaviour
    {
        [Header("UI Component Bindings")]
        [Tooltip("Image displaying interface type icon (WiFi, Ethernet, etc.)")]
        public Image typeImage;

        [Tooltip("TMP text displaying SSID or interface name")]
        public TextMeshProUGUI displayNameText;

        [Tooltip("TMP text displaying interface IP address(es)")]
        public TextMeshProUGUI ipAddressText;

        [Tooltip("Image displaying link speed tier icon (Good, Fair, Poor, Offline)")]
        public Image linkSpeedImage;

        [Tooltip("Optional TMP text displaying numerical link speed (leave null to use icon only)")]
        public TextMeshProUGUI linkSpeedText;

        [Tooltip("TMP text displaying separate RX throughput (optional)")]
        public TextMeshProUGUI rxTrafficText;

        [Tooltip("TMP text displaying separate TX throughput (optional)")]
        public TextMeshProUGUI txTrafficText;


        private readonly NetworkTrafficMonitor.Sampler _trafficTracker = new NetworkTrafficMonitor.Sampler();
        private string _currentInterfaceName;
        private string _currentInterfaceId;
        private bool _isInterfaceUp;
        private float _lastTrafficUpdateTime;

        private string _cachedDisplayName;
        private string _cachedIp;
        private string _cachedLinkSpeed;
        private string _cachedRx;
        private string _cachedTx;
        private Sprite _cachedTypeSprite;
        private Sprite _cachedSpeedSprite;

        /// <summary>
        /// Zero-GC update of UI elements directly from the NetworkInterfaceInfo struct and snapshot.
        /// </summary>
        public void Bind(in NetworkInterfaceInfo info, NetworkSnapshot snapshot, NetworkInfoUI uiContext)
        {
            // 1. Type Icon (uses offlineIcon if status is down)
            if (typeImage != null && uiContext != null)
            {
                Sprite targetSprite = info.Status == NetworkStatus.Up
                    ? uiContext.GetIconForType(info.Type)
                    : (uiContext.offlineIcon != null ? uiContext.offlineIcon : uiContext.GetIconForType(info.Type));

                if (_cachedTypeSprite != targetSprite)
                {
                    typeImage.sprite = targetSprite;
                    typeImage.enabled = targetSprite != null;
                    _cachedTypeSprite = targetSprite;
                }
            }

            // 2. Display Name
            if (displayNameText != null)
            {
                string targetName = !string.IsNullOrEmpty(info.DisplayName) ? info.DisplayName : "N/A";
                if (_cachedDisplayName != targetName)
                {
                    displayNameText.text = targetName;
                    _cachedDisplayName = targetName;
                }
            }

            // 3. IP Address from Snapshot
            if (ipAddressText != null)
            {
                string targetIp = "N/A";
                if (snapshot != null && info.AddressCount > 0)
                {
                    // Find first IPv4 address
                    for (int a = 0; a < info.AddressCount; a++)
                    {
                        if (snapshot.TryGetAddress(info.AddressStartIndex + a, out var addr) && addr.IsIPv4)
                        {
                            targetIp = addr.Address;
                            break;
                        }
                    }

                    // Fallback to IPv6 if no IPv4
                    if (targetIp == "N/A")
                    {
                        for (int a = 0; a < info.AddressCount; a++)
                        {
                            if (snapshot.TryGetAddress(info.AddressStartIndex + a, out var addr) && addr.IsIPv6)
                            {
                                targetIp = addr.Address;
                                break;
                            }
                        }
                    }
                }

                if (_cachedIp != targetIp)
                {
                    ipAddressText.text = targetIp;
                    _cachedIp = targetIp;
                }
            }

            // 4. Link Speed Tier Icon
            if (linkSpeedImage != null && uiContext != null)
            {
                long? speed = info.LinkSpeedMbps > 0 ? info.LinkSpeedMbps : (long?)null;
                Sprite speedSprite = uiContext.GetIconForLinkSpeed(speed);
                if (_cachedSpeedSprite != speedSprite)
                {
                    linkSpeedImage.sprite = speedSprite;
                    linkSpeedImage.enabled = speedSprite != null;
                    _cachedSpeedSprite = speedSprite;
                }
            }

            // 5. Link Speed Text
            if (linkSpeedText != null)
            {
                string targetLink = info.LinkSpeedMbps > 0
                    ? $"{info.LinkSpeedMbps} Mbps"
                    : string.Empty;

                if (_cachedLinkSpeed != targetLink)
                {
                    linkSpeedText.text = targetLink;
                    _cachedLinkSpeed = targetLink;
                }
            }

            // 6. Traffic text initialization (independent real-time monitoring)
            _currentInterfaceName = info.Name;
            _currentInterfaceId = info.Id;
            _isInterfaceUp = (info.Status == NetworkStatus.Up);

            if (_isInterfaceUp && NetworkTrafficMonitor.IsEnabled)
            {
                TrafficUnitFormat format = uiContext != null ? uiContext.TrafficFormat : TrafficUnitFormat.Mbps;
                string initialRx = NetworkTrafficMonitor.FormatValue(0.0, 0.0, format);
                string initialTx = NetworkTrafficMonitor.FormatValue(0.0, 0.0, format);

                if (rxTrafficText != null)
                {
                    rxTrafficText.text = initialRx;
                    _cachedRx = initialRx;
                }

                if (txTrafficText != null)
                {
                    txTrafficText.text = initialTx;
                    _cachedTx = initialTx;
                }

                _trafficTracker.Reset();
                _trafficTracker.UpdateSample(_currentInterfaceName, _currentInterfaceId);
                _lastTrafficUpdateTime = Time.realtimeSinceStartup;
            }
            else
            {
                if (rxTrafficText != null)
                {
                    rxTrafficText.text = string.Empty;
                    _cachedRx = string.Empty;
                }
                if (txTrafficText != null)
                {
                    txTrafficText.text = string.Empty;
                    _cachedTx = string.Empty;
                }
            }
        }

        /// <summary>
        /// Updates displayed RX and TX traffic throughput. Called centrally by NetworkInfoUI.
        /// </summary>
        public void UpdateTraffic(TrafficUnitFormat format = TrafficUnitFormat.Mbps)
        {
            if (!_isInterfaceUp || (rxTrafficText == null && txTrafficText == null))
                return;

            _trafficTracker.UpdateSample(_currentInterfaceName, _currentInterfaceId);

            if (rxTrafficText != null)
            {
                string targetRx = _trafficTracker.GetFormattedRx(format);
                if (_cachedRx != targetRx)
                {
                    rxTrafficText.text = targetRx;
                    _cachedRx = targetRx;
                }
            }

            if (txTrafficText != null)
            {
                string targetTx = _trafficTracker.GetFormattedTx(format);
                if (_cachedTx != targetTx)
                {
                    txTrafficText.text = targetTx;
                    _cachedTx = targetTx;
                }
            }
        }

        /// <summary>
        /// Clears traffic text displays and resets tracking baseline.
        /// </summary>
        public void ClearTraffic()
        {
            _trafficTracker.Reset();
            if (rxTrafficText != null) rxTrafficText.text = string.Empty;
            if (txTrafficText != null) txTrafficText.text = string.Empty;
            _cachedRx = string.Empty;
            _cachedTx = string.Empty;
        }

        private void OnDisable()
        {
            _trafficTracker.Reset();
        }

        /// <summary>
        /// Clears all displayed text and values to default empty state.
        /// </summary>
        public void Clear()
        {
            _currentInterfaceName = null;
            _currentInterfaceId = null;
            _isInterfaceUp = false;
            _trafficTracker.Reset();

            if (displayNameText != null) displayNameText.text = string.Empty;
            if (ipAddressText != null) ipAddressText.text = string.Empty;
            if (linkSpeedText != null) linkSpeedText.text = string.Empty;
            if (rxTrafficText != null) rxTrafficText.text = string.Empty;
            if (txTrafficText != null) txTrafficText.text = string.Empty;
            if (typeImage != null) typeImage.enabled = false;
            if (linkSpeedImage != null) linkSpeedImage.enabled = false;

            _cachedDisplayName = null;
            _cachedIp = null;
            _cachedLinkSpeed = null;
            _cachedRx = null;
            _cachedTx = null;
            _cachedTypeSprite = null;
            _cachedSpeedSprite = null;
        }
    }
}
