using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UTXR.Prefs;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Persists NIC selection and keeps one TMP_Dropdown synchronized with network snapshots.
    /// BindDropdown remains a static entry point; the instance on each dropdown owns subscriptions.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Dropdown))]
    public sealed class NetworkInterfacePrefBridge : MonoBehaviour
    {
        private TMP_Dropdown _dropdown;
        private NetworkInfoService _service;
        private string _prefKey;
        private string _selectedId;
        private string _targetIp;
        private string _lastId;
        private string _lastIp;
        private bool _configured;
        private bool _notified;
        private Action<NetworkInterfaceInfo, string> _onSelectionChanged;
        private readonly List<(NetworkInterfaceInfo Info, string LocalIp)> _items =
            new List<(NetworkInterfaceInfo Info, string LocalIp)>();

        public static string ResolvePreferredInterfaceId(string prefKey, string targetIp, string fallbackId = "")
        {
            var interfaces = NetworkInfoService.GetActiveInterfaces();
            string savedId = string.IsNullOrEmpty(prefKey)
                ? fallbackId : PlayerPrefsManager.GetString(prefKey, fallbackId);

            foreach (var item in interfaces)
            {
                if (MatchesId(item.Info, savedId)) return item.Info.Id;
            }

            if (!string.IsNullOrWhiteSpace(targetIp) &&
                NetworkInfoService.FindInterfaceBySubnet(targetIp, out var matched, out _))
            {
                foreach (var item in interfaces)
                    if (MatchesId(item.Info, matched.Id)) return item.Info.Id;
            }

            return interfaces.Count > 0 ? interfaces[0].Info.Id : string.Empty;
        }

        /// <summary>Call once to bind; further updates follow the service's snapshot event.</summary>
        public static void BindDropdown(TMP_Dropdown dropdown, string prefKey,
            string preferredInterfaceId, Action<NetworkInterfaceInfo, string> onSelectionChanged,
            string targetIp = null)
        {
            if (dropdown == null)
            {
                string id = ResolvePreferredInterfaceId(prefKey, targetIp, preferredInterfaceId);
                foreach (var item in NetworkInfoService.GetActiveInterfaces())
                {
                    if (!MatchesId(item.Info, id)) continue;
                    onSelectionChanged?.Invoke(item.Info, item.LocalIp);
                    break;
                }
                return;
            }

            var bridge = dropdown.GetComponent<NetworkInterfacePrefBridge>();
            if (bridge == null) bridge = dropdown.gameObject.AddComponent<NetworkInterfacePrefBridge>();
            bridge.Configure(dropdown, prefKey, preferredInterfaceId, onSelectionChanged, targetIp);
        }

        private void Awake()
        {
            _dropdown = GetComponent<TMP_Dropdown>();
            _dropdown.onValueChanged.AddListener(OnValueChanged);
        }

        private void Configure(TMP_Dropdown dropdown, string prefKey, string preferredId,
            Action<NetworkInterfaceInfo, string> callback, string targetIp)
        {
            _dropdown = dropdown;
            _prefKey = prefKey;
            // Persisted user selection takes precedence when rebinding after a scene reload.
            _selectedId = !string.IsNullOrEmpty(prefKey)
                ? PlayerPrefsManager.GetString(prefKey, preferredId) : preferredId;
            _targetIp = targetIp;
            _onSelectionChanged = callback;
            _notified = false;
            _configured = true;
            if (isActiveAndEnabled) Subscribe();
            Refresh();
        }

        private void OnEnable()
        {
            if (!_configured) return;
            Subscribe();
            Refresh();
        }

        private void OnDisable() => Unsubscribe();

        private void OnDestroy()
        {
            Unsubscribe();
            if (_dropdown != null) _dropdown.onValueChanged.RemoveListener(OnValueChanged);
        }

        private void Update()
        {
            // The service can be instantiated later, or replaced after a scene change.
            if (_configured && _service != NetworkInfoService.Instance)
            {
                Subscribe();
                Refresh();
            }
        }

        private void Subscribe()
        {
            var current = NetworkInfoService.Instance;
            if (_service == current) return;
            Unsubscribe();
            _service = current;
            if (_service != null) _service.OnInterfacesChanged += OnInterfacesChanged;
        }

        private void Unsubscribe()
        {
            if (_service != null) _service.OnInterfacesChanged -= OnInterfacesChanged;
            _service = null;
        }

        private void OnInterfacesChanged(int _) => Refresh();

        private static bool MatchesId(NetworkInterfaceInfo info, string id) =>
            !string.IsNullOrEmpty(id) &&
            (string.Equals(info.Id, id, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(info.Name, id, StringComparison.OrdinalIgnoreCase));

        private static string Label(NetworkInterfaceInfo info) =>
            !string.IsNullOrEmpty(info.DisplayName) ? info.DisplayName : info.Name;

        private static bool SameItem((NetworkInterfaceInfo Info, string LocalIp) a,
            (NetworkInterfaceInfo Info, string LocalIp) b) =>
            string.Equals(a.Info.Id, b.Info.Id, StringComparison.Ordinal) &&
            string.Equals(a.LocalIp, b.LocalIp, StringComparison.Ordinal) &&
            string.Equals(Label(a.Info), Label(b.Info), StringComparison.Ordinal);

        private int FindIndex(string id)
        {
            for (int i = 0; i < _items.Count; i++)
                if (MatchesId(_items[i].Info, id)) return i;
            return -1;
        }

        private void Refresh()
        {
            if (!_configured || _dropdown == null) return;

            var available = NetworkInfoService.GetActiveInterfaces();
            bool changed = available.Count != _items.Count || _dropdown.options.Count == 0;
            if (!changed)
                for (int i = 0; i < available.Count; i++)
                    if (!SameItem(_items[i], available[i])) { changed = true; break; }

            if (changed)
            {
                _dropdown.Hide();
                _items.Clear();
                _items.AddRange(available);
                var labels = new List<string>(_items.Count);
                foreach (var item in _items) labels.Add(Label(item.Info));
                if (labels.Count == 0) labels.Add("No active IPv4 interfaces");
                _dropdown.ClearOptions();
                _dropdown.AddOptions(labels);
            }

            if (_items.Count == 0)
            {
                _dropdown.SetValueWithoutNotify(0);
                _dropdown.RefreshShownValue();
                _notified = false; // Reconnect must notify DDS/Record3D, even if same IP.
                return;
            }

            int index = FindIndex(_selectedId);
            if (index < 0 && !string.IsNullOrWhiteSpace(_targetIp) &&
                NetworkInfoService.FindInterfaceBySubnet(_targetIp, out var matched, out _))
                index = FindIndex(matched.Id);
            if (index < 0) index = 0;

            if (changed || _dropdown.value != index)
            {
                _dropdown.SetValueWithoutNotify(index);
                _dropdown.RefreshShownValue();
            }
            ApplySelection(_items[index]);
        }

        private void OnValueChanged(int index)
        {
            if ((uint)index < (uint)_items.Count) ApplySelection(_items[index]);
        }

        private void ApplySelection((NetworkInterfaceInfo Info, string LocalIp) item)
        {
            _selectedId = item.Info.Id;
            if (!string.IsNullOrEmpty(_prefKey) &&
                !string.Equals(PlayerPrefsManager.GetString(_prefKey, ""), _selectedId, StringComparison.Ordinal))
                PlayerPrefsManager.SetString(_prefKey, _selectedId);

            if (_notified && string.Equals(_lastId, _selectedId, StringComparison.Ordinal) &&
                string.Equals(_lastIp, item.LocalIp, StringComparison.Ordinal)) return;

            _lastId = _selectedId;
            _lastIp = item.LocalIp;
            _notified = true;
            _onSelectionChanged?.Invoke(item.Info, item.LocalIp);
        }
    }
}
