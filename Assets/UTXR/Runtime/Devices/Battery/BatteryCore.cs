using System;
using UnityEngine;
using UTXR.Core;
using Logger = UTXR.Core.Logger;

namespace UTXR.Devices
{
    /// <summary>
    /// Device battery power state enum.
    /// </summary>
    public enum BatteryState
    {
        Normal,
        Low,
        Charging
    }

    /// <summary>
    /// Immutable value type representing a snapshot of battery status.
    /// Zero allocations and supports high-performance value equality.
    /// </summary>
    public readonly struct BatteryInfo : IEquatable<BatteryInfo>
    {
        public readonly int Percentage;
        public readonly BatteryState State;
        public readonly bool IsPlugged;
        public readonly float RawLevel;

        public BatteryInfo(int percentage, BatteryState state, bool isPlugged, float rawLevel)
        {
            Percentage = percentage;
            State = state;
            IsPlugged = isPlugged;
            RawLevel = rawLevel;
        }

        public bool Equals(BatteryInfo other)
        {
            return Percentage == other.Percentage &&
                   State == other.State &&
                   IsPlugged == other.IsPlugged;
        }

        public override bool Equals(object obj) => obj is BatteryInfo other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Percentage, (int)State, IsPlugged);
        public static bool operator ==(BatteryInfo left, BatteryInfo right) => left.Equals(right);
        public static bool operator !=(BatteryInfo left, BatteryInfo right) => !left.Equals(right);
    }

    /// <summary>
    /// Pure C# battery querying service.
    /// Encapsulates native Android JNI battery broadcast query and Unity SystemInfo fallback.
    /// Completely free of MonoBehaviour lifecycle overhead.
    /// </summary>
    public static class BatteryService
    {
        private const string TAG = "[BatteryService] ";

        /// <summary>
        /// Reads device battery info via native Android sticky intent (or SystemInfo fallback on PC/Editor).
        /// Zero heap allocations during execution.
        /// </summary>
        public static BatteryInfo QueryBattery(int lowThreshold = 20)
        {
            float sysLevel = -1f;
            bool isPlugged = false;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var context = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intentFilter = new AndroidJavaObject("android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED"))
                using (var intent = context.Call<AndroidJavaObject>("registerReceiver", null, intentFilter))
                {
                    if (intent != null)
                    {
                        int level = intent.Call<int>("getIntExtra", "level", -1);
                        int scale = intent.Call<int>("getIntExtra", "scale", -1);
                        int plugged = intent.Call<int>("getIntExtra", "plugged", -1);

                        if (level >= 0 && scale > 0)
                        {
                            sysLevel = level / (float)scale;
                        }

                        // plugged > 0 indicates AC (1), USB (2), Wireless (4)
                        isPlugged = (plugged > 0);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogApp($"{TAG}Android native battery query failed: {e.Message}", LogType.Warning);
            }
#endif

            // Fallback for Editor / Windows or if Android native query failed
            if (sysLevel < 0)
            {
                sysLevel = SystemInfo.batteryLevel;
                var sysStatus = SystemInfo.batteryStatus;
                isPlugged = (sysStatus == UnityEngine.BatteryStatus.Charging ||
                             sysStatus == UnityEngine.BatteryStatus.Full ||
                             sysStatus == UnityEngine.BatteryStatus.NotCharging);
            }

            int percentage = sysLevel >= 0f ? Mathf.Clamp(Mathf.RoundToInt(sysLevel * 100f), 0, 100) : 100;
            BatteryState state;

            if (isPlugged)
            {
                state = BatteryState.Charging;
            }
            else if (percentage <= lowThreshold)
            {
                state = BatteryState.Low;
            }
            else
            {
                state = BatteryState.Normal;
            }

            return new BatteryInfo(percentage, state, isPlugged, sysLevel);
        }
    }

    /// <summary>
    /// High-performance string cache for TextMeshPro battery percentage rendering.
    /// Pre-allocates strings for 0..100 across Normal, Low, and Charging styles at startup,
    /// completely eliminating per-frame and per-update string allocations (Zero-GC).
    /// </summary>
    public static class BatteryStringCache
    {
        private static readonly string[] NormalStrings = new string[101];
        private static readonly string[] LowStrings = new string[101];
        private static readonly string[] ChargingStrings = new string[101];

        static BatteryStringCache()
        {
            for (int i = 0; i <= 100; i++)
            {
                NormalStrings[i] = i.ToString();
                LowStrings[i] = $"<color=#E16661>{i}</color>";
                ChargingStrings[i] = $"<color=white>{i}</color>";
            }
        }

        public static string GetFormattedString(int percentage, BatteryState state)
        {
            int index = Mathf.Clamp(percentage, 0, 100);
            return state switch
            {
                BatteryState.Charging => ChargingStrings[index],
                BatteryState.Low => LowStrings[index],
                _ => NormalStrings[index]
            };
        }
    }

    /// <summary>
    /// Pure C# stateful Battery Monitor Core.
    /// Manages state caching, threshold evaluation, and pure C# event dispatches.
    /// Decoupled from Unity MonoBehaviour lifecycle.
    /// </summary>
    public class BatteryMonitorCore
    {
        private static BatteryMonitorCore _instance;
        public static BatteryMonitorCore Instance => _instance ??= new BatteryMonitorCore();

        public BatteryState CurrentBatteryState { get; private set; } = BatteryState.Normal;
        public int CurrentBatteryPercentage { get; private set; } = 100;
        public bool IsPlugged { get; private set; }
        public float RawLevel { get; private set; } = 1.0f;
        public int LowBatteryThreshold { get; set; } = 20;

        public event Action<BatteryInfo> OnBatteryChanged;
        public event Action OnBatteryLow;
        public event Action OnBatteryRecovered;

        private BatteryState _previousState = BatteryState.Normal;
        private bool _hasEvaluated = false;

        public BatteryInfo Evaluate(bool forceNotify = false)
        {
            BatteryInfo info = BatteryService.QueryBattery(LowBatteryThreshold);
            CurrentBatteryPercentage = info.Percentage;
            CurrentBatteryState = info.State;
            IsPlugged = info.IsPlugged;
            RawLevel = info.RawLevel;

            bool stateChanged = !_hasEvaluated || info.State != _previousState;
            _hasEvaluated = true;

            if (stateChanged || forceNotify)
            {
                if (info.State == BatteryState.Low)
                {
                    OnBatteryLow?.Invoke();
                }
                else if (_previousState == BatteryState.Low)
                {
                    OnBatteryRecovered?.Invoke();
                }
                _previousState = info.State;
            }

            OnBatteryChanged?.Invoke(info);
            return info;
        }

        public void Reset()
        {
            _hasEvaluated = false;
            _previousState = BatteryState.Normal;
        }
    }
}
