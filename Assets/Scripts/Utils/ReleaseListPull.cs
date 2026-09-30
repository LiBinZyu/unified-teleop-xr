using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using AndroidIntent;

public class ReleaseListPull : MonoBehaviour
{
    private const string TAG = "[ReleaseListPull] ";

    [SerializeField] private TMP_Dropdown releaseDropdown;

    [Header("Release Note & Download UI")]
    [SerializeField] private GameObject releaseNotePanel;
    [SerializeField] private TMP_Text releaseNoteTitleText;
    [SerializeField] private TMP_Text releaseNoteText;
    [SerializeField] private Button downloadButton;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;

    private const string ghProxyPrefix = "https://ghproxy.net/";
    private static string CacheFilePath => Path.Combine(Application.persistentDataPath, "release_list_cache.json");
    private const string repoOwner = "Gento-Teleoperation-Apex";
    private const string repoName = "KernelMind_Apex_VR_Apk";

    [SerializeField] private string fileExtension = "_pxr.apk";
    private int perPageLimit = 30;
    private float networkCheckIntervalSeconds = 15.0f;

    private const int maxRetries = 4;
    private const float initialDelaySeconds = 2.0f;
    private const int requestTimeoutSeconds = 8;
    private const bool sortNewestFirst = true;

    public class ApkItem
    {
        public string Title;
        public string TagName;
        public string FileName;
        public string DownloadUrl;
        public string Body;
        public DateTime PublishedAt;
        public bool IsLatest;
    }

    private readonly List<ApkItem> _currentApkItems = new List<ApkItem>();
    private Coroutine _fetchCoroutine;
    private Coroutine _networkMonitorCoroutine;
    private NetworkReachability _lastReachability;
    private string _lastNetworkSignature = "";
    private bool _isFetching;
    private int _selectedApkIndex = -1;

    public TMP_Dropdown ReleaseDropdown => releaseDropdown;
    public IReadOnlyList<ApkItem> CurrentApkItems => _currentApkItems;

    void Start()
    {
        if (releaseDropdown == null)
        {
            releaseDropdown = GetComponent<TMP_Dropdown>();
        }

        if (releaseDropdown != null)
        {
            releaseDropdown.onValueChanged.AddListener(OnDropdownValueChanged);

            var hook = releaseDropdown.GetComponent<DropdownListHook>();
            if (hook == null)
            {
                hook = releaseDropdown.gameObject.AddComponent<DropdownListHook>();
            }
            hook.OnShown = HookDropdownItems;
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

        if (releaseNotePanel != null)
        {
            releaseNotePanel.SetActive(false);
        }

        if (UILanguageManager.Instance != null)
        {
            UILanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
        }

        _lastReachability = Application.internetReachability;
        _networkMonitorCoroutine = StartCoroutine(NetworkMonitorRoutine());

        StartCoroutine(DetachFromLocalizationRoutine());
        LoadCachedReleases();
        TriggerFetch("Initial startup");
    }

    private void LoadCachedReleases()
    {
        try
        {
            string path = CacheFilePath;
            if (!File.Exists(path)) return;

            string cachedJson = File.ReadAllText(path);
            if (string.IsNullOrEmpty(cachedJson)) return;

            var cachedItems = ParseAndFilterPxrApks(cachedJson, sortNewestFirst, fileExtension);
            if (cachedItems != null && cachedItems.Count > 0)
            {
                Logger.LogApp($"{TAG}Loaded {cachedItems.Count} release(s) from local cache file: {path}");
                ApplyResultsToDropdown(cachedItems);
            }
        }
        catch (Exception ex)
        {
            Logger.LogApp($"{TAG}Failed to load cached releases: {ex.Message}", LogType.Warning);
        }
    }

    void OnDestroy()
    {
        if (releaseDropdown != null)
        {
            releaseDropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);

            var hook = releaseDropdown.GetComponent<DropdownListHook>();
            if (hook != null)
            {
                hook.OnShown = null;
            }
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
            UILanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
        }

        if (_networkMonitorCoroutine != null)
        {
            StopCoroutine(_networkMonitorCoroutine);
            _networkMonitorCoroutine = null;
        }

        if (_fetchCoroutine != null)
        {
            StopCoroutine(_fetchCoroutine);
            _fetchCoroutine = null;
        }
    }

    private IEnumerator DetachFromLocalizationRoutine()
    {
        yield return null;
        UnbindFromUiLocalization();
    }

