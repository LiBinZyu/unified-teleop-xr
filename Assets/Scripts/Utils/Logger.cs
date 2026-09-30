using System;
using System.Collections.Concurrent;
using System.Diagnostics; // 引入 Process
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class Logger : MonoBehaviour
{
    public float maxFileSizeMB = 16.0f;
    public bool enableMediaCodecLog = false;
    
    public enum LogCategory { App, System, MediaCodec }

    private struct LogEntry
    {
        public LogCategory Category;
        public string Message;
        public string StackTrace;
        public LogType Type;
        public DateTime Time;
    }

    private readonly ConcurrentQueue<LogEntry> _logQueue = new ConcurrentQueue<LogEntry>();
    private Thread _writeThread;
    private readonly AutoResetEvent _threadSignal = new AutoResetEvent(false);
    private volatile bool _isRunning = false;

    private string _appLogPath;
    private string _sysLogPath;
    private string _mediaCodecLogPath;
    private long _maxByteSize;

    private Process _logcatProcess; // 用于捕获原生 Logcat 的进程
    private bool _lastEnableMediaCodecLog = false;

    public static Logger Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _maxByteSize = (long)(maxFileSizeMB * 1024 * 1024);
        string basePath = Application.persistentDataPath;
        
        _appLogPath = Path.Combine(basePath, "AppLog.txt");
        _sysLogPath = Path.Combine(basePath, "SystemLog.txt");
        _mediaCodecLogPath = Path.Combine(basePath, "MediaCodecLog.txt");

        ClearOldLogs();

        _isRunning = true;
        _writeThread = new Thread(LogWriterThread)
        {
            Name = "VR_Disk_Logger_Thread",
            IsBackground = true,
            Priority = System.Threading.ThreadPriority.BelowNormal
        };
        _writeThread.Start();

        // App 的常规日志依然使用 Unity 接口
        Application.logMessageReceivedThreaded += OnUnityLogReceived;

        // 启动 Logcat 捕获原生日志（仅限安卓/头显端）
#if UNITY_ANDROID && !UNITY_EDITOR
        StartAndroidLogcat();
        _lastEnableMediaCodecLog = enableMediaCodecLog;
        if (enableMediaCodecLog)
        {
            StartMediaCodecLogcat();
        }
#endif
    }

    private void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (enableMediaCodecLog != _lastEnableMediaCodecLog)
        {
            _lastEnableMediaCodecLog = enableMediaCodecLog;
            if (enableMediaCodecLog)
            {
                StartMediaCodecLogcat();
            }
            else
            {
                StopMediaCodecLogcat();
            }
        }
