using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UTXR.Core;
using Logger = UTXR.Core.Logger;

namespace UTXR.Network
{
    /// <summary>
    /// Explicit fetch states for release lifecycle management.
    /// </summary>
    public enum FetchState : byte
    {
        Idle = 0,
        Fetching,
        Succeeded,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Lightweight, zero-GC descriptor representing a parsed release item on the hot path.
    /// </summary>
    public class ReleaseInfo
    {
        public string Title { get; }
        public string TagName { get; }
        public string FileName { get; }
        public string DownloadUrl { get; }
        public string Body { get; }
        public DateTime UpdatedAt { get; }
        public DateTime PublishedAt => UpdatedAt;
        public bool IsLatest { get; internal set; }

        /// <summary>
        /// Cached rich text body to avoid repeated markdown regex parsing. Managed by UI on demand.
        /// </summary>
        public string CachedRichTextBody { get; set; }

        public ReleaseInfo(string title, string tagName, string fileName, string downloadUrl, string body, DateTime updatedAt, bool isLatest)
        {
            Title = title ?? string.Empty;
            TagName = tagName ?? string.Empty;
            FileName = fileName ?? string.Empty;
            DownloadUrl = downloadUrl ?? string.Empty;
            Body = body ?? string.Empty;
            UpdatedAt = updatedAt;
            IsLatest = isLatest;
        }
    }

    /// <summary>
    /// Minimal standardized interface for release queries, state observation, and refresh execution.
    /// </summary>
    public interface IReleaseService : IDisposable
    {
        int Count { get; }
        FetchState State { get; }
        IReadOnlyList<ReleaseInfo> Releases { get; }

        bool TryGetRelease(int index, out ReleaseInfo info);

        bool RequestRefresh(bool force = false);
        void Cancel();

        event Action OnReleasesChanged;
        event Action<FetchState> OnStateChanged;
    }

    /// <summary>
    /// Pure C# ReleaseService responsible for querying GitHub Releases, parsing, caching,
    /// retrying with backoff, ETag conditional requests, and managing release lifecycle.
    /// Decoupled from MonoBehaviour, TMPro, and UnityEngine.UI.
    /// </summary>
    public class ReleaseService : IReleaseService
    {
        private const string TAG = "[ReleaseService] ";

        private readonly string _repoOwner;
        private readonly string _repoName;
        private readonly string _fileExtension;
        private readonly int _perPageLimit;
        private readonly string _cacheFilePath;
        private readonly int _requestTimeoutSeconds;
        private readonly int _maxRetries;
        private readonly float _initialDelaySeconds;
        private readonly int _maxResponseSizeBytes;

        private readonly List<ReleaseInfo> _releases = new List<ReleaseInfo>();
        private FetchState _state = FetchState.Idle;
        private string _cachedETag = string.Empty;
        private UnityWebRequest _currentRequest;
        private CancellationTokenSource _cts;
        private bool _isDisposed;
        private bool _hasPendingRefresh;
        private bool _pendingForce;
        private float _lastSuccessfulFetchTime = -100f;

        public int Count => _releases.Count;
        public FetchState State => _state;
        public IReadOnlyList<ReleaseInfo> Releases => _releases;

        public event Action OnReleasesChanged;
        public event Action<FetchState> OnStateChanged;

        public ReleaseService(
            string repoOwner = "LiBinZyu",
            string repoName = "unified-teleop-xr",
            string fileExtension = "_pxr.apk",
            string cacheFilePath = null,
            int perPageLimit = 30,
            int requestTimeoutSeconds = 8,
            int maxRetries = 3,
            float initialDelaySeconds = 2.0f,
            int maxResponseSizeBytes = 4 * 1024 * 1024)
        {
            _repoOwner = repoOwner;
            _repoName = repoName;
            _fileExtension = fileExtension;
            _perPageLimit = perPageLimit;
            _cacheFilePath = !string.IsNullOrEmpty(cacheFilePath)
                ? cacheFilePath
                : Path.Combine(Application.persistentDataPath, "release_list_cache.json");
            _requestTimeoutSeconds = requestTimeoutSeconds;
            _maxRetries = maxRetries;
            _initialDelaySeconds = initialDelaySeconds;
            _maxResponseSizeBytes = maxResponseSizeBytes;
        }

