using System;
using System.Collections;
using System.Collections.Generic;
using Network.InterfaceInfo;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UTXR.Core;
using Logger = UTXR.Core.Logger;
using UTXR.Network;
using UTXR.UI.Localization;

namespace UTXR.UI
{
    /// <summary>
    /// UI controller responsible for presenting GitHub releases, handling version dropdown selection,
    /// markdown release notes rendering, download actions, and lifecycle management.
    /// Delegates all network, caching, parsing, and state operations to IReleaseService.
    /// </summary>
    [DisallowMultipleComponent]
    public class ReleaseListUI : MonoBehaviour
    {
        private const string TAG = "[ReleaseListUI] ";

        [SerializeField] private TMP_Dropdown releaseDropdown;

        [Header("Release Note & Download UI")]
        [SerializeField] private GameObject releaseNotePanel;
        [SerializeField] private TMP_Text releaseNoteTitleText;
        [SerializeField] private TMP_Text releaseNoteText;
        [SerializeField] private Button downloadButton;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;

        [Header("Configuration")]
        [SerializeField] private string repoOwner = "LiBinZyu";
        [SerializeField] private string repoName = "unified-teleop-xr";
        [SerializeField] private string fileExtension = "_pxr.apk";
        [SerializeField] private int perPageLimit = 30;

        private const string GhProxyPrefix = "https://ghproxy.net/";

        private ReleaseService _service;
        private NetworkInfoService _subscribedNetworkService;
        private int _selectedApkIndex = -1;
        private bool _wasOffline;

        private readonly List<TMP_Dropdown.OptionData> _cachedOptionData = new List<TMP_Dropdown.OptionData>();
        private readonly List<Toggle> _toggleBuffer = new List<Toggle>();

        public TMP_Dropdown ReleaseDropdown => releaseDropdown;
        public IReleaseService Service => _service;
        public IReadOnlyList<ReleaseInfo> Releases => _service != null ? _service.Releases : Array.Empty<ReleaseInfo>();
        public IReadOnlyList<ReleaseInfo> CurrentApkItems => Releases;

        protected virtual void Awake()
        {
            if (releaseDropdown == null)
            {
                releaseDropdown = GetComponent<TMP_Dropdown>();
            }

            _service = new ReleaseService(
                repoOwner: repoOwner,
                repoName: repoName,
                fileExtension: fileExtension,
                perPageLimit: perPageLimit
            );

            _service.OnReleasesChanged += HandleReleasesChanged;
            _service.OnStateChanged += HandleStateChanged;
        }

        protected virtual void OnEnable()
        {
            if (releaseDropdown != null)
            {
                releaseDropdown.onValueChanged.AddListener(OnDropdownValueChanged);
            }

            if (downloadButton != null)
            {
                downloadButton.onClick.AddListener(OnDownloadButtonClicked);
            }

            if (previousButton != null)
            {
                previousButton.onClick.AddListener(OnPreviousButtonClicked);
            }

            if (nextButton != null)
            {
                nextButton.onClick.AddListener(OnNextButtonClicked);
            }

            if (UILanguageManager.Instance != null)
            {
                UILanguageManager.Instance.OnLanguageChanged += HandleLanguageChanged;
            }

            SubscribeNetworkService();
            UnbindFromUiLocalization();
        }

        protected virtual void Start()
        {
            HookDropdownItems();

            if (releaseNotePanel != null)
            {
                releaseNotePanel.SetActive(false);
            }

            // Immediately display cached releases if available, avoiding blank UI
            bool hasCache = _service.LoadCachedReleases();
            if (!hasCache)
            {
                SetDropdownSingleOption("Loading releases...");
            }

            // Trigger initial async fetch
            _service.RequestRefresh();
        }

        protected virtual void OnDisable()
        {
            if (releaseDropdown != null)
            {
                releaseDropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
            }

            if (downloadButton != null)
            {
                downloadButton.onClick.RemoveListener(OnDownloadButtonClicked);
            }

            if (previousButton != null)
            {
                previousButton.onClick.RemoveListener(OnPreviousButtonClicked);
            }

            if (nextButton != null)
            {
                nextButton.onClick.RemoveListener(OnNextButtonClicked);
            }

            if (UILanguageManager.Instance != null)
            {
                UILanguageManager.Instance.OnLanguageChanged -= HandleLanguageChanged;
            }

            UnsubscribeNetworkService();

            if (_service != null && _service.State == FetchState.Fetching)
            {
                _service.Cancel();
            }
        }

