using System;
using System.Collections.Generic;
using UnityEngine;

namespace AndroidIntent
{
    [Serializable]
    public struct IntExtra
    {
        public string key;
        public int value;

        public IntExtra(string key, int value)
        {
            this.key = key;
            this.value = value;
        }
    }

    [Serializable]
    public struct StringExtra
    {
        public string key;
        public string value;

        public StringExtra(string key, string value)
        {
            this.key = key;
            this.value = value;
        }
    }

    [CreateAssetMenu(fileName = "NewAndroidIntent", menuName = "Android Intent/Intent Data", order = 1)]
    public class AndroidIntentData : ScriptableObject
    {
        [Header("Intent Configurations")]
        [Tooltip("Android Action, e.g., android.intent.action.MAIN, android.intent.action.VIEW, or android.settings.WIFI_SETTINGS")]
        public string action = "android.intent.action.MAIN";

        [Tooltip("Optional Data URI, e.g., systemux://settings or package:com.example.app")]
        public string dataUri;

        [Tooltip("Target Package Name, e.g., com.oculus.vrshell or com.pvr.seethrough.setting")]
        public string packageName;

        [Tooltip("Target Class Name, e.g., com.oculus.vrshell.MainActivity")]
        public string className;

        [Tooltip("Intent Flags (e.g., 270532608 for 0x10200000, 268435456 for 0x10000000)")]
        public int flags = 268435456;

        [Header("Extras")]
        public List<IntExtra> intExtras = new List<IntExtra>();
        public List<StringExtra> stringExtras = new List<StringExtra>();

        /// <summary>
        /// Launch this Intent on Android platform (or log in Editor).
        /// </summary>
        public void Launch()
        {
            AndroidIntentUtility.SendIntent(this);
        }
    }
}
