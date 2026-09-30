using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Record3D
{
    public enum Record3DLogCategory
    {
        General,
        Signaling,
        WebRTC,
        GPU,
        Metadata
    }

    public enum Record3DLogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public struct Record3DLogEntry
    {
        public DateTime Timestamp;
        public Record3DLogCategory Category;
        public Record3DLogLevel Level;
        public string Message;

        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss.fff}] [{Category}] [{Level}] {Message}";
        }
    }

    /// <summary>
    /// Opt-in Record3D logger. Add this component in the Inspector and enable
    /// either output there; without an enabled component, static log calls are silent.
    /// </summary>
    [DisallowMultipleComponent]
    public class Record3DLogger : MonoBehaviour
    {
        [SerializeField] private bool _enableDebugLog = false;
        [SerializeField] private bool _enableFileLog = false;
        private bool _countedDebugLog;
        private bool _countedFileLog;
        private int _instanceGeneration;
        private static int _debugLogInstanceCount;
        private static int _fileLogInstanceCount;
        private static int _generation;
        private static string _fileLogPath;
        private static bool _sessionFileInitialized;
        private static bool _fileErrorReported;

        public bool EnableDebugLog
        {
            get => _enableDebugLog;
            set
            {
                _enableDebugLog = value;
                RefreshEnabledState();
            }
        }

        public bool EnableFileLog
        {
            get => _enableFileLog;
            set
            {
                _enableFileLog = value;
                RefreshEnabledState();
            }
        }

        public static bool IsDebugLoggingEnabled => Volatile.Read(ref _debugLogInstanceCount) > 0;
        public static bool IsFileLoggingEnabled => Volatile.Read(ref _fileLogInstanceCount) > 0;
        public static bool IsLoggingEnabled => IsDebugLoggingEnabled || IsFileLoggingEnabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Interlocked.Increment(ref _generation);
            Interlocked.Exchange(ref _debugLogInstanceCount, 0);
            Interlocked.Exchange(ref _fileLogInstanceCount, 0);
            lock (_lock)
            {
                _fileLogPath = null;
                _sessionFileInitialized = false;
                _fileErrorReported = false;
                _logs.Clear();
            }
        }

        private static void EnsureFileLogInitialized()
        {
            if (_sessionFileInitialized && _fileLogPath != null)
                return;

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "Record3D_Logs");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                else
                {
                    // Clean up any older historical log files so ONLY the latest application run is stored!
                    try
                    {
                        var oldTxt = Directory.GetFiles(dir, "record3d_*.txt");
                        foreach (var f in oldTxt) File.Delete(f);
                        var oldLog = Directory.GetFiles(dir, "record3d_*.log");
                        foreach (var f in oldLog) File.Delete(f);
                    }
                    catch { }
                }

                _fileLogPath = Path.Combine(dir, "record3d_latest.txt");
                File.WriteAllText(_fileLogPath, $"=== Record3D Session Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===" + Environment.NewLine);
                _sessionFileInitialized = true;
            }
            catch (Exception ex)
            {
                if (!_fileErrorReported)
                {
                    _fileErrorReported = true;
                    Debug.LogError($"[Record3D] Failed to initialize latest session log file: {ex}");
                }
            }
        }

        private void OnEnable() => RefreshEnabledState();

        // Inspector changes to a serialized field do not use the public setter.
        private void Update() => RefreshEnabledState();

        private void OnDisable()
        {
            if (_instanceGeneration == Volatile.Read(ref _generation))
            {
                if (_countedDebugLog) Interlocked.Decrement(ref _debugLogInstanceCount);
                if (_countedFileLog) Interlocked.Decrement(ref _fileLogInstanceCount);
            }
            _countedDebugLog = false;
            _countedFileLog = false;
        }

        private void RefreshEnabledState()
        {
            int generation = Volatile.Read(ref _generation);
            if (_instanceGeneration != generation)
            {
                _instanceGeneration = generation;
                _countedDebugLog = false;
                _countedFileLog = false;
            }
            bool active = Application.isPlaying && isActiveAndEnabled;
            bool debugLog = active && _enableDebugLog;
            bool fileLog = active && _enableFileLog;
            if (debugLog != _countedDebugLog)
            {
                _countedDebugLog = debugLog;
                if (debugLog) Interlocked.Increment(ref _debugLogInstanceCount);
                else Interlocked.Decrement(ref _debugLogInstanceCount);
            }
            if (fileLog != _countedFileLog)
            {
                _countedFileLog = fileLog;
                if (fileLog) Interlocked.Increment(ref _fileLogInstanceCount);
                else Interlocked.Decrement(ref _fileLogInstanceCount);
            }
        }

        private const int MaxLogCapacity = 200;
        private static readonly List<Record3DLogEntry> _logs = new List<Record3DLogEntry>(MaxLogCapacity);
        private static readonly object _lock = new object();

        public static event Action<Record3DLogEntry> OnNewLog;

        public static Record3DLogLevel MinimumLogLevel = Record3DLogLevel.Debug;

        public static void Log(Record3DLogCategory category, Record3DLogLevel level, string message)
        {
            if (!IsLoggingEnabled || level < MinimumLogLevel)
                return;

            var entry = new Record3DLogEntry
            {
                Timestamp = DateTime.Now,
                Category = category,
                Level = level,
                Message = message
            };

            lock (_lock)
            {
                if (_logs.Count >= MaxLogCapacity)
                {
                    _logs.RemoveAt(0);
                }
                _logs.Add(entry);

                if (IsFileLoggingEnabled)
                {
                    try
                    {
                        if (!_sessionFileInitialized || _fileLogPath == null)
                        {
                            EnsureFileLogInitialized();
                        }

                        if (_fileLogPath != null)
                        {
                            File.AppendAllText(_fileLogPath, entry + Environment.NewLine);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!_fileErrorReported)
                        {
                            _fileErrorReported = true;
                            Debug.LogError($"[Record3D] File log write failed: {ex}");
                        }
                    }
                }
            }

            if (IsDebugLoggingEnabled)
            {
                string formatted = $"<color=#00e5ff>[Record3D::{category}]</color> {message}";
                switch (level)
                {
                    case Record3DLogLevel.Debug:
                    case Record3DLogLevel.Info:
                        Debug.Log(formatted);
                        break;
                    case Record3DLogLevel.Warning:
                        Debug.LogWarning(formatted);
                        break;
                    case Record3DLogLevel.Error:
                        Debug.LogError(formatted);
                        break;
                }
            }

            OnNewLog?.Invoke(entry);
        }

        // Convenience shortcuts
        public static void Info(Record3DLogCategory category, string message) => Log(category, Record3DLogLevel.Info, message);
        public static void Warning(Record3DLogCategory category, string message) => Log(category, Record3DLogLevel.Warning, message);
        public static void Error(Record3DLogCategory category, string message) => Log(category, Record3DLogLevel.Error, message);

        public static void Signaling(string message, Record3DLogLevel level = Record3DLogLevel.Info) => Log(Record3DLogCategory.Signaling, level, message);
        public static void WebRTC(string message, Record3DLogLevel level = Record3DLogLevel.Info) => Log(Record3DLogCategory.WebRTC, level, message);
        public static void GPU(string message, Record3DLogLevel level = Record3DLogLevel.Info) => Log(Record3DLogCategory.GPU, level, message);
        public static void Metadata(string message, Record3DLogLevel level = Record3DLogLevel.Info) => Log(Record3DLogCategory.Metadata, level, message);

        public static List<Record3DLogEntry> GetSnapshot()
        {
            lock (_lock)
            {
                return new List<Record3DLogEntry>(_logs);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _logs.Clear();
            }
        }

        public static string DumpToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Record3D Session Log Dump ===");
            sb.AppendLine($"Export Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=================================");

            lock (_lock)
            {
                foreach (var log in _logs)
                {
                    sb.AppendLine(log.ToString());
                }
            }
            return sb.ToString();
        }

        public static string DumpToFile()
        {
            if (!IsFileLoggingEnabled)
                return string.Empty;

            EnsureFileLogInitialized();
            if (_fileLogPath != null)
            {
                File.WriteAllText(_fileLogPath, DumpToString());
                Log(Record3DLogCategory.General, Record3DLogLevel.Info, $"Session logs written to: {_fileLogPath}");
            }
            return _fileLogPath ?? string.Empty;
        }
    }
}