        protected virtual void OnDestroy()
        {
            if (releaseDropdown != null)
            {
                var hook = releaseDropdown.GetComponent<DropdownListHook>();
                if (hook != null)
                {
                    hook.OnShown = null;
                }
            }

            if (_service != null)
            {
                _service.OnReleasesChanged -= HandleReleasesChanged;
                _service.OnStateChanged -= HandleStateChanged;
                _service.Dispose();
                _service = null;
            }
        }

        #region Network Monitoring Integration

        private void SubscribeNetworkService()
        {
            if (_subscribedNetworkService != null) return;

            var netService = NetworkInfoService.Instance;
            if (netService != null)
            {
                _subscribedNetworkService = netService;
                _subscribedNetworkService.OnInterfacesChanged += HandleNetworkInterfacesChanged;
            }
        }

        private void UnsubscribeNetworkService()
        {
            if (_subscribedNetworkService != null)
            {
                _subscribedNetworkService.OnInterfacesChanged -= HandleNetworkInterfacesChanged;
                _subscribedNetworkService = null;
            }
        }

        private void HandleNetworkInterfacesChanged(int version)
        {
            var reachability = Application.internetReachability;
            if (reachability == NetworkReachability.NotReachable)
            {
                _wasOffline = true;
                return;
            }

            // Demand-driven: Only request refresh if network was offline, previous fetch failed, or release list is empty
            if (_wasOffline || _service.State == FetchState.Failed || _service.Count == 0)
            {
                _wasOffline = false;
                Logger.LogApp($"{TAG}Network connectivity restored, requesting release refresh...");
                _service.RequestRefresh();
            }
        }

        #endregion

        #region Localization Integration

        private void UnbindFromUiLocalization()
        {
            if (releaseDropdown == null) return;
            try
            {
                var uiLoc = UiLocalization.Instance ?? FindFirstObjectByType<UiLocalization>();
                uiLoc?.UnbindDropdown(releaseDropdown);
            }
            catch
            {
                // Fallback safe
            }
        }

        private void HandleLanguageChanged()
        {
            UnbindFromUiLocalization();

            // Refresh currently shown release note if visible
            if (_selectedApkIndex >= 0 && _service.TryGetRelease(_selectedApkIndex, out var item))
            {
                PopulateReleaseNoteContent(item);
            }
        }

        #endregion

        #region Service State and Data Handlers

        private void HandleStateChanged(FetchState state)
        {
            if (state == FetchState.Failed && _service.Count == 0)
            {
                SetDropdownSingleOption("Pull failed. Please check network.");
            }
            else if (state == FetchState.Fetching && _service.Count == 0)
            {
                SetDropdownSingleOption("Loading releases...");
            }
        }

        private void HandleReleasesChanged()
        {
            if (releaseDropdown == null || _service == null) return;

            string previouslySelectedFileName = null;
            if (_service.TryGetRelease(_selectedApkIndex, out var currentSelected))
            {
                previouslySelectedFileName = currentSelected.FileName;
            }

            RebuildDropdownOptions();

            // Retain user's current selection across updates instead of resetting to index 0
            int targetIndex = 0;
            if (!string.IsNullOrEmpty(previouslySelectedFileName))
            {
                for (int i = 0; i < _service.Count; i++)
                {
                    if (_service.TryGetRelease(i, out var info) && info.FileName == previouslySelectedFileName)
                    {
                        targetIndex = i;
                        break;
                    }
                }
            }

            bool keepPanelActive = releaseNotePanel != null && releaseNotePanel.activeSelf;
            SelectItemByIndex(targetIndex, showPanel: keepPanelActive);
        }

