using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class WifiMonitor : MonoBehaviour
{
    [Min(1.0f)]
    public float pollInterval = 5f;

    public UnityEvent onWiFiEnabled;
    public UnityEvent onWiFiDisabled;

    private bool lastState;
    private bool isFirstCheck = true;
    private const string TAG = "[WifiMonitor]";

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(PollRoutine());
#else
        Logger.LogApp($"{TAG} Target must be Android device.");
#endif
    }

    private IEnumerator PollRoutine()
    {
        while (true)
        {
            bool currentState = CheckWiFiActive();

            if (isFirstCheck || currentState != lastState)
            {
                Logger.LogApp(TAG + "State changed -> " + (currentState ? "ON" : "OFF"));
                
                lastState = currentState;
                isFirstCheck = false;

                if (currentState) 
                    onWiFiEnabled?.Invoke();
                else 
                    onWiFiDisabled?.Invoke();
            }

            yield return new WaitForSeconds(pollInterval);
        }
    }

    private bool CheckWiFiActive()
    {
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi"))
            {
                if (wifiManager != null)
                {
                    return wifiManager.Call<bool>("isWifiEnabled");
                }
                Logger.LogApp($"{TAG} System service 'wifi' returned null.");
                return false;
            }
        }
        catch (System.Exception e)
        {
            Logger.LogApp($"{TAG} {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Open Wi-Fi Settings, Use Android 10 Panel or Wi-Fi Settings
    /// </summary>
    public void OpenWiFiSettings()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                // Android 10+ Panel
                string panelAction = "android.settings.panel.action.WIFI";
                bool panelSuccess = TryStartActivity(currentActivity, panelAction);

                if (panelSuccess)
                {
                    Logger.LogApp($"{TAG} Successfully opened Wi-Fi Panel.");
                    return;
                }

                Logger.LogApp($"{TAG} Panel action not found or supported. Falling back to legacy settings...");

                //  Wi-Fi Setting
                string legacyAction = "android.settings.WIFI_SETTINGS";
                bool legacySuccess = TryStartActivity(currentActivity, legacyAction);

                if (legacySuccess)
                {
                    Logger.LogApp($"{TAG} Successfully opened legacy Wi-Fi settings.");
                }
                else
                {
                    Logger.LogApp($"{TAG} Both Panel and Legacy Wi-Fi settings intents failed. System might be heavily restricted.");
                }
            }
        }
        catch (System.Exception e)
        {
            Logger.LogApp($"{TAG} Fatal error in OpenWiFiSettings: {e.Message}");
        }
#else
        Logger.LogApp($"{TAG} Opening Wi-Fi settings is only supported on Android devices.");
#endif
    }

    /// <summary>
    /// 辅助方法：尝试根据 action 字符串拉起系统界面并捕获异常
    /// </summary>
    private bool TryStartActivity(AndroidJavaObject activity, string action)
    {
        try
        {
            using (AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent", action))
            {
                activity.Call("startActivity", intent);
                return true; 
            }
        }
        catch (System.Exception)
        {
            // 如果系统里没有处理该 action 的应用或组件，startActivity 会抛出 ActivityNotFoundException
            // 在 Unity JNI 中会被捕获为普通的 Exception
            return false;
        }
    }
}