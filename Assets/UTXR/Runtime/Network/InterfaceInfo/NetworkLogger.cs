using System;
using UnityEngine;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Centralized logging manager for the Network module.
    /// Manages unified logging switches (DebugLog for console, FileLog for UTXR.Core.Logger).
    /// Can be configured via Unity Inspector or accessed statically.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkLogger : MonoBehaviour
    {
        [Header("Logging Configuration")]
        [Tooltip("Enable output to Unity console via UnityEngine.Debug.")]
        [SerializeField] private bool enableDebugLog = true;

        [Tooltip("Enable output to persistent disk log file via UTXR.Core.Logger.")]
        [SerializeField] private bool enableFileLog = true;

        public static bool EnableDebugLog { get; set; } = true;
        public static bool EnableFileLog { get; set; } = true;

        public static bool IsEnabled => EnableDebugLog || EnableFileLog;

        private static Action<string, LogType> _loggerLogAppDelegate;
        private static bool _loggerResolved = false;

        private void Awake()
        {
            SyncSettings();
        }

        private void OnValidate()
        {
            SyncSettings();
        }

        private void SyncSettings()
        {
            EnableDebugLog = enableDebugLog;
            EnableFileLog = enableFileLog;
        }

        public static void Log(string message)
        {
            LogApp(message, LogType.Log);
        }

        public static void LogWarning(string message)
        {
            LogApp(message, LogType.Warning);
        }

        public static void LogError(string message)
        {
            LogApp(message, LogType.Error);
        }

        public static void LogApp(string message, LogType type = LogType.Log)
        {
            if (!EnableDebugLog && !EnableFileLog) return;

            if (EnableFileLog)
            {
                EnsureLoggerResolved();
                _loggerLogAppDelegate?.Invoke(message, type);
            }

            if (EnableDebugLog)
            {
                switch (type)
                {
                    case LogType.Error:
                    case LogType.Exception:
                        UnityEngine.Debug.LogError(message);
                        break;
                    case LogType.Warning:
                        UnityEngine.Debug.LogWarning(message);
                        break;
                    default:
                        UnityEngine.Debug.Log(message);
                        break;
                }
            }
        }

        private static void EnsureLoggerResolved()
        {
            if (_loggerResolved) return;
            _loggerResolved = true;
            try
            {
                Type loggerType = Type.GetType("UTXR.Core.Logger, Assembly-CSharp");
                if (loggerType != null)
                {
                    var method = loggerType.GetMethod("LogApp",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null, new[] { typeof(string), typeof(LogType) }, null);
                    if (method != null)
                    {
                        _loggerLogAppDelegate = (Action<string, LogType>)Delegate.CreateDelegate(typeof(Action<string, LogType>), method);
                    }
                }
            }
            catch
            {
                // Reflection fallback handled gracefully
            }
        }
    }
}
