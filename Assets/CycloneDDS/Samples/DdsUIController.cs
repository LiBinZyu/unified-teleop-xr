using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using TMPro;
using UTXR.Prefs;
using Network.InterfaceInfo;

namespace CycloneDDS
{
    /// <summary>
    /// UI Controller for CycloneDDS:
    /// 1. Service Running Toggle: starts / stops DDS service and binds to PlayerPrefs.
    /// 2. Discovered IPs Dropdown (ipDropdown): DDS LAN discovery dropdown (Option 0: "Use Custom IP", Options 1..N: Scanned IPs).
    /// 3. Target IP InputField (targetIpInputField): target robot IP input.
    /// 4. Interface ID Dropdown (interfaceDropdown): local network card (NIC) selection. Matched NIC is auto-calculated and shown as Option 0.
    /// </summary>
    public class DdsUIController : MonoBehaviour
    {
        private const string PREF_KEY_SERVICE = "Dds_ServiceEnabled";
        private const string PREF_KEY_TARGET_IP = "Dds_TargetIp";
        private const string PREF_KEY_INTERFACE_ID = "Dds_InterfaceId";

        private const string CustomIpOptionLabel = "Use Custom IP";

        [Header("DDS Publisher Reference")]
        [Tooltip("Reference to DdsPublisher. If null, automatically resolves Instance or FindFirstObjectByType")]
        public DdsPublisher publisher;

        [Header("Service Running State (Toggle)")]
        [Tooltip("Toggle component controlling and reflecting the DDS service running state")]
        public Toggle serviceToggle;

        [Header("Self IP / Status Display")]
        [Tooltip("TMP_Text displaying local IP addresses or status messages")]
        public TMP_Text selfIpText;

        [Header("1. Discovered IP Dropdown (DDS Scanner)")]
        [Tooltip("Dropdown listing scanned robot IPs. Option 0 is 'Use Custom IP'.")]
        public TMP_Dropdown ipDropdown;

        [Header("2. Target IP Input Field")]
        [Tooltip("TMP_InputField for target robot IP entry")]
        [FormerlySerializedAs("customIpInputField")]
        public TMP_InputField targetIpInputField;

        [Header("3. Local Network Interface Dropdown")]
        [Tooltip("Dropdown listing local physical network cards (NICs). Auto-matched interface appears as Option 0.")]
        public TMP_Dropdown interfaceDropdown;

        [Header("Optional Display & Status Icons")]
        [Tooltip("Optional TMP_Text displaying selected discovered IP")]
        public TMP_Text selectedIpText;

        [Tooltip("Icon displayed next to an IP in the dropdown when a device is actively subscribing (Green)")]
        public Sprite subscribingSprite;

        [Tooltip("Icon displayed next to an IP in the dropdown when idle / not subscribing (Gray)")]
        public Sprite notSubscribingSprite;

        private string _currentTargetIp = "";
        private string _currentInterfaceId = "";
        private List<string> _discoveredIps = new List<string>();
        private bool _lastMatchedState = false;
        private string _lastPublishedTargetIp = "";

        private void Awake()
        {
            EnsurePublisher();

            if (subscribingSprite == null)
                subscribingSprite = CreateDotSprite(new Color(0.2f, 0.9f, 0.3f, 1f));
            if (notSubscribingSprite == null)
                notSubscribingSprite = CreateDotSprite(new Color(0.5f, 0.5f, 0.5f, 0.6f));
        }

