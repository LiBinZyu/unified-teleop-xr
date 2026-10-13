using System;
using System.Collections.Generic;
using Network.InterfaceInfo;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UTXR.Core;
using Logger = UTXR.Core.Logger;

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

        [Header("Interface Filters")]
        [Tooltip("Hide loopback (lo / 127.0.0.1). Disable to show every OS interface.")]
        [SerializeField] private bool filterLoopback = true;

        [Tooltip("Hide down/disconnected interfaces.")]
        [SerializeField] private bool filterDisconnected = false;

        [Tooltip("Hide virtual interfaces (including dummy0). VPN is separate.")]
        [SerializeField] private bool filterVirtual = false;

        [Tooltip("Hide VPN interfaces.")]
        [SerializeField] private bool filterVpn = false;

        [Tooltip("Filter out IPv4 interfaces/addresses from UI. When ON, IPv4 is excluded.")]
        [SerializeField] private bool filterIPv4 = false;

        [Tooltip("Filter out IPv6 interfaces/addresses from UI. When ON, IPv6 is excluded.")]
        [SerializeField] private bool filterIPv6 = true;

        public bool FilterIPv4 => filterIPv4;
        public bool FilterIPv6 => filterIPv6;

        public NetworkInfoService NetworkService => networkService != null ? networkService : NetworkInfoService.Instance;
        public TrafficUnitFormat TrafficFormat => NetworkService != null ? NetworkService.TrafficFormat : TrafficUnitFormat.Mbps;

        private long mediumSpeedThreshold = 100;
        private long highSpeedThreshold = 500;
        private float _lastTrafficUpdateTime;
        private NetworkInfoService _subscribedService;

        private readonly List<NetworkItemView> _itemPool = new List<NetworkItemView>();
        private readonly HashSet<NetworkItemView> _runtimeClones = new HashSet<NetworkItemView>();
        private static readonly Dictionary<Transform, NetworkInfoUI> ListOwners =
            new Dictionary<Transform, NetworkInfoUI>();
        private Transform _ownedListRoot;
        // TMP_Dropdown owns its item clones. Never instantiate NetworkItemView rows inside its Template.
        private TMP_Dropdown _dropdown;
        private readonly List<string> _dropdownIds = new List<string>();
        private readonly List<string> _nextDropdownIds = new List<string>();
        private readonly List<string> _nextDropdownTexts = new List<string>();

        private void Awake()
        {
            if (networkItem != null)
            {
                // For a native TMP_Dropdown, Item must stay active inside its inactive Template.
                // Disabling Item breaks TMP_Dropdown.SetupTemplate() (cannot discover its Toggle).
                networkItem.gameObject.SetActive(
                    networkItem.GetComponentInParent<TMP_Dropdown>(true) != null);
                if (networkItem.rxTrafficText == null || networkItem.txTrafficText == null)
                    Logger.LogApp($"[NetworkInfoUI] RX/TX TMP references not assigned on template '{networkItem.name}'.", LogType.Warning);
                // Every cloned row must refer to its own child labels, not a shared text elsewhere.
                CheckLabel(networkItem.displayNameText, "DisplayName");
                CheckLabel(networkItem.ipAddressText, "IP");
                CheckLabel(networkItem.rxTrafficText, "RX");
                CheckLabel(networkItem.txTrafficText, "TX");
            }
        }

        private void CheckLabel(TMPro.TextMeshProUGUI label, string field)
        {
            if (label != null && !label.transform.IsChildOf(networkItem.transform))
                Logger.LogApp($"[NetworkInfoUI] {field} TMP on template '{networkItem.name}' " +
                              "is outside this item. Clones would all write to the same label.", LogType.Warning);
        }

        private void OnEnable()
        {
            Logger.LogApp($"[NetDiag][UI] OnEnable on '{name}' (InstanceID={gameObject.GetInstanceID()})");
            // Exactly one active controller owns a list root.
            if (networkItem != null && networkItem.transform.parent != null)
            {
                var root = networkItem.transform.parent;
                Logger.LogApp($"[NetDiag][UI] networkItem template='{networkItem.name}' (InstanceID={networkItem.gameObject.GetInstanceID()}), root parent='{root.name}' (childCount={root.childCount})");
                for (int c = 0; c < root.childCount; c++)
                {
                    var ch = root.GetChild(c);
                    Logger.LogApp($"[NetDiag][UI]   Initial root.Child[{c}]: '{ch.name}' (active={ch.gameObject.activeSelf}, InstanceID={ch.gameObject.GetInstanceID()}, hasItemView={ch.GetComponent<NetworkItemView>() != null})");
                }

                var dropdown = networkItem.GetComponentInParent<TMPro.TMP_Dropdown>(true);
                if (dropdown != null)
                {
                    _dropdown = dropdown;
                    // TMP_Dropdown owns its template and must find an ACTIVE Toggle item in it.
                    // The Template parent itself remains inactive except when the dropdown opens.
                    if (dropdown.template == null || !networkItem.transform.IsChildOf(dropdown.template))
                        Logger.LogApp($"[NetworkInfoUI] '{dropdown.name}': networkItem must be under Dropdown.Template.", LogType.Error);
                    if (networkItem.GetComponent<UnityEngine.UI.Toggle>() == null)
                        Logger.LogApp($"[NetworkInfoUI] '{dropdown.name}': add a Toggle component to networkItem '{networkItem.name}' in the prefab.", LogType.Error);
                    if (dropdown.itemText == null && networkItem.displayNameText != null)
                        dropdown.itemText = networkItem.displayNameText;
                    Logger.LogApp($"[NetworkInfoUI] '{dropdown.name}' uses native TMP_Dropdown options; manual row cloning disabled.");
                }

                if (_dropdown == null && ListOwners.TryGetValue(root, out var owner) && owner != null && owner != this)
                    Logger.LogApp($"[NetworkInfoUI] Duplicate list controllers: '{owner.name}' and '{name}' share '{root.name}'.", LogType.Error);
                else if (_dropdown == null)
                {
                    ListOwners[root] = this;
                    _ownedListRoot = root;
                    // Claim/reuse old serialized rows before creating any prefab clones.
                    AdoptSiblingRows();
                    if (primaryNetworkItem != null && primaryNetworkItem.transform.parent == root)
                        Logger.LogApp("[NetworkInfoUI] primaryNetworkItem is inside the dynamic list root. " +
                                      "It is an extra visible row; move it to a separate status bar.", LogType.Warning);
                }
            }
            SyncSubscription();
            NetworkTrafficMonitor.OnMonitoringToggled += HandleTrafficMonitoringToggled;
            _lastTrafficUpdateTime = Time.realtimeSinceStartup;
        }

        private void OnDisable()
        {
            if (!ReferenceEquals(_ownedListRoot, null) &&
                ListOwners.TryGetValue(_ownedListRoot, out var owner) && owner == this)
                ListOwners.Remove(_ownedListRoot);
            _ownedListRoot = null;
            _dropdown = null;
            _dropdownIds.Clear();
            if (_subscribedService != null)
                _subscribedService.OnInterfacesChanged -= HandleInterfacesChanged;
            _subscribedService = null;
            NetworkTrafficMonitor.OnMonitoringToggled -= HandleTrafficMonitoringToggled;
            foreach (var item in _itemPool)
                if (item != null) item.gameObject.SetActive(false);
        }

        private void SyncSubscription()
        {
            var current = NetworkService;
            if (_subscribedService == current) return;
            if (_subscribedService != null)
                _subscribedService.OnInterfacesChanged -= HandleInterfacesChanged;
            _subscribedService = current;
            if (_subscribedService != null)
            {
                _subscribedService.OnInterfacesChanged += HandleInterfacesChanged;
                RenderSnapshot(_subscribedService.CurrentSnapshot);
            }
        }

        private void OnDestroy()
        {
            foreach (var item in _runtimeClones)
                if (item != null) Destroy(item.gameObject);
            _runtimeClones.Clear();
            _itemPool.Clear();
        }

        private void Update()
        {
            // Supports service creation/replacement after this view becomes active.
            if (_subscribedService != NetworkService) SyncSubscription();
            if (!NetworkTrafficMonitor.IsEnabled) return;

            float interval = NetworkService != null ? NetworkService.TrafficUpdateInterval : 1.0f;
            float now = Time.realtimeSinceStartup;
            if (now - _lastTrafficUpdateTime >= interval)
            {
                _lastTrafficUpdateTime = now;
                var format = TrafficFormat;
                UpdateAllTraffic(format);
                if (_dropdown != null) RefreshDropdown(NetworkService != null ? NetworkService.CurrentSnapshot : NetworkSnapshot.Empty);
            }
        }

        private void UpdateAllTraffic(TrafficUnitFormat format)
        {
            var service = NetworkService;

            if (primaryNetworkItem != null && primaryNetworkItem.gameObject.activeInHierarchy)
            {
                primaryNetworkItem.UpdateTraffic(service, format);
            }

            for (int i = 0; i < _itemPool.Count; i++)
            {
                var item = _itemPool[i];
                if (item != null && item.gameObject.activeInHierarchy)
                {
                    item.UpdateTraffic(service, format);
                }
            }
        }

        private void HandleTrafficMonitoringToggled(bool enabled)
        {
            if (enabled)
            {
                _lastTrafficUpdateTime = Time.realtimeSinceStartup;
                var format = TrafficFormat;
                UpdateAllTraffic(format);
                if (_dropdown != null) RefreshDropdown(NetworkService != null ? NetworkService.CurrentSnapshot : NetworkSnapshot.Empty);
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
            if (_subscribedService != null)
                RenderSnapshot(_subscribedService.CurrentSnapshot);
        }

        // Filter only once per interface, never once per address.
        // All filters follow pure "Filter Out" semantics: checked (true) = exclude / hide.
        private bool MatchesFilters(NetworkSnapshot snapshot, int index, in NetworkInterfaceInfo iface)
        {
            if (filterLoopback && iface.IsLoopback)
            {
                Logger.LogApp($"[NetDiag][UI] Filter REJECTED NIC[{index}] '{iface.Name}': loopback filtered out");
                return false;
            }
            if (filterDisconnected && !iface.IsUp)
            {
                Logger.LogApp($"[NetDiag][UI] Filter REJECTED NIC[{index}] '{iface.Name}': disconnected filtered out (!IsUp, status={iface.Status})");
                return false;
            }
            if (filterVirtual && iface.IsVirtual)
            {
                Logger.LogApp($"[NetDiag][UI] Filter REJECTED NIC[{index}] '{iface.Name}': virtual filtered out");
                return false;
            }
            if (filterVpn && iface.IsVpn)
            {
                Logger.LogApp($"[NetDiag][UI] Filter REJECTED NIC[{index}] '{iface.Name}': VPN filtered out");
                return false;
            }

            // Both IPv4 and IPv6 filtered out -> reject all IP-bearing interfaces
            if (filterIPv4 && filterIPv6)
            {
                Logger.LogApp($"[NetDiag][UI] Filter REJECTED NIC[{index}] '{iface.Name}': both IPv4 and IPv6 filtered out");
                return false;
            }

            // Count usable addresses after applying filterIPv4 and filterIPv6
            int usableIpv4 = 0;
            int usableIpv6 = 0;

            for (int a = 0; a < iface.AddressCount; a++)
            {
                if (!snapshot.TryGetAddress(index, a, out var address)) continue;
                if (address.IsIPv4 && !filterIPv4) usableIpv4++;
                if (address.IsIPv6 && !filterIPv6) usableIpv6++;
            }

            // If the interface has addresses, but all available addresses were filtered out by filterIPv4 / filterIPv6
            if (iface.AddressCount > 0 && usableIpv4 == 0 && usableIpv6 == 0)
            {
                Logger.LogApp($"[NetDiag][UI] Filter REJECTED NIC[{index}] '{iface.Name}': all {iface.AddressCount} addresses filtered out by IP protocol filters (filterIPv4={filterIPv4}, filterIPv6={filterIPv6})");
                return false;
            }

            Logger.LogApp($"[NetDiag][UI] Filter ACCEPTED NIC[{index}] '{iface.Name}' (usableIPv4={usableIpv4}, usableIPv6={usableIpv6})");
            return true;
        }

        private void RenderSnapshot(NetworkSnapshot snapshot)
        {
            if (snapshot == null) return;
            Logger.LogApp($"[NetDiag][UI] RenderSnapshot v{snapshot.Version} invoked (InterfaceCount={snapshot.InterfaceCount})");

            bool preferIPv6 = !filterIPv6 && filterIPv4;

            // Primary and list rows use identical explicit Inspector filters.
            if (primaryNetworkItem != null)
            {
                bool bound = false;
                for (int i = 0; i < snapshot.InterfaceCount; i++)
                {
                    if (!snapshot.TryGetInterface(i, out var iface) || !MatchesFilters(snapshot, i, in iface))
                        continue;
                    Logger.LogApp($"[NetDiag][UI] primaryNetworkItem binding to NIC[{i}] '{iface.Name}' (preferIPv6={preferIPv6})");
                    primaryNetworkItem.Bind(in iface, snapshot, this, preferIPv6);
                    bound = true;
                    break;
                }
                if (!bound)
                {
                    Logger.LogApp("[NetDiag][UI] primaryNetworkItem cleared (no eligible interface)");
                    primaryNetworkItem.Clear();
                }
            }

            if (_dropdown != null)
                RefreshDropdown(snapshot);
            else if (networkItem != null && _ownedListRoot != null)
                RenderSiblingList(snapshot);
        }

        // Native TMP_Dropdown renders one option per entry. Its Template/Item is NOT a list container.
        // This also replaces stale Inspector options (e.g. three hardcoded wlan0 entries).
        private void RefreshDropdown(NetworkSnapshot snapshot)
        {
            if (_dropdown == null || snapshot == null) return;

            string selectedId = _dropdownIds.Count > _dropdown.value && _dropdown.value >= 0
                ? _dropdownIds[_dropdown.value] : null;

            var nextIds = _nextDropdownIds;
            var nextTexts = _nextDropdownTexts;
            nextIds.Clear();
            nextTexts.Clear();
            bool preferIPv6 = !filterIPv6 && filterIPv4;
            var service = NetworkService;

            for (int i = 0; i < snapshot.InterfaceCount; i++)
            {
                if (!snapshot.TryGetInterface(i, out var nic) || !MatchesFilters(snapshot, i, in nic))
                    continue;

                string ip = null;
                for (int pass = 0; pass < 2 && ip == null; pass++)
                {
                    bool useV6 = pass == 0 ? preferIPv6 : !preferIPv6;
                    if ((useV6 && filterIPv6) || (!useV6 && filterIPv4)) continue;
                    for (int a = 0; a < nic.AddressCount; a++)
                    {
                        if (!snapshot.TryGetAddress(i, a, out var address)) continue;
                        if (useV6 ? address.IsIPv6 : address.IsIPv4) { ip = address.Address; break; }
                    }
                }

                double rx = -1, tx = -1;
                bool validRate = service != null && service.TryGetTraffic(nic.Id, out rx, out tx);
                // Throughput is live telemetry, not the cumulative byte counter.
                string rate = validRate
                    ? $"RX {NetworkTrafficMonitor.FormatValue(rx, TrafficFormat)}  TX {NetworkTrafficMonitor.FormatValue(tx, TrafficFormat)}"
                    : "RX N/A  TX N/A";
                nextIds.Add(nic.Id);
                nextTexts.Add($"{nic.DisplayName} ({ip ?? "N/A"})   {rate}");
            }

            // Rebuild options only when the set/order of interfaces changes.
            // Live RX/TX text can update without resetting the user's selection.
            bool interfacesChanged = _dropdownIds.Count != nextIds.Count || _dropdown.options.Count != nextIds.Count;
            if (!interfacesChanged)
                for (int i = 0; i < nextIds.Count; i++)
                    if (_dropdownIds[i] != nextIds[i]) { interfacesChanged = true; break; }

            if (interfacesChanged)
            {
                _dropdown.ClearOptions();
                _dropdownIds.Clear();
                for (int i = 0; i < nextIds.Count; i++)
                {
                    _dropdownIds.Add(nextIds[i]);
                    _dropdown.options.Add(new TMP_Dropdown.OptionData(nextTexts[i]));
                }
                int index = string.IsNullOrEmpty(selectedId) ? -1 : _dropdownIds.IndexOf(selectedId);
                _dropdown.SetValueWithoutNotify(Mathf.Max(0, index));
                _dropdown.RefreshShownValue();
                return;
            }

            bool textChanged = false;
            for (int i = 0; i < nextTexts.Count; i++)
            {
                if (_dropdown.options[i].text == nextTexts[i]) continue;
                _dropdown.options[i].text = nextTexts[i];
                textChanged = true;
            }
            if (textChanged) _dropdown.RefreshShownValue();
        }

        private void AdoptSiblingRows()
        {
            if (networkItem == null || networkItem.transform.parent == null) return;
            Transform root = networkItem.transform.parent;
            // Only direct siblings belong to this list; leave nested/other UI untouched.
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == networkItem.transform ||
                    (primaryNetworkItem != null && child == primaryNetworkItem.transform)) continue;
                if (child.TryGetComponent<NetworkItemView>(out var row) && !_itemPool.Contains(row))
                {
                    Logger.LogApp($"[NetDiag][UI] AdoptSiblingRows: adopting existing child '{child.name}' (InstanceID={child.gameObject.GetInstanceID()})");
                    row.gameObject.SetActive(false);
                    _itemPool.Add(row);
                }
            }
            Logger.LogApp($"[NetDiag][UI] AdoptSiblingRows complete. _itemPool count={_itemPool.Count}");
        }

        private void RenderSiblingList(NetworkSnapshot snapshot)
        {
            Transform parentTransform = networkItem.transform.parent;
            if (parentTransform == null) return;
            int targetItemIdx = 0;

            Logger.LogApp($"[NetDiag][UI] RenderSiblingList START: Snapshot Interfaces={snapshot.InterfaceCount}, parent='{parentTransform.name}' (current childCount={parentTransform.childCount}, itemPool.Count={_itemPool.Count})");

            bool preferIPv6 = !filterIPv6 && filterIPv4;

            // One row per eligible network interface, not one row per IP address.
            for (int i = 0; i < snapshot.InterfaceCount; i++)
            {
                if (!snapshot.TryGetInterface(i, out var iface) || !MatchesFilters(snapshot, i, in iface))
                    continue;

                while (targetItemIdx >= _itemPool.Count)
                {
                    var clone = Instantiate(networkItem, parentTransform);
                    _itemPool.Add(clone);
                    _runtimeClones.Add(clone);
                    Logger.LogApp($"[NetDiag][UI] Instantiated new row clone #{_itemPool.Count - 1} '{clone.name}' (InstanceID={clone.gameObject.GetInstanceID()})");
                }

                var itemView = _itemPool[targetItemIdx];
                // A wrongly wired prefab can produce multiple visually identical "wlan0"
                // labels despite distinct source interfaces. Never bind through shared labels.
                if (!itemView.EnsureLocalBindings())
                {
                    Logger.LogApp($"[NetDiag][UI] EnsureLocalBindings FAILED on itemView '{itemView.name}'! Deactivating row to avoid duplicate/misleading visual.", LogType.Warning);
                    itemView.gameObject.SetActive(false);
                    continue;
                }
                targetItemIdx++;
                itemView.gameObject.SetActive(true);
                Logger.LogApp($"[NetDiag][UI] Row[{targetItemIdx - 1}]: Binding NIC[{i}] '{iface.Name}' to itemView '{itemView.name}' (InstanceID={itemView.gameObject.GetInstanceID()})");
                itemView.Bind(in iface, snapshot, this, preferIPv6);
            }

            // Deactivate surplus pooled items
            for (int i = targetItemIdx; i < _itemPool.Count; i++)
            {
                Logger.LogApp($"[NetDiag][UI] Deactivating surplus pooled item #{i} '{_itemPool[i].name}' (InstanceID={_itemPool[i].gameObject.GetInstanceID()})");
                _itemPool[i].gameObject.SetActive(false);
            }

            Logger.LogApp($"[NetDiag][UI] RenderSiblingList END: bound {targetItemIdx} rows. Children under '{parentTransform.name}':");
            for (int c = 0; c < parentTransform.childCount; c++)
            {
                var ch = parentTransform.GetChild(c);
                Logger.LogApp($"[NetDiag][UI]   parent.Child[{c}]: '{ch.name}' activeSelf={ch.gameObject.activeSelf}, InstanceID={ch.gameObject.GetInstanceID()}");
            }
        }

        [ContextMenu("Debug Network List")]
        public void DebugNetworkList()
        {
            var snapshot = NetworkService != null ? NetworkService.CurrentSnapshot : NetworkSnapshot.Empty;
            int eligible = 0;
            for (int i = 0; i < snapshot.InterfaceCount; i++)
            {
                if (snapshot.TryGetInterface(i, out var nic))
                {
                    bool show = MatchesFilters(snapshot, i, in nic);
                    if (show) eligible++;
                    Logger.LogApp($"[NetworkUI] Snapshot#{i}: id={nic.Id}, type={nic.Type}, " +
                              $"IPv4={snapshot.GetPrimaryIPv4(i)}, eligible={show}");
                }
            }
            Logger.LogApp($"[NetworkUI] Filters: IPv4={filterIPv4}, IPv6={filterIPv6}, " +
                      $"Loopback={filterLoopback}, Virtual={filterVirtual}, " +
                      $"Disconnected={filterDisconnected}, VPN={filterVpn}. " +
                      $"Snapshot={snapshot.InterfaceCount}, Eligible={eligible}, Pool={_itemPool.Count}.");

            if (_dropdown != null)
            {
                Logger.LogApp($"[NetworkUI] NativeDropdown='{_dropdown.name}', Options={_dropdown.options.Count}, SnapshotEligible={eligible}.");
                for (int i = 0; i < _dropdown.options.Count; i++)
                    Logger.LogApp($"[NetworkUI] Option[{i}]: {_dropdown.options[i].text}");
                return;
            }
            if (networkItem == null || networkItem.transform.parent == null)
            {
                Logger.LogApp("[NetworkUI] Missing list template or parent.", LogType.Error);
                return;
            }
            Transform root = networkItem.transform.parent;
            int visible = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (!child.gameObject.activeSelf || child == networkItem.transform) continue;
                if (child.TryGetComponent<NetworkItemView>(out var row))
                {
                    visible++;
                    Logger.LogApp($"[NetworkUI] VisibleRow '{row.name}': bound={row.BoundInterfaceId ?? "<unbound>"}, " +
                              $"label={row.displayNameText?.text ?? "<null>"}, " +
                              $"ownLabel={row.OwnsDisplayText}, ownRx={row.OwnsRxText}, ownTx={row.OwnsTxText}, " +
                              $"rx={row.rxTrafficText?.text ?? "<null>"}, tx={row.txTrafficText?.text ?? "<null>"}");
                }
                else
                    Logger.LogApp($"[NetworkUI] Unmanaged active child in list: '{child.name}'.", LogType.Warning);
            }
            Logger.LogApp($"[NetworkUI] VisibleRows={visible}, Eligible={eligible}, " +
                      $"PrimaryInsideList={primaryNetworkItem != null && primaryNetworkItem.transform.parent == root}, " +
                      $"OwnsRoot={_ownedListRoot == root}");
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
