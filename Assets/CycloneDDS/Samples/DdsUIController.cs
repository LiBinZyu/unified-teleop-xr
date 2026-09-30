using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CycloneDDS
{
    /// <summary>
    /// Pure UI View Controller for CycloneDDS.
    /// Connects to DdsPublisher for real-time state:
    /// - Service On/Off Toggle (strictly reflecting real DDS service state)
    /// - Self IP display (updates automatically via DdsPublisher.OnSelfIpChanged)
    /// - Discovered Target IP TMP_Dropdown (updates automatically via DdsPublisher.OnDiscoveredIpsChanged)
    /// - Dynamic switching between TMP_Text and TMP_InputField when "Use Custom IP" is chosen (Dropdown remains visible)
    /// - Item image in Dropdown indicating active subscriber connection status
    /// </summary>
    public class DdsUIController : MonoBehaviour
    {
        [Header("DDS Publisher Reference")]
        [Tooltip("Reference to DdsPublisher. If null, automatically resolves Instance or FindFirstObjectByType")]
        public DdsPublisher publisher;

        [Header("Service Running State (Toggle)")]
        [Tooltip("Toggle component controlling and reflecting the DDS service running state")]
        public Toggle serviceToggle;

        [Header("Self IP Display")]
        [Tooltip("TMP_Text displaying local IP addresses (differentiating WLAN and Ethernet)")]
        public TMP_Text selfIpText;

        [Header("Target IP Selection and Input")]
        [Tooltip("Dropdown listing discovered IPs, with first option 'Use Custom IP'. ALWAYS remains visible.")]
        public TMP_Dropdown ipDropdown;

        [Tooltip("Optional TMP_Text displaying selected discovered IP (hidden when custom IP is selected)")]
        public TMP_Text selectedIpText;

        [Tooltip("TMP_InputField for manual IP entry (visible only when 'Use Custom IP' is selected, alongside Dropdown)")]
        public TMP_InputField customIpInputField;

        [Header("Subscriber Status Indicator Sprites")]
        [Tooltip("Icon displayed next to an IP in the dropdown when a device is actively subscribing (Green)")]
        public Sprite subscribingSprite;

        [Tooltip("Icon displayed next to an IP in the dropdown when idle / not subscribing (Gray)")]
        public Sprite notSubscribingSprite;

        private const string CustomIpOptionLabel = "Use Custom IP";
        private List<string> _discoveredIps = new List<string>();
        private bool _isSyncingToggle = false;
        private string _lastPublishedTargetIp = "";
        private bool _lastMatchedState = false;

        private void Awake()
        {
            EnsurePublisher();

            // Create procedural default indicator sprites if none assigned in Inspector
            if (subscribingSprite == null)
            {
                subscribingSprite = CreateDotSprite(new Color(0.2f, 0.9f, 0.3f, 1f)); // Vibrant Green
            }
            if (notSubscribingSprite == null)
            {
                notSubscribingSprite = CreateDotSprite(new Color(0.5f, 0.5f, 0.5f, 0.6f)); // Muted Gray
            }
        }

        private void Start()
        {
            InitServiceToggle();
            InitIpDropdown();
            InitCustomIpInput();
        }

        private void OnEnable()
        {
            EnsurePublisher();
            if (publisher != null)
            {
                publisher.OnSelfIpChanged += OnSelfIpUpdated;
                publisher.OnDiscoveredIpsChanged += OnDiscoveredIpsUpdated;

                // Sync initial cached states
                if (!string.IsNullOrEmpty(publisher.currentSelfIpSummary))
                {
                    OnSelfIpUpdated(publisher.currentSelfIpSummary);
                }
                if (publisher.discoveredIps != null && publisher.discoveredIps.Count > 0)
                {
                    OnDiscoveredIpsUpdated(publisher.discoveredIps);
                }
            }
        }

        private void OnDisable()
        {
            if (publisher != null)
            {
                publisher.OnSelfIpChanged -= OnSelfIpUpdated;
                publisher.OnDiscoveredIpsChanged -= OnDiscoveredIpsUpdated;
            }
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

        private void Update()
        {
            // Sync Toggle with true DDS state (ensuring 100% accurate true/false)
            SyncToggleState();

            // Check if subscriber connection state changed to update item icons
            if (publisher != null && publisher.hasMatchedSubscribers != _lastMatchedState)
            {
                _lastMatchedState = publisher.hasMatchedSubscribers;
                RebuildDropdownOptions();
            }
        }

        #region Publisher Event Callbacks

        private void OnSelfIpUpdated(string selfIpSummary)
        {
            if (selfIpText != null && selfIpText.text != selfIpSummary)
            {
                selfIpText.text = selfIpSummary;
            }
        }

        private void OnDiscoveredIpsUpdated(List<string> ips)
        {
            if (AreListsEqual(_discoveredIps, ips)) return;
            _discoveredIps = new List<string>(ips);
            RebuildDropdownOptions();
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

        #endregion

        #region Service Toggle Management

        private void InitServiceToggle()
        {
            if (serviceToggle == null) return;

            bool isRunning = (publisher != null && publisher.isInitialized);
            _isSyncingToggle = true;
            serviceToggle.SetIsOnWithoutNotify(isRunning);
            _isSyncingToggle = false;

            serviceToggle.onValueChanged.AddListener(OnServiceToggleChanged);
        }

        private void OnServiceToggleChanged(bool isOn)
        {
            if (_isSyncingToggle || publisher == null) return;

            publisher.SetServiceRunning(isOn);

            // Re-sync immediately to ensure toggle reflects true operational outcome
            SyncToggleState();
        }

        private void SyncToggleState()
        {
            if (serviceToggle == null || publisher == null) return;

            bool actualRunning = publisher.isInitialized;
            if (serviceToggle.isOn != actualRunning)
            {
                _isSyncingToggle = true;
                serviceToggle.SetIsOnWithoutNotify(actualRunning);
                _isSyncingToggle = false;
            }
        }

        #endregion

        #region Dropdown & IP Selection

        private void InitIpDropdown()
        {
            if (ipDropdown == null) return;

            ipDropdown.onValueChanged.AddListener(OnDropdownSelectionChanged);
            RebuildDropdownOptions();
        }

        private void InitCustomIpInput()
        {
            if (customIpInputField == null) return;

            customIpInputField.onEndEdit.AddListener(OnCustomIpEndEdit);

            // Pre-populate with publisher's current IP if any
            if (publisher != null && !string.IsNullOrWhiteSpace(publisher.targetIp))
            {
                customIpInputField.text = publisher.targetIp.Trim();
            }
        }

        private void OnDropdownSelectionChanged(int index)
        {
            if (index == 0)
            {
                // Option 0: Use Custom IP
                // Dropdown ALWAYS remains visible! Show customIpInputField
                if (selectedIpText != null) selectedIpText.gameObject.SetActive(false);
                if (customIpInputField != null)
                {
                    customIpInputField.gameObject.SetActive(true);
                    customIpInputField.ActivateInputField();
                }
            }
            else
            {
                // Discovered IP selected
                int ipIndex = index - 1;
                if (ipIndex >= 0 && ipIndex < _discoveredIps.Count)
                {
                    string chosenIp = _discoveredIps[ipIndex];

                    if (customIpInputField != null) customIpInputField.gameObject.SetActive(false);
                    if (selectedIpText != null)
                    {
                        selectedIpText.gameObject.SetActive(true);
                        selectedIpText.text = chosenIp;
                    }

                    ApplyTargetIpAndPublish(chosenIp);
                }
            }
        }

        private void OnCustomIpEndEdit(string input)
        {
            string trimmed = input?.Trim() ?? "";
            if (!string.IsNullOrEmpty(trimmed))
            {
                ApplyTargetIpAndPublish(trimmed);
            }
        }

        private void ApplyTargetIpAndPublish(string ip)
        {
            if (publisher == null) return;

            if (_lastPublishedTargetIp == ip && publisher.isInitialized)
            {
                return; // Already targeting this IP
            }

            _lastPublishedTargetIp = ip;
            publisher.SwitchTargetIp(ip);

            // Refresh toggle state
            SyncToggleState();

            // Refresh dropdown icons
            RebuildDropdownOptions();
        }

        public void RebuildDropdownOptions()
        {
            if (ipDropdown == null) return;

            string currentSelected = (ipDropdown.value == 0) ? CustomIpOptionLabel :
                (ipDropdown.value - 1 < _discoveredIps.Count ? _discoveredIps[ipDropdown.value - 1] : "");

            var options = new List<TMP_Dropdown.OptionData>();

            // Option 0: Use Custom IP
            options.Add(new TMP_Dropdown.OptionData { text = CustomIpOptionLabel });

            // Discovered IPs with subscriber connection status indicator
            for (int i = 0; i < _discoveredIps.Count; i++)
            {
                string ip = _discoveredIps[i];
                bool isSubscribing = IsIpSubscribing(ip);
                Sprite icon = isSubscribing ? subscribingSprite : notSubscribingSprite;
                options.Add(new TMP_Dropdown.OptionData { text = ip, image = icon });
            }

            ipDropdown.ClearOptions();
            ipDropdown.AddOptions(options);

            // Restore selection index
            int newIdx = 0;
            if (currentSelected != CustomIpOptionLabel)
            {
                int found = _discoveredIps.IndexOf(currentSelected);
                if (found >= 0) newIdx = found + 1;
            }

            ipDropdown.SetValueWithoutNotify(newIdx);

            // Update UI visibility according to selection (Dropdown ALWAYS remains visible)
            if (newIdx == 0)
            {
                if (selectedIpText != null) selectedIpText.gameObject.SetActive(false);
                if (customIpInputField != null) customIpInputField.gameObject.SetActive(true);
            }
            else
            {
                if (customIpInputField != null) customIpInputField.gameObject.SetActive(false);
                if (selectedIpText != null)
                {
                    selectedIpText.gameObject.SetActive(true);
                    selectedIpText.text = (newIdx - 1 < _discoveredIps.Count) ? _discoveredIps[newIdx - 1] : "";
                }
            }
        }

        private bool IsIpSubscribing(string ip)
        {
            if (publisher == null || !publisher.isInitialized) return false;
            // Matches when this IP is targeted and has active matched subscribers
            if (publisher.targetIp == ip && publisher.hasMatchedSubscribers)
            {
                return true;
            }
            return false;
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