        private void Start()
        {
            EnsurePublisher();

            // 1. Load stored Target IP (fallback to legacy key or publisher's default)
            string defaultTarget = publisher != null && !string.IsNullOrWhiteSpace(publisher.targetIp)
                ? publisher.targetIp.Trim()
                : "192.168.8.181";
            _currentTargetIp = PlayerPrefsManager.GetString(PREF_KEY_TARGET_IP, PlayerPrefsManager.GetString("Dds_CustomIp", defaultTarget));

            // 2. Resolve preferred Interface ID based on Target IP's subnet
            _currentInterfaceId = NetworkInterfacePrefBridge.ResolvePreferredInterfaceId(
                PREF_KEY_INTERFACE_ID, _currentTargetIp, publisher != null ? publisher.interfaceId : "");

            // 3. Service Running Toggle (validated against available network interface & target IP)
            bool initialServiceState = publisher != null && publisher.isInitialized;
            if (initialServiceState && !HasUsableInterfaceAndTarget())
            {
                initialServiceState = false;
                publisher?.SetServiceRunning(false);
                PlayerPrefsManager.SetBool(PREF_KEY_SERVICE, false);
            }

            PlayerPrefsManager.Bind(serviceToggle, PREF_KEY_SERVICE, initialServiceState, isOn =>
            {
                if (isOn)
                {
                    if (!HasUsableInterfaceAndTarget())
                    {
                        if (serviceToggle != null) serviceToggle.SetIsOnWithoutNotify(false);
                        PlayerPrefsManager.SetBool(PREF_KEY_SERVICE, false);
                        publisher?.SetServiceRunning(false);
                        return;
                    }
                }
                publisher?.SetServiceRunning(isOn);
            });

            // 4. Bind Target IP InputField
            if (targetIpInputField != null)
            {
                PlayerPrefsManager.Bind(targetIpInputField, PREF_KEY_TARGET_IP, _currentTargetIp, newIp =>
                {
                    ApplyTargetIp(newIp);
                    ValidateServiceToggleState();
                });
            }

            // 5. Populate and Bind Interface Dropdown (matched NIC is placed as Option 0)
            RefreshInterfaceDropdown();

            // 6. Initialize Discovered IPs Dropdown
            InitDiscoveredIpDropdown();

            // 7. Apply initial settings to publisher
            publisher?.SwitchTarget(_currentTargetIp, _currentInterfaceId);
            ValidateServiceToggleState();
        }

        #region Target IP & Interface Binding

        /// <summary>
        /// Central method for setting target IP (from input field or discovered dropdown selection).
        /// Automatically recalculates the matching NIC (interfaceId), refreshes interfaceDropdown,
        /// and updates the DDS publisher.
        /// </summary>
        private void ApplyTargetIp(string newIp)
        {
            if (string.IsNullOrWhiteSpace(newIp)) return;
            newIp = newIp.Trim();

            _currentTargetIp = newIp;
            PlayerPrefsManager.SetString(PREF_KEY_TARGET_IP, newIp);

            // Auto-calculate matching physical NIC by subnet
            if (NetworkInfoService.FindInterfaceBySubnet(newIp, out var matchedIface, out _))
            {
                _currentInterfaceId = matchedIface.Id;
                PlayerPrefsManager.SetString(PREF_KEY_INTERFACE_ID, _currentInterfaceId);
            }

            // Refresh interfaceDropdown so the newly matched NIC is displayed as Option 0
            RefreshInterfaceDropdown();

            // Notify publisher
            _lastPublishedTargetIp = newIp;
            publisher?.SwitchTarget(_currentTargetIp, _currentInterfaceId);
        }

        private void RefreshInterfaceDropdown()
        {
            if (interfaceDropdown == null) return;

            NetworkInterfacePrefBridge.BindDropdown(interfaceDropdown, PREF_KEY_INTERFACE_ID, _currentInterfaceId, (selectedIface, localIp) =>
            {
                // User manually selected a different network interface in dropdown
                _currentInterfaceId = selectedIface.Id;
                publisher?.SwitchTarget(_currentTargetIp, _currentInterfaceId);
            });
        }

        #endregion

        #region Discovered IPs Dropdown

        private void InitDiscoveredIpDropdown()
        {
            if (ipDropdown == null) return;

            ipDropdown.onValueChanged.RemoveListener(OnDiscoveredIpDropdownChanged);
            ipDropdown.onValueChanged.AddListener(OnDiscoveredIpDropdownChanged);
            RebuildDiscoveredIpOptions();
        }