        /// <summary>
        /// Reads release information by index directly from memory.
        /// Hot path: O(1), 0 GC.
        /// </summary>
        public bool TryGetRelease(int index, out ReleaseInfo info)
        {
            if ((uint)index < (uint)_releases.Count)
            {
                info = _releases[index];
                return true;
            }

            info = null;
            return false;
        }

        /// <summary>
        /// Loads cached releases synchronously from local disk into memory on startup.
        /// Does not block on subsequent frames.
        /// </summary>
        public bool LoadCachedReleases()
        {
            if (_isDisposed) return false;

            try
            {
                if (!File.Exists(_cacheFilePath)) return false;

                string cachedJson = File.ReadAllText(_cacheFilePath);
                if (string.IsNullOrEmpty(cachedJson)) return false;

                var items = ParseAndFilterReleases(cachedJson, _fileExtension);
                if (items != null && items.Count > 0)
                {
                    _releases.Clear();
                    _releases.AddRange(items);

                    string etagPath = _cacheFilePath + ".etag";
                    if (File.Exists(etagPath))
                    {
                        try
                        {
                            _cachedETag = File.ReadAllText(etagPath).Trim();
                        }
                        catch
                        {
                            _cachedETag = string.Empty;
                        }
                    }

                    Logger.LogApp($"{TAG}Loaded {_releases.Count} release(s) from local cache: {_cacheFilePath}");
                    OnReleasesChanged?.Invoke();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogApp($"{TAG}Failed to load cached releases: {ex.Message}", LogType.Warning);
            }

            return false;
        }

        /// <summary>
        /// Requests an asynchronous refresh. Merges duplicate concurrent requests into at most one queued fetch.
        /// </summary>
        public bool RequestRefresh(bool force = false)
        {
            if (_isDisposed) return false;

            if (_state == FetchState.Fetching)
            {
                _hasPendingRefresh = true;
                if (force) _pendingForce = true;
                return false;
            }

            // Cooldown check for non-forced refresh (5 seconds) to coalesce rapid network interface changes
            if (!force && _lastSuccessfulFetchTime > 0f && Time.realtimeSinceStartup - _lastSuccessfulFetchTime < 5.0f)
            {
                return false;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            _ = ExecuteFetchLoopAsync(force, _cts.Token);
            return true;
        }

        /// <summary>
        /// Cancels any currently active HTTP request and backoff wait.
        /// </summary>
        public void Cancel()
        {
            if (_isDisposed) return;

            if (_state == FetchState.Fetching)
            {
                _cts?.Cancel();
                try
                {
                    _currentRequest?.Abort();
                }
                catch { }

                SetState(FetchState.Cancelled);
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Cancel();

            _cts?.Dispose();
            _cts = null;

            try
            {
                _currentRequest?.Dispose();
            }
            catch { }
            _currentRequest = null;

            OnReleasesChanged = null;
            OnStateChanged = null;
        }

        private void SetState(FetchState newState)
        {
            if (_state != newState)
            {
                _state = newState;
                OnStateChanged?.Invoke(_state);
            }
        }

        private async Task ExecuteFetchLoopAsync(bool force, CancellationToken ct)
        {
            SetState(FetchState.Fetching);

            int limit = Math.Clamp(_perPageLimit, 1, 100);
            string apiUrl = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases?per_page={limit}";
            float currentDelay = _initialDelaySeconds;
            bool isSuccess = false;
            string lastError = "Unknown";

            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                if (ct.IsCancellationRequested) break;

                Logger.LogApp($"{TAG}Attempting pull from GitHub (Attempt {attempt}/{_maxRetries}): {apiUrl}");

                using (var request = UnityWebRequest.Get(apiUrl))
                {
                    _currentRequest = request;
                    request.SetRequestHeader("User-Agent", "Unity-XR-App-Updater");
                    request.SetRequestHeader("Accept", "application/vnd.github+json");
                    request.timeout = _requestTimeoutSeconds;

                    if (!force && !string.IsNullOrEmpty(_cachedETag))
                    {
                        request.SetRequestHeader("If-None-Match", _cachedETag);
                    }

                    try
                    {
                        await SendWebRequestAsync(request, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        SetState(FetchState.Cancelled);
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        Logger.LogApp($"{TAG}Network error on attempt {attempt}: {lastError}", LogType.Warning);
                    }
                    finally
                    {
                        _currentRequest = null;
                    }

                    if (ct.IsCancellationRequested)
                    {
                        SetState(FetchState.Cancelled);
                        return;
                    }

                    long responseCode = request.responseCode;

                    // 1. HTTP 304 Not Modified
                    if (responseCode == 304)
                    {
                        Logger.LogApp($"{TAG}Releases unchanged (HTTP 304 Not Modified).");
                        isSuccess = true;
                        _lastSuccessfulFetchTime = Time.realtimeSinceStartup;
                        break;
                    }

                    // 2. HTTP 200 OK
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        string jsonResponse = request.downloadHandler?.text;
                        if (!string.IsNullOrEmpty(jsonResponse))
                        {
                            if (jsonResponse.Length > _maxResponseSizeBytes)
                            {
                                lastError = $"Response size ({jsonResponse.Length}) exceeds {_maxResponseSizeBytes} bytes";
                                Logger.LogApp($"{TAG}{lastError}", LogType.Warning);
                            }
                            else
                            {
                                try
                                {
                                    var parsedItems = ParseAndFilterReleases(jsonResponse, _fileExtension);
                                    string etag = request.GetResponseHeader("ETag");
                                    if (!string.IsNullOrEmpty(etag))
                                    {
                                        _cachedETag = etag;
                                    }

                                    SaveCacheAsync(_cacheFilePath, jsonResponse, _cachedETag);

                                    bool changed = ApplyNewReleases(parsedItems);
                                    Logger.LogApp($"{TAG}Successfully pulled {parsedItems.Count} releases from GitHub (changed: {changed}).");
                                    isSuccess = true;
                                    _lastSuccessfulFetchTime = Time.realtimeSinceStartup;
                                    break;
                                }
                                catch (Exception parseEx)
                                {
                                    lastError = parseEx.Message;
                                    Logger.LogApp($"{TAG}JSON parse error on attempt {attempt}: {lastError}", LogType.Warning);
                                }
                            }
                        }
                        else
                        {
                            lastError = "Empty response body";
                        }
                    }
                    else
                    {
                        lastError = responseCode > 0 ? responseCode.ToString() : request.result.ToString();
                        Logger.LogApp($"{TAG}Attempt {attempt} failed: {lastError}", LogType.Warning);

                        // Check GitHub rate limiting (403 with X-RateLimit-Remaining == 0, or 429)
                        if (responseCode == 403 || responseCode == 429)
                        {
                            string remaining = request.GetResponseHeader("X-RateLimit-Remaining");
                            if (remaining == "0" || responseCode == 429)
                            {
                                string reset = request.GetResponseHeader("X-RateLimit-Reset");
                                Logger.LogApp($"{TAG}GitHub rate limit reached (Reset epoch: {reset}). Aborting retries.", LogType.Warning);
                                break;
                            }
                        }

                        if (!IsTransientError(request))
                        {
                            Logger.LogApp($"{TAG}Non-transient error ({lastError}), aborting retries.", LogType.Warning);
                            break;
                        }
                    }
                }

                if (attempt < _maxRetries && !ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay((int)(currentDelay * 1000f), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        SetState(FetchState.Cancelled);
                        return;
                    }
                    currentDelay *= 2f;
                }
            }

            if (ct.IsCancellationRequested)
            {
                SetState(FetchState.Cancelled);
                return;
            }

            if (isSuccess)
            {
                SetState(FetchState.Succeeded);
            }
            else
            {
                Logger.LogApp($"{TAG}Failed to fetch releases: {lastError}", LogType.Warning);
                SetState(FetchState.Failed);
            }

            if (_hasPendingRefresh && !_isDisposed)
            {
                bool forcePending = _pendingForce;
                _hasPendingRefresh = false;
                _pendingForce = false;
                _ = RequestRefresh(forcePending);
            }
        }

        private bool ApplyNewReleases(List<ReleaseInfo> newItems)
        {
            if (AreReleasesIdentical(_releases, newItems))
            {
                return false;
            }

            _releases.Clear();
            _releases.AddRange(newItems);
            OnReleasesChanged?.Invoke();
            return true;
        }

        private static bool AreReleasesIdentical(List<ReleaseInfo> a, List<ReleaseInfo> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                var itemA = a[i];
                var itemB = b[i];
                if (itemA.FileName != itemB.FileName || itemA.TagName != itemB.TagName || itemA.DownloadUrl != itemB.DownloadUrl)
                {
                    return false;
                }
            }
            return true;
        }

        private static Task<UnityWebRequest> SendWebRequestAsync(UnityWebRequest request, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<UnityWebRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            var op = request.SendWebRequest();
            op.completed += _ => tcs.TrySetResult(request);

            if (ct.CanBeCanceled)
            {
                ct.Register(() =>
                {
                    try
                    {
                        if (!request.isDone) request.Abort();
                    }
                    catch { }
                    tcs.TrySetCanceled();
                });
            }

            return tcs.Task;
        }

        private static bool IsTransientError(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                return true;
            }

            long code = request.responseCode;
            if (code == 408 || code == 429)
            {
                return true;
            }

            if (code >= 500 && code <= 599)
            {
                return true;
            }

            return false;
        }