#endif
    }

    private void ClearOldLogs()
    {
        try
        {
            if (File.Exists(_appLogPath)) File.Delete(_appLogPath);
            if (File.Exists(_sysLogPath)) File.Delete(_sysLogPath);
            if (File.Exists(_mediaCodecLogPath)) File.Delete(_mediaCodecLogPath);
            if (File.Exists(_appLogPath + ".bak")) File.Delete(_appLogPath + ".bak");
            if (File.Exists(_sysLogPath + ".bak")) File.Delete(_sysLogPath + ".bak");
            if (File.Exists(_mediaCodecLogPath + ".bak")) File.Delete(_mediaCodecLogPath + ".bak");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Logger] Failed to clear old logs: {e.Message}");
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private Thread _mediaCodecThread;
    private AndroidJavaObject _mediaCodecLogcatProcess;
    private volatile bool _isMediaCodecRunning = false;

    private void StartAndroidLogcat()
    {
        // 必须在独立的后台线程运行，以免阻塞 Unity 主线程
        Thread logcatThread = new Thread(() =>
        {
            // 在后台线程使用 AndroidJavaObject，必须先 Attach JNI
            AndroidJNI.AttachCurrentThread();
            try
            {
                // 第一步：清空旧的 Logcat 缓存 (-c)
                using (AndroidJavaClass runtimeClass = new AndroidJavaClass("java.lang.Runtime"))
                using (AndroidJavaObject runtime = runtimeClass.CallStatic<AndroidJavaObject>("getRuntime"))
                {
                    using (AndroidJavaObject clearProcess = runtime.Call<AndroidJavaObject>("exec", "logcat -c"))
                    {
                        clearProcess.Call<int>("waitFor"); 
                    }

                    // 第二步：持续读取新的 Logcat (-v time 附带时间戳)
                    // 如果日志太多，可以改成 "logcat -v time -s Unity OpenXR" 来过滤
                    using (AndroidJavaObject logcatProcess = runtime.Call<AndroidJavaObject>("exec", "logcat -v time"))
                    using (AndroidJavaObject inputStream = logcatProcess.Call<AndroidJavaObject>("getInputStream"))
                    using (AndroidJavaObject inputStreamReader = new AndroidJavaObject("java.io.InputStreamReader", inputStream))
                    using (AndroidJavaObject bufferedReader = new AndroidJavaObject("java.io.BufferedReader", inputStreamReader))
                    {
                        string line;
                        // readLine() 会阻塞线程直到有新日志输出
                        while (_isRunning && (line = bufferedReader.Call<string>("readLine")) != null)
                        {
                            Instance._logQueue.Enqueue(new LogEntry
                            {
                                Category = LogCategory.System,
                                Message = line,
                                StackTrace = string.Empty,
                                Type = LogType.Log,
                                Time = DateTime.Now // 这里的 C# 时间只做备用，具体看下面时间问题的解释
                            });
                            Instance._threadSignal.Set();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Logger] JNI Logcat Error: {e.Message}");
            }
            finally
            {
                // 退出线程前必须 Detach
                AndroidJNI.DetachCurrentThread();
            }
        })
        {
            Name = "VR_Native_Logcat_Thread",
            IsBackground = true,
            Priority = System.Threading.ThreadPriority.BelowNormal
        };
        
        logcatThread.Start();
    }

    private void StartMediaCodecLogcat()
    {
        if (_isMediaCodecRunning) return;
        _isMediaCodecRunning = true;
        
        _mediaCodecThread = new Thread(MediaCodecLogcatThread)
        {
            Name = "VR_MediaCodec_Logcat_Thread",
            IsBackground = true,
            Priority = System.Threading.ThreadPriority.BelowNormal
        };
        _mediaCodecThread.Start();
        Debug.Log("[Logger] MediaCodec logcat started.");
    }

    private void StopMediaCodecLogcat()
    {
        if (!_isMediaCodecRunning) return;
        _isMediaCodecRunning = false;

        try
        {
            if (_mediaCodecLogcatProcess != null)
            {
                _mediaCodecLogcatProcess.Call("destroy");
                _mediaCodecLogcatProcess.Dispose();
                _mediaCodecLogcatProcess = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Logger] Error stopping MediaCodec logcat: {e.Message}");
        }
        
        Debug.Log("[Logger] MediaCodec logcat stopped.");
    }

    private void MediaCodecLogcatThread()
    {
        AndroidJNI.AttachCurrentThread();
        try
        {
            using (AndroidJavaClass runtimeClass = new AndroidJavaClass("java.lang.Runtime"))
            using (AndroidJavaObject runtime = runtimeClass.CallStatic<AndroidJavaObject>("getRuntime"))
            {
                string cmd = "logcat -v time -s MediaCodec:V ACodec:V CCodec:V Codec2:V OMXNodeInstance:V *:S";
                _mediaCodecLogcatProcess = runtime.Call<AndroidJavaObject>("exec", cmd);
                
                using (AndroidJavaObject inputStream = _mediaCodecLogcatProcess.Call<AndroidJavaObject>("getInputStream"))
                using (AndroidJavaObject inputStreamReader = new AndroidJavaObject("java.io.InputStreamReader", inputStream))
                using (AndroidJavaObject bufferedReader = new AndroidJavaObject("java.io.BufferedReader", inputStreamReader))
                {
                    string line;
                    while (_isMediaCodecRunning && _isRunning && (line = bufferedReader.Call<string>("readLine")) != null)
                    {
                        Instance._logQueue.Enqueue(new LogEntry
                        {
                            Category = LogCategory.MediaCodec,
                            Message = line,
                            StackTrace = string.Empty,
                            Type = LogType.Log,
                            Time = DateTime.Now
                        });
                        Instance._threadSignal.Set();
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (_isMediaCodecRunning && _isRunning)
            {
                Debug.LogError($"[Logger] JNI MediaCodec Logcat Error: {e.Message}");
            }
        }
        finally
        {
            AndroidJNI.DetachCurrentThread();
        }
    }
#endif

    private void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= OnUnityLogReceived;
        
        _isRunning = false;
        _threadSignal.Set(); 

#if UNITY_ANDROID && !UNITY_EDITOR
        if (_logcatProcess != null && !_logcatProcess.HasExited)
        {
            _logcatProcess.Kill();
            _logcatProcess.Dispose();
        }
        StopMediaCodecLogcat();
        if (_mediaCodecThread != null && _mediaCodecThread.IsAlive)
        {
            _mediaCodecThread.Join(500);
        }
#endif
        
        if (_writeThread != null && _writeThread.IsAlive)
        {
            _writeThread.Join(500);
        }
    }

    public static void LogApp(string message, LogType type = LogType.Log)
    {
        if (Instance == null || !Instance._isRunning) return;

        Instance._logQueue.Enqueue(new LogEntry
        {
            Category = LogCategory.App,
            Message = message,
            StackTrace = string.Empty,
            Type = type,
            Time = DateTime.Now 
        });
        
        Instance._threadSignal.Set();
    }

    private void OnUnityLogReceived(string condition, string stackTrace, LogType type)
    {
        if (Instance == null || !Instance._isRunning) return;

        bool isWebRTC = condition.Contains("webrtc", StringComparison.OrdinalIgnoreCase) || 
                        condition.Contains("WebRTC", StringComparison.OrdinalIgnoreCase);

#if UNITY_ANDROID && !UNITY_EDITOR
        // 专门拦截 WebRTC 的报错到 AppLog
        if (isWebRTC && (type == LogType.Error || type == LogType.Exception || type == LogType.Warning))
        {
            Instance._logQueue.Enqueue(new LogEntry
            {
                Category = LogCategory.App,
                Message = $"[WebRTC Native] {condition}",
                StackTrace = stackTrace,
                Type = type,
                Time = DateTime.Now
            });
            Instance._threadSignal.Set();
        }
        return; 
#else
        Instance._logQueue.Enqueue(new LogEntry
        {
            Category = LogCategory.System,
            Message = condition,
            StackTrace = (type == LogType.Exception || type == LogType.Error) ? stackTrace : string.Empty,
            Type = type,
            Time = DateTime.Now
        });
        Instance._threadSignal.Set();
#endif
    }

    private void LogWriterThread()
    {
        using StreamWriter appWriter = new StreamWriter(new FileStream(_appLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.None), Encoding.UTF8) { AutoFlush = true };
        using StreamWriter sysWriter = new StreamWriter(new FileStream(_sysLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.None), Encoding.UTF8) { AutoFlush = true };
        StreamWriter mediaCodecWriter = null;

        StringBuilder sb = new StringBuilder(512);
        StreamWriter currentAppWriter = appWriter;
        StreamWriter currentSysWriter = sysWriter;

        try
        {
            while (_isRunning || !_logQueue.IsEmpty)
            {
                if (_logQueue.IsEmpty)
                {
                    _threadSignal.WaitOne(); 
                }

                while (_logQueue.TryDequeue(out LogEntry entry))
                {
                    sb.Clear();
                    
                    // 如果是原生的 Logcat (已经自带了安卓的时间戳和 PID/TID)，我们就不再给它套我们自己的时间格式了，保持最原始的样子
                    if (entry.Category == LogCategory.System || entry.Category == LogCategory.MediaCodec)
                    {
                        sb.Append(entry.Message);
                    }
                    else
                    {
                        sb.Append("[").Append(entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff zzz")).Append("] ");
                        sb.Append("[").Append(entry.Type.ToString()).Append("] ");
                        sb.Append(entry.Message);
                        if (!string.IsNullOrEmpty(entry.StackTrace))
                        {
                            sb.Append("\n").Append(entry.StackTrace);
                        }
                    }

                    StreamWriter currentWriter;
                    if (entry.Category == LogCategory.App)
                    {
                        currentWriter = currentAppWriter;
                    }
                    else if (entry.Category == LogCategory.MediaCodec)
                    {
                        if (mediaCodecWriter == null)
                        {
                            mediaCodecWriter = new StreamWriter(new FileStream(_mediaCodecLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.None), Encoding.UTF8) { AutoFlush = true };
                        }
                        currentWriter = mediaCodecWriter;
                    }
                    else
                    {
                        currentWriter = currentSysWriter;
                    }

                    currentWriter.WriteLine(sb.ToString());

                    if (currentWriter.BaseStream.Length > _maxByteSize)
                    {
                        if (entry.Category == LogCategory.App)
                            currentAppWriter = RotateFile(currentAppWriter, _appLogPath);
                        else if (entry.Category == LogCategory.MediaCodec)
                            mediaCodecWriter = RotateFile(mediaCodecWriter, _mediaCodecLogPath);
                        else
                            currentSysWriter = RotateFile(currentSysWriter, _sysLogPath);
                    }
                }
            }
        }
        finally
        {
            if (mediaCodecWriter != null)
            {
                mediaCodecWriter.Flush();
                mediaCodecWriter.Close();
                mediaCodecWriter.Dispose();
            }
        }
    }

    private StreamWriter RotateFile(StreamWriter writer, string path)
    {
        writer.Flush();
        writer.Close();
        writer.Dispose();

        string backupPath = path + ".bak";
        try
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(path, backupPath); 
        }
        catch (Exception)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        StreamWriter newWriter = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.None), Encoding.UTF8) { AutoFlush = true };
        newWriter.WriteLine($"--- Log Rotated at {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz")} ---");
        return newWriter;
    }
}