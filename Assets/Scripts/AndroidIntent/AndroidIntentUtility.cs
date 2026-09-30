using System;
using System.Collections.Generic;
using UnityEngine;

namespace AndroidIntent
{
    /// <summary>
    /// Pure, generic Android Intent utility for sending dynamic intents via JNI.
    /// Supports standard Android actions (e.g. android.settings.WIFI_SETTINGS) and Meta SystemUX DeepLinks.
    /// </summary>
    public static class AndroidIntentUtility
    {
        /// <summary>
        /// Send an Intent specified by an AndroidIntentData ScriptableObject.
        /// </summary>
        public static void SendIntent(AndroidIntentData data)
        {
            if (data == null)
            {
                Debug.LogError("[AndroidIntentUtility] AndroidIntentData is null.");
                return;
            }

            SendIntent(data.action, data.dataUri, data.packageName, data.className, data.flags, data.intExtras, data.stringExtras);
        }

        /// <summary>
        /// Send a dynamic Android Intent by providing action, dataUri, package, class, flags, and extras directly.
        /// </summary>
        public static void SendIntent(
            string action,
            string dataUri = null,
            string packageName = null,
            string className = null,
            int flags = 0,
            List<IntExtra> intExtras = null,
            List<StringExtra> stringExtras = null)
        {
            if (string.IsNullOrEmpty(action))
            {
                Debug.LogError("[AndroidIntentUtility] Action cannot be null or empty.");
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent", action))
                {
                    if (!string.IsNullOrEmpty(dataUri))
                    {
                        using (AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri"))
                        using (AndroidJavaObject uri = uriClass.CallStatic<AndroidJavaObject>("parse", dataUri))
                        {
                            intent.Call<AndroidJavaObject>("setData", uri);
                        }
                    }

                    if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(className))
                    {
                        using (AndroidJavaObject componentName = new AndroidJavaObject("android.content.ComponentName", packageName, className))
                        {
                            intent.Call<AndroidJavaObject>("setComponent", componentName);
                        }
                    }
                    else if (!string.IsNullOrEmpty(packageName))
                    {
                        intent.Call<AndroidJavaObject>("setPackage", packageName);
                    }

                    if (flags != 0)
                    {
                        intent.Call<AndroidJavaObject>("setFlags", flags);
                    }

                    if (intExtras != null)
                    {
                        foreach (var extra in intExtras)
                        {
                            if (!string.IsNullOrEmpty(extra.key))
                                intent.Call<AndroidJavaObject>("putExtra", extra.key, extra.value);
                        }
                    }

                    if (stringExtras != null)
                    {
                        foreach (var extra in stringExtras)
                        {
                            if (!string.IsNullOrEmpty(extra.key))
                                intent.Call<AndroidJavaObject>("putExtra", extra.key, extra.value);
                        }
                    }

                    currentActivity.Call("startActivity", intent);
                    Debug.Log($"[AndroidIntentUtility] Sent Intent: Action='{action}', DataUri='{dataUri}', Package='{packageName}', Class='{className}', Flags={flags}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AndroidIntentUtility] Failed to send Intent: {ex.Message}\n{ex.StackTrace}");
            }
#else
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("[AndroidIntentUtility Mock] Sent Intent:");
            sb.AppendLine($"  - Action: '{action}'");
            if (!string.IsNullOrEmpty(dataUri)) sb.AppendLine($"  - DataUri: '{dataUri}'");
            if (!string.IsNullOrEmpty(packageName)) sb.AppendLine($"  - Package: '{packageName}'");
            if (!string.IsNullOrEmpty(className)) sb.AppendLine($"  - Class: '{className}'");
            if (flags != 0) sb.AppendLine($"  - Flags: {flags} (0x{flags:X8})");
            if (intExtras != null && intExtras.Count > 0)
            {
                foreach (var extra in intExtras)
                    sb.AppendLine($"  - Int Extra: '{extra.key}' = {extra.value}");
            }
            if (stringExtras != null && stringExtras.Count > 0)
            {
                foreach (var extra in stringExtras)
                    sb.AppendLine($"  - String Extra: '{extra.key}' = '{extra.value}'");
            }
            Debug.Log(sb.ToString());
#endif
        }
    }
}
