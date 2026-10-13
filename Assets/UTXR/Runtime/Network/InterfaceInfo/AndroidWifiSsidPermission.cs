using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Utility for requesting Android runtime location permissions required to read Wi-Fi SSID.
    /// Android 12+ (API 31+) mandates requesting both Fine and Coarse location permissions simultaneously.
    /// Pure static method without MonoBehaviour lifecycle.
    /// </summary>
    public static class AndroidWifiSsidPermission
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool _requestInitiated = false;
        private static PermissionCallbacks _callbacks;

        private const string TAG = "[Network] ";

        public static void RequestLocationPermissions()
        {
            // Unity's Permission API must strictly run on the Unity main thread
            if (System.Threading.Thread.CurrentThread.IsThreadPoolThread) return;
            if (_requestInitiated) return;
            if (Permission.HasUserAuthorizedPermission(Permission.FineLocation)) return;

            _requestInitiated = true;
            _callbacks = new PermissionCallbacks();
            _callbacks.PermissionGranted += OnGranted;
            _callbacks.PermissionDenied += OnDenied;
            _callbacks.PermissionDeniedAndDontAskAgain += OnDeniedAndDontAskAgain;

            NetworkLogger.Log($"{TAG}Requesting location permissions for Wi-Fi SSID...");

            // Android 12+ requires requesting FineLocation and CoarseLocation together, otherwise request may be ignored
            Permission.RequestUserPermissions(
                new[]
                {
                    Permission.FineLocation,
                    Permission.CoarseLocation
                },
                _callbacks);
        }

        private static void OnGranted(string permission)
        {
            NetworkLogger.Log($"{TAG}Location permission granted: {permission}");
            if (permission == Permission.FineLocation)
            {
                NetworkInfoService.Instance?.RequestRefresh();
            }
        }

        private static void OnDenied(string permission)
        {
            NetworkLogger.LogWarning($"{TAG}Location permission denied: {permission}");
        }

        private static void OnDeniedAndDontAskAgain(string permission)
        {
            NetworkLogger.LogWarning($"{TAG}Location permission permanently denied: {permission}");
        }
#else
        public static void RequestLocationPermissions()
        {
        }
#endif
    }
}