        private void OnDiscoveredIpDropdownChanged(int index)
        {
            if (index == 0)
            {
                // Option 0: Use Custom IP
                if (selectedIpText != null) selectedIpText.gameObject.SetActive(false);
                if (targetIpInputField != null)
                {
                    targetIpInputField.gameObject.SetActive(true);
                    targetIpInputField.ActivateInputField();
                }

                string manualIp = targetIpInputField != null && !string.IsNullOrWhiteSpace(targetIpInputField.text)
                    ? targetIpInputField.text
                    : _currentTargetIp;
                ApplyTargetIp(manualIp);
            }
            else
            {
                // Option 1..N: Discovered IP from scanner
                int ipIndex = index - 1;
                if (ipIndex >= 0 && ipIndex < _discoveredIps.Count)
                {
                    string chosenIp = _discoveredIps[ipIndex];

                    if (targetIpInputField != null) targetIpInputField.gameObject.SetActive(false);
                    if (selectedIpText != null)
                    {
                        selectedIpText.gameObject.SetActive(true);
                        selectedIpText.text = chosenIp;
                    }

                    ApplyTargetIp(chosenIp);
                }
            }

            ValidateServiceToggleState();
        }

        public void RebuildDiscoveredIpOptions()
        {
            if (ipDropdown == null) return;

            var options = new List<TMP_Dropdown.OptionData>();
            options.Add(new TMP_Dropdown.OptionData { text = CustomIpOptionLabel });

            for (int i = 0; i < _discoveredIps.Count; i++)
            {
                string ip = _discoveredIps[i];
                bool isSubscribing = IsIpSubscribing(ip);
                Sprite icon = isSubscribing ? subscribingSprite : notSubscribingSprite;
                options.Add(new TMP_Dropdown.OptionData { text = ip, image = icon });
            }

            ipDropdown.ClearOptions();
            ipDropdown.AddOptions(options);

            // Determine selection index: if current target IP matches a discovered IP, select it; otherwise select 0 (custom)
            int selectedIndex = 0;
            if (!string.IsNullOrEmpty(_currentTargetIp))
            {
                int found = _discoveredIps.IndexOf(_currentTargetIp);
                if (found >= 0) selectedIndex = found + 1;
            }

            ipDropdown.SetValueWithoutNotify(selectedIndex);

            // Update UI visibility
            if (selectedIndex == 0)
            {
                if (selectedIpText != null) selectedIpText.gameObject.SetActive(false);
                if (targetIpInputField != null) targetIpInputField.gameObject.SetActive(true);
            }
            else
            {
                if (targetIpInputField != null) targetIpInputField.gameObject.SetActive(false);
                if (selectedIpText != null)
                {
                    selectedIpText.gameObject.SetActive(true);
                    selectedIpText.text = (selectedIndex - 1 < _discoveredIps.Count) ? _discoveredIps[selectedIndex - 1] : "";
                }
            }
        }

        // Backward compatibility method
        public void RebuildDropdownOptions() => RebuildDiscoveredIpOptions();

        private bool IsIpSubscribing(string ip)
        {
            if (publisher == null || !publisher.isInitialized) return false;
            return publisher.targetIp == ip && publisher.hasMatchedSubscribers;
        }

        #endregion

        #region Publisher Callbacks & Lifecycle

        private NetworkInfoService _observedNetworkService;

        private void OnEnable()
        {
            EnsurePublisher();
            if (publisher != null)
            {
                publisher.OnSelfIpChanged += OnSelfIpUpdated;
                publisher.OnDiscoveredIpsChanged += OnDiscoveredIpsUpdated;
                publisher.OnConnectionTimeout += OnDdsTimeout;

                if (!string.IsNullOrEmpty(publisher.currentSelfIpSummary))
                    OnSelfIpUpdated(publisher.currentSelfIpSummary);
                if (publisher.discoveredIps != null && publisher.discoveredIps.Count > 0)
                    OnDiscoveredIpsUpdated(publisher.discoveredIps);
            }

            SubscribeNetworkService();
            ValidateServiceToggleState();
        }

        private void OnDisable()
        {
            if (publisher != null)
            {
                publisher.OnSelfIpChanged -= OnSelfIpUpdated;
                publisher.OnDiscoveredIpsChanged -= OnDiscoveredIpsUpdated;
                publisher.OnConnectionTimeout -= OnDdsTimeout;
            }

            UnsubscribeNetworkService();
        }

