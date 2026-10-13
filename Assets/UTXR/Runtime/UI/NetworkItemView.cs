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

        // Diagnostics: which real interface this row represents (independent of UI label text).
        public string BoundInterfaceId => _currentInterfaceId;
        private bool _localBindingsChecked;
        private bool _localBindingsValid;

        // Clone-time only: Unity does not remap references to objects OUTSIDE the cloned prefab.
        // Repair by exact child object name if possible; never write to a shared external label.
        public bool EnsureLocalBindings()
        {
            if (_localBindingsChecked) return _localBindingsValid;
            _localBindingsChecked = true;
            displayNameText = OwnChild(displayNameText, nameof(displayNameText));
            ipAddressText = OwnChild(ipAddressText, nameof(ipAddressText));
            linkSpeedText = OwnChild(linkSpeedText, nameof(linkSpeedText));
            rxTrafficText = OwnChild(rxTrafficText, nameof(rxTrafficText));
            txTrafficText = OwnChild(txTrafficText, nameof(txTrafficText));
            typeImage = OwnChild(typeImage, nameof(typeImage));
            linkSpeedImage = OwnChild(linkSpeedImage, nameof(linkSpeedImage));

            // Adopted scene rows can have old cached strings after references are fixed.
            _cachedDisplayName = _cachedIp = _cachedLinkSpeed = _cachedRx = _cachedTx = null;
            _cachedTypeSprite = _cachedSpeedSprite = null;
            if (displayNameText == null || ipAddressText == null)
            {
                UTXR.Core.Logger.LogApp($"[NetworkItemView] '{name}': row requires its OWN DisplayName and IP TMP children. " +
                               "Fix template Inspector references; refusing to render a misleading row.", LogType.Error);
                _localBindingsValid = false;
                return false;
            }
            if (rxTrafficText == null || txTrafficText == null)
                UTXR.Core.Logger.LogApp($"[NetworkItemView] '{name}': RX/TX TMP children not bound. " +
                                 "Assign the two text children on the networkItem TEMPLATE, not on a parent or sibling.", LogType.Warning);
            _localBindingsValid = true;
            return true;
        }

        private T OwnChild<T>(T current, string field) where T : Component
        {
            if (current == null) return null;
            if (current.transform == transform || current.transform.IsChildOf(transform)) return current;

            T found = null;
            foreach (var child in GetComponentsInChildren<T>(true))
            {
                if (child.name != current.name) continue;
                if (found != null) { found = null; break; } // Ambiguous: do not guess.
                found = child;
            }
            if (found != null)
            {
                UTXR.Core.Logger.LogApp($"[NetworkItemView] '{name}' repaired external '{field}' " +
                                 $"reference by exact child name '{found.name}'. Fix the template Inspector.", LogType.Warning);
                return found;
            }
            UTXR.Core.Logger.LogApp($"[NetworkItemView] '{name}' field '{field}' references " +
                           $"'{current.name}' OUTSIDE this row and no unique child match exists. " +
                           "Clearing unsafe reference; bind a local child in the template Inspector.", LogType.Error);
            return null;
        }

        public bool OwnsDisplayText => displayNameText != null &&
            (displayNameText.transform == transform || displayNameText.transform.IsChildOf(transform));
        public bool OwnsRxText => rxTrafficText != null &&
            (rxTrafficText.transform == transform || rxTrafficText.transform.IsChildOf(transform));
        public bool OwnsTxText => txTrafficText != null &&
            (txTrafficText.transform == transform || txTrafficText.transform.IsChildOf(transform));

        private string _currentInterfaceName;
        private string _currentInterfaceId;
        private bool _isInterfaceUp;

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
        public void Bind(in NetworkInterfaceInfo info, NetworkSnapshot snapshot, NetworkInfoUI uiContext, bool preferIPv6 = false)
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
                    bool excludeIpv4 = uiContext != null && uiContext.FilterIPv4;
                    bool excludeIpv6 = uiContext != null && uiContext.FilterIPv6;

                    // Show the requested address family first; never turn addresses into extra rows.
                    for (int pass = 0; pass < 2 && targetIp == "N/A"; pass++)
                    {
                        bool ipv6 = pass == 0 ? preferIPv6 : !preferIPv6;
                        if (ipv6 && excludeIpv6) continue;
                        if (!ipv6 && excludeIpv4) continue;

                        for (int a = 0; a < info.AddressCount; a++)
                        {
                            if (!snapshot.TryGetAddress(info.AddressStartIndex + a, out var addr)) continue;
                            if (ipv6 ? addr.IsIPv6 : addr.IsIPv4)
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

            // 6. Live traffic is separate from the topology snapshot.
            _currentInterfaceName = info.Name;
            _currentInterfaceId = info.Id;
            _isInterfaceUp = (info.IsUp || info.AddressCount > 0) && !info.IsLoopback;

            if (_isInterfaceUp && NetworkTrafficMonitor.IsEnabled)
            {
                TrafficUnitFormat format = uiContext != null ? uiContext.TrafficFormat : TrafficUnitFormat.Mbps;
                var service = uiContext != null ? uiContext.NetworkService : NetworkInfoService.Instance;

                string queryKey = !string.IsNullOrEmpty(_currentInterfaceId) ? _currentInterfaceId : _currentInterfaceName;
                double rxMbps = -1, txMbps = -1;
                bool available = service != null && !string.IsNullOrEmpty(queryKey) &&
                                 service.TryGetTraffic(queryKey, out rxMbps, out txMbps);
                string initialRx = available ? NetworkTrafficMonitor.FormatValue(rxMbps, format) : "N/A";
                string initialTx = available ? NetworkTrafficMonitor.FormatValue(txMbps, format) : "N/A";

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
            }
            else
            {
                ClearTraffic();
            }
        }

        /// <summary>
        /// Updates displayed RX and TX traffic throughput. Called centrally by NetworkInfoUI.
        /// </summary>
        public void UpdateTraffic(TrafficUnitFormat format = TrafficUnitFormat.Mbps)
        {
            UpdateTraffic(null, format);
        }

        /// <summary>
        /// Updates displayed RX and TX traffic throughput using live telemetry from NetworkInfoService.
        /// </summary>
        public void UpdateTraffic(NetworkInfoService service, TrafficUnitFormat format = TrafficUnitFormat.Mbps)
        {
            if (!_isInterfaceUp || (rxTrafficText == null && txTrafficText == null)) return;

            var netService = service != null ? service : NetworkInfoService.Instance;
            string queryKey = !string.IsNullOrEmpty(_currentInterfaceId) ? _currentInterfaceId : _currentInterfaceName;
            double rxMbps = -1, txMbps = -1;
            bool available = NetworkTrafficMonitor.IsEnabled && netService != null &&
                             !string.IsNullOrEmpty(queryKey) &&
                             netService.TryGetTraffic(queryKey, out rxMbps, out txMbps);

            if (rxTrafficText != null)
            {
                string targetRx = available ? NetworkTrafficMonitor.FormatValue(rxMbps, format) : "N/A";
                if (_cachedRx != targetRx)
                {
                    rxTrafficText.text = targetRx;
                    _cachedRx = targetRx;
                }
            }
            if (txTrafficText != null)
            {
                string targetTx = available ? NetworkTrafficMonitor.FormatValue(txMbps, format) : "N/A";
                if (_cachedTx != targetTx)
                {
                    txTrafficText.text = targetTx;
                    _cachedTx = targetTx;
                }
            }
        }

        /// <summary>
        /// Clears traffic text displays.
        /// </summary>
        public void ClearTraffic()
        {
            if (rxTrafficText != null) rxTrafficText.text = string.Empty;
            if (txTrafficText != null) txTrafficText.text = string.Empty;
            _cachedRx = string.Empty;
            _cachedTx = string.Empty;
        }

        private void OnDisable()
        {
            _cachedRx = null;
            _cachedTx = null;
        }

        /// <summary>
        /// Clears all displayed text and values to default empty state.
        /// </summary>
        public void Clear()
        {
            _currentInterfaceName = null;
            _currentInterfaceId = null;
            _isInterfaceUp = false;

            if (displayNameText != null) displayNameText.text = string.Empty;
            if (ipAddressText != null) ipAddressText.text = string.Empty;
            if (linkSpeedText != null) linkSpeedText.text = string.Empty;
            ClearTraffic();
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