        private void RebuildDropdownOptions()
        {
            if (releaseDropdown == null || _service == null) return;

            releaseDropdown.ClearOptions();

            int count = _service.Count;
            if (count == 0)
            {
                _cachedOptionData.Clear();
                _cachedOptionData.Add(new TMP_Dropdown.OptionData("No releases found with extension: " + fileExtension));
                releaseDropdown.AddOptions(_cachedOptionData);
                releaseDropdown.RefreshShownValue();
                return;
            }

            _cachedOptionData.Clear();
            for (int i = 0; i < count; i++)
            {
                if (_service.TryGetRelease(i, out var item))
                {
                    string dateSuffix = item.UpdatedAt != DateTime.MinValue
                        ? $" ({item.UpdatedAt:yyyy-MM-dd})"
                        : string.Empty;
                    string displayText = item.IsLatest
                        ? $"<b>{item.FileName}{dateSuffix}</b>"
                        : $"{item.FileName}{dateSuffix}";
                    _cachedOptionData.Add(new TMP_Dropdown.OptionData(displayText));
                }
            }

            releaseDropdown.AddOptions(_cachedOptionData);
        }

        #endregion

        #region UI Presentation & Navigation

        public void SelectItemByIndex(int index, bool showPanel = false)
        {
            if (_service == null || !_service.TryGetRelease(index, out var info)) return;

            _selectedApkIndex = index;

            if (releaseDropdown != null && releaseDropdown.value != index)
            {
                releaseDropdown.SetValueWithoutNotify(index);
                releaseDropdown.RefreshShownValue();
            }

            PopulateReleaseNoteContent(info);
            UpdateNavigationButtons();

            if (showPanel && releaseNotePanel != null)
            {
                releaseNotePanel.SetActive(true);
            }
        }

        private void PopulateReleaseNoteContent(ReleaseInfo item)
        {
            if (item == null) return;

            // Convert and cache markdown to rich text on demand
            if (item.CachedRichTextBody == null)
            {
                string rawBody = !string.IsNullOrWhiteSpace(item.Body)
                    ? item.Body
                    : "No release notes provided.";
                item.CachedRichTextBody = MarkdownToTmpConverter.ConvertToRichText(rawBody);
            }

            string dateSuffix = item.UpdatedAt != DateTime.MinValue
                ? $" ({item.UpdatedAt:yyyy-MM-dd})"
                : string.Empty;

            if (releaseNoteTitleText != null)
            {
                releaseNoteTitleText.text = $"{item.Title}{dateSuffix}";
                if (releaseNoteText != null)
                {
                    releaseNoteText.text = item.CachedRichTextBody;
                }
            }
            else if (releaseNoteText != null)
            {
                releaseNoteText.text = $"{item.Title}{dateSuffix}\n\n{item.CachedRichTextBody}";
            }
        }

        private void OnDropdownValueChanged(int index)
        {
            if (index < 0 || _service == null || index >= _service.Count) return;
            SelectItemByIndex(index, showPanel: true);
        }

        private void OnPreviousButtonClicked()
        {
            if (_selectedApkIndex > 0)
            {
                SelectItemByIndex(_selectedApkIndex - 1, showPanel: true);
            }
        }

        private void OnNextButtonClicked()
        {
            if (_service != null && _selectedApkIndex >= 0 && _selectedApkIndex < _service.Count - 1)
            {
                SelectItemByIndex(_selectedApkIndex + 1, showPanel: true);
            }
        }

        private void UpdateNavigationButtons()
        {
            int count = _service != null ? _service.Count : 0;

            if (previousButton != null)
            {
                previousButton.interactable = _selectedApkIndex > 0;
            }

            if (nextButton != null)
            {
                nextButton.interactable = _selectedApkIndex >= 0 && _selectedApkIndex < count - 1;
            }
        }

        private void OnDownloadButtonClicked()
        {
            OpenDownloadUrl();
        }