        private static void SaveCacheAsync(string path, string jsonContent, string etag)
        {
            Task.Run(() =>
            {
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    string tempPath = path + ".tmp";
                    File.WriteAllText(tempPath, jsonContent);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    File.Move(tempPath, path);

                    if (!string.IsNullOrEmpty(etag))
                    {
                        string etagPath = path + ".etag";
                        File.WriteAllText(etagPath, etag);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogApp($"[ReleaseService] Failed to save cache: {ex.Message}", LogType.Warning);
                }
            });
        }

        private static List<ReleaseInfo> ParseAndFilterReleases(string rawJson, string extension)
        {
            string wrappedJson = "{\"items\":" + rawJson + "}";
            var wrapper = JsonUtility.FromJson<GitHubReleaseArrayWrapper>(wrappedJson);
            var list = new List<ReleaseInfo>();
            if (wrapper == null || wrapper.items == null || wrapper.items.Length == 0)
            {
                return list;
            }

            string latestTagName = wrapper.items[0]?.tag_name;

            for (int i = 0; i < wrapper.items.Length; i++)
            {
                var release = wrapper.items[i];
                if (release == null || release.assets == null) continue;

                string rawDate = !string.IsNullOrEmpty(release.updated_at) ? release.updated_at : release.published_at;
                DateTime.TryParse(rawDate, out DateTime releaseTime);
                bool isLatest = !string.IsNullOrEmpty(latestTagName) && release.tag_name == latestTagName;

                for (int j = 0; j < release.assets.Length; j++)
                {
                    var asset = release.assets[j];
                    if (asset == null || string.IsNullOrEmpty(asset.name)) continue;

                    if (asset.name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        string itemDate = !string.IsNullOrEmpty(asset.updated_at) ? asset.updated_at : rawDate;
                        DateTime.TryParse(itemDate, out DateTime updateTime);
                        if (updateTime == DateTime.MinValue) updateTime = releaseTime;

                        string title = !string.IsNullOrWhiteSpace(release.name) ? release.name : release.tag_name;
                        list.Add(new ReleaseInfo(
                            title: title,
                            tagName: release.tag_name,
                            fileName: asset.name,
                            downloadUrl: asset.browser_download_url,
                            body: release.body,
                            updatedAt: updateTime,
                            isLatest: isLatest
                        ));
                    }
                }
            }

            // In-place sort newest first without LINQ allocation
            list.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));

            if (list.Count > 0)
            {
                list[0].IsLatest = true;
            }

            return list;
        }

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
            public string updated_at;
            public string published_at;
            public string body;
            public GitHubAssetItem[] assets;
        }

        [Serializable]
        private class GitHubAssetItem
        {
            public string name;
            public string browser_download_url;
            public string updated_at;
        }
    }
}