    private void UnbindFromUiLocalization()
    {
        try
        {
            var uiLoc = FindFirstObjectByType<UiLocalization>();
            if (uiLoc != null && releaseDropdown != null)
            {
                var field = typeof(UiLocalization).GetField("_dropdownBindings", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    var dict = field.GetValue(uiLoc) as Dictionary<TMP_Dropdown, List<string>>;
                    dict?.Remove(releaseDropdown);
                }
            }
        }
        catch
        {
            // Fallback safe
        }
    }

    private void OnLanguageChanged()
    {
        UnbindFromUiLocalization();
        StartCoroutine(RestoreDropdownOptionsRoutine());
    }

    private IEnumerator RestoreDropdownOptionsRoutine()
    {
        yield return null;
        UnbindFromUiLocalization();
        if (_currentApkItems.Count > 0)
        {
            RebuildDropdownOptions();
        }
    }

    #region Network Monitor (Non-blocking Background Thread)

    private IEnumerator NetworkMonitorRoutine()
    {
        var wait = new WaitForSecondsRealtime(Mathf.Max(5.0f, networkCheckIntervalSeconds));

        // Initial signature check in background
        var initTask = Task.Run(GetNetworkSignatureBackground);
        while (!initTask.IsCompleted)
        {
            yield return null;
        }
        _lastNetworkSignature = initTask.Result;

        while (true)
        {
            yield return wait;

            NetworkReachability currentReachability = Application.internetReachability;

            // Offload network adapter signature polling to thread pool to prevent main-thread hitching
            var sigTask = Task.Run(GetNetworkSignatureBackground);
            while (!sigTask.IsCompleted)
            {
                yield return null;
            }
            string currentSignature = sigTask.Result;

            bool becameReachable = _lastReachability == NetworkReachability.NotReachable &&
                                   currentReachability != NetworkReachability.NotReachable;

            bool networkSwitched = currentReachability != NetworkReachability.NotReachable &&
                                   !string.IsNullOrEmpty(currentSignature) &&
                                   !string.IsNullOrEmpty(_lastNetworkSignature) &&
                                   currentSignature != _lastNetworkSignature;

            _lastReachability = currentReachability;

            if (becameReachable || networkSwitched)
            {
                string reason = becameReachable ? "connection restored" : "network switched";
                _lastNetworkSignature = currentSignature;
                TriggerFetch($"Network change ({reason})");
            }
            else if (!string.IsNullOrEmpty(currentSignature))
            {
                _lastNetworkSignature = currentSignature;
            }
        }
    }