        private void SubscribeNetworkService()
        {
            var svc = NetworkInfoService.Instance;
            if (_observedNetworkService == svc) return;
            UnsubscribeNetworkService();
            _observedNetworkService = svc;
            if (_observedNetworkService != null)
            {
                _observedNetworkService.OnInterfacesChanged += OnNetworkInterfacesChanged;
            }
        }

        private void UnsubscribeNetworkService()
        {
            if (_observedNetworkService != null)
            {
                _observedNetworkService.OnInterfacesChanged -= OnNetworkInterfacesChanged;
                _observedNetworkService = null;
            }
        }

        private void OnNetworkInterfacesChanged(int _)
        {
            ValidateServiceToggleState();
        }

        /// <summary>
        /// Validates whether there is an operational local network interface and a valid non-empty target IP.
        /// If Custom IP is selected (ipDropdown index == 0), targetIpInputField cannot be empty.
        /// </summary>
        public bool HasUsableInterfaceAndTarget()
        {
            // 1. Validate if target IP is provided (custom input field or selected discovered dropdown)
            if (ipDropdown != null && ipDropdown.value == 0)
            {
                if (targetIpInputField == null || string.IsNullOrWhiteSpace(targetIpInputField.text))
                {
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_currentTargetIp))
                {
                    return false;
                }
            }

            // 2. Validate if there is any active, non-loopback network interface available
            var activeInterfaces = NetworkInfoService.GetActiveInterfaces();
            if (activeInterfaces == null || activeInterfaces.Count == 0)
            {
                return false;
            }

            // 3. If a specific interface is selected, ensure it is still operational
            if (!string.IsNullOrEmpty(_currentInterfaceId))
            {
                bool interfaceFound = false;
                for (int i = 0; i < activeInterfaces.Count; i++)
                {
                    var item = activeInterfaces[i];
                    if (string.Equals(item.Info.Id, _currentInterfaceId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(item.Info.Name, _currentInterfaceId, StringComparison.OrdinalIgnoreCase))
                    {
                        interfaceFound = true;
                        break;
                    }
                }
                if (!interfaceFound)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks network and target availability. If unusable and serviceToggle is ON,
        /// turns it off without notify, updates PlayerPrefs, and shuts down DDS.
        /// </summary>
        public void ValidateServiceToggleState()
        {
            if (serviceToggle != null && serviceToggle.isOn)
            {
                if (!HasUsableInterfaceAndTarget())
                {
                   serviceToggle.isOn = false;
                    PlayerPrefsManager.SetBool(PREF_KEY_SERVICE, false);
                    publisher?.SetServiceRunning(false);
                }
            }
        }

        private void OnDdsTimeout(string target)
        {
            if (selfIpText != null)
            {
                selfIpText.text = $"[Timeout] Waiting for peer {target} on {_currentInterfaceId}...";
            }
        }

        private void Update()
        {
            if (_observedNetworkService != NetworkInfoService.Instance)
            {
                SubscribeNetworkService();
                ValidateServiceToggleState();
            }
            if (publisher != null && publisher.hasMatchedSubscribers != _lastMatchedState)
            {
                _lastMatchedState = publisher.hasMatchedSubscribers;
                RebuildDiscoveredIpOptions();
            }
        }

        private void OnSelfIpUpdated(string selfIpSummary)
        {
            if (selfIpText != null && selfIpText.text != selfIpSummary)
                selfIpText.text = selfIpSummary;
        }

        private void OnDiscoveredIpsUpdated(List<string> ips)
        {
            if (AreListsEqual(_discoveredIps, ips)) return;
            _discoveredIps = new List<string>(ips);
            RebuildDiscoveredIpOptions();
        }

        private static bool AreListsEqual(List<string> a, List<string> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private void EnsurePublisher()
        {
            if (publisher == null)
            {
                publisher = DdsPublisher.Instance;
                if (publisher == null)
                {
                    publisher = FindFirstObjectByType<DdsPublisher>();
                }
            }
        }

        #endregion

        #region Procedural Sprite Generator

        private static Sprite CreateDotSprite(Color color)
        {
            int size = 16;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = (size - 1) / 2f;
            float radius = size / 2f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    if (dist <= radius)
                    {
                        float alpha = Mathf.Clamp01((radius - dist) + 0.5f);
                        tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        #endregion
    }
}
