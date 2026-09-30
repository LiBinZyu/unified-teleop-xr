using System;
using UnityEngine;

namespace CycloneDDS
{
    public enum DdsLogLevel
    {
        None = 0,
        Error = 1,
        Warning = 2,
        Info = 3,
        Debug = 4
    }

    /// <summary>
    /// Dedicated logging module for CycloneDDS with level filtering and debug controls.
    /// </summary>
    public static class DdsLog
    {
        private const string Tag = "[CycloneDDS]";

        /// <summary>
        /// Global switch to enable verbose debug logging for DDS packet transmission and internal events.
        /// </summary>
        public static bool EnableDebugLog { get; set; } = false;

        /// <summary>
        /// Current minimum log level to output to Unity Console.
        /// </summary>
        public static DdsLogLevel LogLevel { get; set; } = DdsLogLevel.Info;

        public static void Debug(string message)
        {
            if (EnableDebugLog || LogLevel >= DdsLogLevel.Debug)
            {
                UnityEngine.Debug.Log($"{Tag} [DEBUG] {message}");
            }
        }

        public static void Debug(string format, params object[] args)
        {
            if (EnableDebugLog || LogLevel >= DdsLogLevel.Debug)
            {
                UnityEngine.Debug.LogFormat($"{Tag} [DEBUG] {format}", args);
            }
        }

        public static void Info(string message)
        {
            if (LogLevel >= DdsLogLevel.Info)
            {
                UnityEngine.Debug.Log($"{Tag} [INFO] {message}");
            }
        }

        public static void Info(string format, params object[] args)
        {
            if (LogLevel >= DdsLogLevel.Info)
            {
                UnityEngine.Debug.LogFormat($"{Tag} [INFO] {format}", args);
            }
        }

        public static void Warning(string message)
        {
            if (LogLevel >= DdsLogLevel.Warning)
            {
                UnityEngine.Debug.LogWarning($"{Tag} [WARN] {message}");
            }
        }

        public static void Warning(string format, params object[] args)
        {
            if (LogLevel >= DdsLogLevel.Warning)
            {
                UnityEngine.Debug.LogWarningFormat($"{Tag} [WARN] {format}", args);
            }
        }

        public static void Error(string message)
        {
            if (LogLevel >= DdsLogLevel.Error)
            {
                UnityEngine.Debug.LogError($"{Tag} [ERROR] {message}");
            }
        }

        public static void Error(string format, params object[] args)
        {
            if (LogLevel >= DdsLogLevel.Error)
            {
                UnityEngine.Debug.LogErrorFormat($"{Tag} [ERROR] {format}", args);
            }
        }
    }
}
