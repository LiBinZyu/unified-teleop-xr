using System;
using System.Collections.Generic;
using Network.InterfaceInfo;
using UnityEngine;
using UnityEngine.Serialization;

namespace UTXR.UI
{
    /// <summary>
    /// UI binding controller responsible for presenting network interfaces.
    /// Supports:
    /// 1. Direct binding: Updates a fixed NetworkItemView with the primary active network interface.
    /// 2. Template list binding: Spawns sibling NetworkItemView items next to networkItem.
    /// Both modes can coexist or operate independently.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkInfoUI : MonoBehaviour
    {
        [Tooltip("Optional reference to NetworkInfoService. If unassigned, automatically resolved via Instance.")]
        [SerializeField] private NetworkInfoService networkService;

        [Tooltip("Displays the highest-priority/top-scored active interface (e.g. status bar). Formerly directBinding.")]
        [FormerlySerializedAs("directBinding")]
        [SerializeField] private NetworkItemView primaryNetworkItem;

        [Tooltip("Template item. When assigned, multiple items will be dynamically generated as its siblings under the same parent.")]
        [SerializeField] private NetworkItemView networkItem;

        [Header("Interface Type Icons")]
        [Tooltip("Icon displayed when offline / disconnected")]
        public Sprite offlineIcon;
        public Sprite wifiIcon;
        public Sprite ethernetIcon;
        public Sprite cellularIcon;
        public Sprite vpnIcon;
        public Sprite bluetoothIcon;
        public Sprite otherIcon;

        [Header("Link Speed Tier Icons")]
        [Tooltip("Icon displayed when link speed is offline or unknown")]
        public Sprite speedOfflineIcon;

        [Tooltip("Icon displayed when link speed is low (< 100 Mbps)")]
        public Sprite speedLowIcon;

        [Tooltip("Icon displayed when link speed is medium (100 - 500 Mbps)")]
        public Sprite speedMediumIcon;

        [Tooltip("Icon displayed when link speed is high (>= 500 Mbps)")]
        public Sprite speedHighIcon;

        [Header("Interface Filtering Options")]
        [Tooltip("Filter out loopback interfaces from the sibling list.")]
        [SerializeField] private bool filterLoopback = true;

        [Tooltip("Filter out down/disconnected interfaces from the sibling list.")]
        [SerializeField] private bool filterDisconnected = true;

        [Tooltip("Filter out virtual hypervisor/switch interfaces (Hyper-V, VMware). Does NOT filter VPN.")]
        [SerializeField] private bool filterVirtual = false;

        [Tooltip("Filter out VPN interfaces from the sibling list.")]
        [SerializeField] private bool filterVpn = false;

        public TrafficUnitFormat TrafficFormat => networkService != null ? networkService.TrafficFormat : TrafficUnitFormat.Mbps;

        private long mediumSpeedThreshold = 100;
        private long highSpeedThreshold = 500;
        private float _lastTrafficUpdateTime;

        private readonly List<NetworkItemView> _itemPool = new List<NetworkItemView>();

        private void Awake()
        {
            if (networkItem != null)
            {
                // Template is disabled initially so it serves purely as a clone source
                networkItem.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (networkService == null)
            {
                networkService = NetworkInfoService.Instance;
            }

            if (networkService != null)
            {
                networkService.OnInterfacesChanged += HandleInterfacesChanged;
                // Render current snapshot state immediately
                RenderSnapshot(networkService.CurrentSnapshot);
            }

            NetworkTrafficMonitor.OnMonitoringToggled += HandleTrafficMonitoringToggled;
            _lastTrafficUpdateTime = Time.realtimeSinceStartup;
        }

        private void OnDisable()
        {
            if (networkService != null)
            {
                networkService.OnInterfacesChanged -= HandleInterfacesChanged;
            }

            NetworkTrafficMonitor.OnMonitoringToggled -= HandleTrafficMonitoringToggled;
        }

        private void Update()
        {
            if (!NetworkTrafficMonitor.IsEnabled) return;

            float interval = networkService != null ? networkService.TrafficUpdateInterval : 1.0f;
            float now = Time.realtimeSinceStartup;
            if (now - _lastTrafficUpdateTime >= interval)
            {
                _lastTrafficUpdateTime = now;
                var format = networkService != null ? networkService.TrafficFormat : TrafficUnitFormat.Mbps;
                UpdateAllTraffic(format);
            }
        }

        private void UpdateAllTraffic(TrafficUnitFormat format)
        {
            if (primaryNetworkItem != null && primaryNetworkItem.gameObject.activeInHierarchy)
            {
                primaryNetworkItem.UpdateTraffic(format);
            }

            for (int i = 0; i < _itemPool.Count; i++)
            {
                var item = _itemPool[i];
                if (item != null && item.gameObject.activeInHierarchy)
                {
                    item.UpdateTraffic(format);
                }
            }
        }

        private void HandleTrafficMonitoringToggled(bool enabled)
        {
            if (enabled)
            {
                _lastTrafficUpdateTime = Time.realtimeSinceStartup;
                var format = networkService != null ? networkService.TrafficFormat : TrafficUnitFormat.Mbps;
                UpdateAllTraffic(format);
            }
            else
            {
                if (primaryNetworkItem != null) primaryNetworkItem.ClearTraffic();
                for (int i = 0; i < _itemPool.Count; i++)
                {
                    var item = _itemPool[i];
                    if (item != null) item.ClearTraffic();
                }
            }
        }

        private void HandleInterfacesChanged(int version)
        {
            if (networkService != null)
            {
                RenderSnapshot(networkService.CurrentSnapshot);
            }
        }

        private void RenderSnapshot(NetworkSnapshot snapshot)
        {
            if (snapshot == null) return;

            // 1. Primary interface binding (Top-scored active/Up interface, fallback to default route)
            if (primaryNetworkItem != null)
            {
                if (snapshot.InterfaceCount > 0 && snapshot.TryGetInterface(0, out var topScoreIface) && topScoreIface.IsUp)
                {
                    primaryNetworkItem.Bind(in topScoreIface, snapshot, this);
                }
                else if (snapshot.TryGetDefaultInterface(out int defaultIdx) && snapshot.TryGetInterface(defaultIdx, out var defIface) && defIface.IsUp)
                {
                    primaryNetworkItem.Bind(in defIface, snapshot, this);
                }
                else
                {
                    // If no Up interface is available, clear primary binding
                    primaryNetworkItem.Clear();
                }
            }

            // 2. Dynamic sibling list update
            if (networkItem != null)
            {
                RenderSiblingList(snapshot);
            }
        }

        private void RenderSiblingList(NetworkSnapshot snapshot)
        {
            Transform parentTransform = networkItem.transform.parent;
            int targetItemIdx = 0;

            for (int i = 0; i < snapshot.InterfaceCount; i++)
            {
                if (snapshot.TryGetInterface(i, out var iface))
                {
                    if (filterLoopback && iface.IsLoopback) continue;
                    if (filterDisconnected && !iface.IsUp) continue;
                    if (filterVirtual && iface.IsVirtual) continue;
                    if (filterVpn && iface.IsVpn) continue;

                    while (targetItemIdx >= _itemPool.Count)
                    {
                        var clone = Instantiate(networkItem, parentTransform);
                        _itemPool.Add(clone);
                    }

                    var itemView = _itemPool[targetItemIdx++];
                    itemView.gameObject.SetActive(true);
                    itemView.Bind(in iface, snapshot, this);
                }
            }

            // Deactivate surplus pooled items
            for (int i = targetItemIdx; i < _itemPool.Count; i++)
            {
                _itemPool[i].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Maps NetworkType enum to configured UI icons.
        /// </summary>
        public Sprite GetIconForType(NetworkType type)
        {
            return type switch
            {
                NetworkType.WiFi => wifiIcon,
                NetworkType.Ethernet => ethernetIcon,
                NetworkType.Cellular => cellularIcon,
                NetworkType.VPN => vpnIcon,
                NetworkType.Bluetooth => bluetoothIcon,
                _ => otherIcon
            };
        }

        /// <summary>
        /// Evaluates link speed (Mbps) into tier icons (High, Medium, Low, Offline).
        /// </summary>
        public Sprite GetIconForLinkSpeed(long? speedMbps)
        {
            if (!speedMbps.HasValue || speedMbps.Value <= 0)
            {
                return speedOfflineIcon;
            }

            long speed = speedMbps.Value;
            if (speed >= highSpeedThreshold)
            {
                return speedHighIcon;
            }
            if (speed >= mediumSpeedThreshold)
            {
                return speedMediumIcon;
            }
            return speedLowIcon;
        }
    }
}