    private static string GetNetworkSignatureBackground()
    {
        try
        {
            var sb = new StringBuilder();
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            sb.Append(addr.Address).Append(';');
                        }
                    }
                    foreach (var gw in ipProps.GatewayAddresses)
                    {
                        sb.Append("gw:").Append(gw.Address).Append(';');
                    }
                }
            }
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    #endregion

    #region Fetch & Retry Logic

    [ContextMenu("Context UI / Refresh Release List")]
    public void RefreshReleaseList()
    {
        if (_isFetching) return;
        TriggerFetch("Manual context menu refresh");
    }

    private void TriggerFetch(string reason = "Unknown")
    {
        Logger.LogApp($"{TAG}Triggering pull (Reason: {reason})...");
        if (_fetchCoroutine != null)
        {
            StopCoroutine(_fetchCoroutine);
        }
        _fetchCoroutine = StartCoroutine(FetchReleasesRoutine());
    }

    private IEnumerator FetchReleasesRoutine()
    {
        _isFetching = true;
        // 仅在当前没有任何缓存版本时才显示 Loading，绝不清除已有缓存和UI
        if (_currentApkItems.Count == 0)
        {
            SetDropdownSingleOption("Loading releases...");
        }

        int limit = Mathf.Clamp(perPageLimit, 1, 100);
        string apiUrl = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases?per_page={limit}";
        float currentDelay = initialDelaySeconds;
        bool isSuccess = false;
        string lastErrorCode = "Unknown";

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            Logger.LogApp($"{TAG}Attempting to pull releases from GitHub (Attempt {attempt}/{maxRetries}): {apiUrl}");

            using (UnityWebRequest request = UnityWebRequest.Get(apiUrl))
            {
                request.SetRequestHeader("User-Agent", "Unity-XR-App-Updater");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.timeout = requestTimeoutSeconds;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string jsonResponse = request.downloadHandler.text;
                    try
                    {
                        var parsedItems = ParseAndFilterPxrApks(jsonResponse, sortNewestFirst, fileExtension);
                        Logger.LogApp($"{TAG}Successfully pulled {parsedItems.Count} release(s) from GitHub.");

                        // 拉取成功后才写入独立本地文件缓存！
                        try
                        {
                            File.WriteAllText(CacheFilePath, jsonResponse);
                        }
                        catch (Exception ioEx)
                        {
                            Logger.LogApp($"{TAG}Failed to write cache file: {ioEx.Message}", LogType.Warning);
                        }

                        ApplyResultsToDropdown(parsedItems);
                        isSuccess = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastErrorCode = ex.Message;
                        Logger.LogApp($"{TAG}Pull parse error on attempt {attempt}: {lastErrorCode}", LogType.Warning);
                    }
                }
                else
                {
                    lastErrorCode = request.responseCode > 0 
                        ? request.responseCode.ToString() 
                        : request.result.ToString();
                    Logger.LogApp($"{TAG}Pull attempt {attempt} failed: {lastErrorCode}", LogType.Warning);
                }
            }

            if (attempt < maxRetries)
            {
                yield return new WaitForSecondsRealtime(currentDelay);
                currentDelay *= 2f;
            }
        }

        if (!isSuccess)
        {
            Logger.LogApp($"{TAG}Failed to fetch releases after {maxRetries} attempts: {lastErrorCode}", LogType.Error);
            // 仅在完全没有缓存时才覆盖下拉框显示错误，已有缓存继续保留展示
            if (_currentApkItems.Count == 0)
            {
                SetDropdownSingleOption($"pull failed {lastErrorCode}");
            }
        }

        _isFetching = false;
        _fetchCoroutine = null;
    }

    #endregion

    #region JSON Parsing

    private static List<ApkItem> ParseAndFilterPxrApks(string rawJson, bool newestFirst, string extension)
    {
        string wrappedJson = "{\"items\":" + rawJson + "}";
        GitHubReleaseArrayWrapper wrapper = JsonUtility.FromJson<GitHubReleaseArrayWrapper>(wrappedJson);

        var list = new List<ApkItem>();
        if (wrapper == null || wrapper.items == null || wrapper.items.Length == 0)
        {
            return list;
        }

        string latestTagName = wrapper.items[0]?.tag_name;

        foreach (var release in wrapper.items)
        {
            if (release.assets == null) continue;

            DateTime.TryParse(release.published_at, out DateTime publishTime);
            bool isLatestRelease = !string.IsNullOrEmpty(latestTagName) && release.tag_name == latestTagName;

            foreach (var asset in release.assets)
            {
                if (!string.IsNullOrEmpty(asset.name) && 
                    asset.name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    string releaseTitle = !string.IsNullOrWhiteSpace(release.name) ? release.name : release.tag_name;

                    list.Add(new ApkItem
                    {
                        Title = releaseTitle,
                        TagName = release.tag_name,
                        FileName = asset.name,
                        DownloadUrl = asset.browser_download_url,
                        Body = release.body,
                        PublishedAt = publishTime,
                        IsLatest = isLatestRelease
                    });
                }
            }
        }

        var sortedList = newestFirst 
            ? list.OrderByDescending(x => x.PublishedAt).ToList() 
            : list.OrderBy(x => x.PublishedAt).ToList();

        if (sortedList.Count > 0)
        {
            sortedList[0].IsLatest = true;
        }

        return sortedList;
    }

    #endregion

    #region UI Presentation & Navigation

    private void ApplyResultsToDropdown(List<ApkItem> items)
    {
        if (releaseDropdown == null) return;

        _currentApkItems.Clear();
        _currentApkItems.AddRange(items);

        RebuildDropdownOptions();

        // Keep panel open only if user already had it open; do not auto-pop on pull
        bool keepPanelActive = releaseNotePanel != null && releaseNotePanel.activeSelf;
        SelectItemByIndex(0, showPanel: keepPanelActive);
    }

    private void RebuildDropdownOptions()
    {
        if (releaseDropdown == null) return;

        releaseDropdown.ClearOptions();

        if (_currentApkItems.Count == 0)
        {
            releaseDropdown.options.Add(new TMP_Dropdown.OptionData("No releases found with extension: " + fileExtension));
            releaseDropdown.RefreshShownValue();
            return;
        }

        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>(_currentApkItems.Count);
        foreach (var item in _currentApkItems)
        {
            string displayText = item.IsLatest ? $"<b>{item.FileName}</b>" : item.FileName;
            options.Add(new TMP_Dropdown.OptionData(displayText));
        }

        releaseDropdown.AddOptions(options);

        int targetIndex = Mathf.Clamp(_selectedApkIndex >= 0 ? _selectedApkIndex : 0, 0, _currentApkItems.Count - 1);
        releaseDropdown.SetValueWithoutNotify(targetIndex);
        releaseDropdown.RefreshShownValue();
    }

    private void OnDropdownValueChanged(int index)
    {
        if (index < 0 || index >= _currentApkItems.Count) return;
        SelectItemByIndex(index, showPanel: true);
    }

    private void HookDropdownItems()
    {
        if (releaseDropdown == null) return;
        Transform dropdownList = releaseDropdown.transform.Find("Dropdown List");
        if (dropdownList == null) return;

        var toggles = dropdownList.GetComponentsInChildren<Toggle>(false);
        for (int i = 0; i < toggles.Length; i++)
        {
            int index = i;
            var pass = toggles[i].GetComponent<DropdownItemClickPass>() ?? 
                       toggles[i].gameObject.AddComponent<DropdownItemClickPass>();
            pass.Index = index;
            pass.OnClick = OnDropdownItemClicked;
        }
    }

    private void OnDropdownItemClicked(int index)
    {
        SelectItemByIndex(index, showPanel: true);
        if (releaseDropdown != null)
        {
            releaseDropdown.Hide();
        }
    }

    private void SelectItemByIndex(int index, bool showPanel = false)
    {
        if (index < 0 || index >= _currentApkItems.Count) return;

        _selectedApkIndex = index;

        if (releaseDropdown != null && releaseDropdown.value != index)
        {
            releaseDropdown.SetValueWithoutNotify(index);
            releaseDropdown.RefreshShownValue();
        }

        PopulateReleaseNoteContent(_currentApkItems[index]);
        UpdateNavigationButtons();

        if (showPanel && releaseNotePanel != null)
        {
            releaseNotePanel.SetActive(true);
        }
    }

    private void PopulateReleaseNoteContent(ApkItem item)
    {
        string rawBody = !string.IsNullOrWhiteSpace(item.Body) 
            ? item.Body 
            : "No release notes provided.";
        string richTextBody = MarkdownToTmpConverter.ConvertToRichText(rawBody);

        if (releaseNoteTitleText != null)
        {
            releaseNoteTitleText.text = item.Title;
            if (releaseNoteText != null)
            {
                releaseNoteText.text = richTextBody;
            }
        }
        else if (releaseNoteText != null)
        {
            releaseNoteText.text = $"{item.Title}\n\n{richTextBody}";
        }
    }

    private void OnPreviousButtonClicked()
    {
        int currentIndex = GetValidCurrentIndex();
        if (currentIndex > 0)
        {
            SelectItemByIndex(currentIndex - 1, showPanel: true);
        }
    }

    private void OnNextButtonClicked()
    {
        int currentIndex = GetValidCurrentIndex();
        if (currentIndex >= 0 && currentIndex < _currentApkItems.Count - 1)
        {
            SelectItemByIndex(currentIndex + 1, showPanel: true);
        }
    }

    private int GetValidCurrentIndex()
    {
        if (_currentApkItems.Count == 0) return -1;
        return _selectedApkIndex >= 0 ? _selectedApkIndex : (releaseDropdown != null ? releaseDropdown.value : -1);
    }

    private void UpdateNavigationButtons()
    {
        int currentIndex = GetValidCurrentIndex();

        if (previousButton != null)
        {
            previousButton.interactable = currentIndex > 0;
        }

        if (nextButton != null)
        {
            nextButton.interactable = currentIndex >= 0 && currentIndex < _currentApkItems.Count - 1;
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

        // GHProxy 加速
        if (!url.StartsWith(ghProxyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            url = ghProxyPrefix + url;
        }

        Logger.LogApp($"{TAG}Launching browser download: {url}");
        LaunchBrowser(url);
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
        releaseDropdown.options.Add(new TMP_Dropdown.OptionData(text));
        releaseDropdown.value = 0;
        releaseDropdown.RefreshShownValue();
    }

    public string GetSelectedApkDownloadUrl()
    {
        int idx = GetValidCurrentIndex();
        if (idx >= 0 && idx < _currentApkItems.Count)
        {
            return _currentApkItems[idx].DownloadUrl;
        }
        return null;
    }

    #endregion

    #region Lightweight Dropdown Item Click Handling

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

    #region JSON Serialization Models

    [Serializable]
    private class GitHubReleaseArrayWrapper
    {
        public GitHubReleaseItem[] items;
    }

    [Serializable]
    private class GitHubReleaseItem
    {
        public string tag_name;
        public string name;
        public string published_at;
        public string body;
        public GitHubAssetItem[] assets;
    }

    [Serializable]
    private class GitHubAssetItem
    {
        public string name;
        public string browser_download_url;
    }

    #endregion
}