        public void OpenDownloadUrl(string downloadLink = null)
        {
            string url = !string.IsNullOrWhiteSpace(downloadLink) ? downloadLink.Trim() : GetSelectedApkDownloadUrl();
            if (string.IsNullOrEmpty(url))
            {
                Logger.LogApp($"{TAG}Download failed: Selected APK download URL is empty.", LogType.Warning);
                return;
            }

            if (!url.StartsWith(GhProxyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                url = GhProxyPrefix + url;
            }

            Logger.LogApp($"{TAG}Launching browser download: {url}");
            LaunchBrowser(url);
        }

        public string GetSelectedApkDownloadUrl()
        {
            if (_service != null && _service.TryGetRelease(_selectedApkIndex, out var item))
            {
                return item.DownloadUrl;
            }
            return null;
        }

        private void LaunchBrowser(string url)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", url))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW", uri))
                {
                    intent.Call<AndroidJavaObject>("addCategory", "android.intent.category.BROWSABLE");
                    intent.Call<AndroidJavaObject>("addFlags", 0x14000000); // FLAG_ACTIVITY_NEW_TASK | FLAG_ACTIVITY_CLEAR_TOP
                    currentActivity.Call("startActivity", intent);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogApp($"{TAG}Android Intent launch failed: {ex.Message}. Falling back to Application.OpenURL.", LogType.Warning);
            }
#endif
            Application.OpenURL(url);
        }

        private void SetDropdownSingleOption(string text)
        {
            if (releaseDropdown == null) return;

            releaseDropdown.ClearOptions();
            _cachedOptionData.Clear();
            _cachedOptionData.Add(new TMP_Dropdown.OptionData(text));
            releaseDropdown.AddOptions(_cachedOptionData);
            releaseDropdown.value = 0;
            releaseDropdown.RefreshShownValue();
        }

        [ContextMenu("Context UI / Refresh Release List")]
        public void RefreshReleaseList()
        {
            if (_service != null)
            {
                _service.RequestRefresh(force: true);
            }
        }

        #endregion

        #region Dropdown Click Hooking

        private void HookDropdownItems()
        {
            if (releaseDropdown == null) return;

            var hook = releaseDropdown.GetComponent<DropdownListHook>();
            if (hook == null)
            {
                hook = releaseDropdown.gameObject.AddComponent<DropdownListHook>();
            }
            hook.OnShown = HandleDropdownShown;
        }

        private void HandleDropdownShown()
        {
            if (releaseDropdown == null) return;
            Transform dropdownList = releaseDropdown.transform.Find("Dropdown List");
            if (dropdownList == null) return;

            _toggleBuffer.Clear();
            dropdownList.GetComponentsInChildren(false, _toggleBuffer);
            for (int i = 0; i < _toggleBuffer.Count; i++)
            {
                int index = i;
                var toggle = _toggleBuffer[i];
                if (toggle == null) continue;

                var pass = toggle.GetComponent<DropdownItemClickPass>();
                if (pass == null)
                {
                    pass = toggle.gameObject.AddComponent<DropdownItemClickPass>();
                }
                pass.Index = index;
                pass.OnClick = OnDropdownItemClicked;
            }
            _toggleBuffer.Clear();
        }

        private void OnDropdownItemClicked(int index)
        {
            SelectItemByIndex(index, showPanel: true);
            if (releaseDropdown != null)
            {
                releaseDropdown.Hide();
            }
        }

        private class DropdownItemClickPass : MonoBehaviour, IPointerClickHandler
        {
            public int Index;
            public Action<int> OnClick;

            public void OnPointerClick(PointerEventData eventData)
            {
                OnClick?.Invoke(Index);
            }
        }

        private class DropdownListHook : MonoBehaviour
        {
            public Action OnShown;

            private void OnTransformChildrenChanged()
            {
                if (transform.Find("Dropdown List") != null)
                {
                    StartCoroutine(NotifyNextFrame());
                }
            }

            private IEnumerator NotifyNextFrame()
            {
                yield return null;
                OnShown?.Invoke();
            }
        }

        #endregion
    }

    /// <summary>
    /// Backward-compatible subclass keeping the historical ReleaseListPull MonoBehaviour name
    /// so existing scenes, prefabs, and script references function without modifications.
    /// </summary>
    public class ReleaseListPull : ReleaseListUI
    {
        /// <summary>
        /// Backward-compatible ApkItem alias to ReleaseInfo.
        /// </summary>
        public class ApkItem : ReleaseInfo
        {
            public ApkItem(string title, string tagName, string fileName, string downloadUrl, string body, DateTime updatedAt, bool isLatest)
                : base(title, tagName, fileName, downloadUrl, body, updatedAt, isLatest)
            {
            }
        }
    }
}
