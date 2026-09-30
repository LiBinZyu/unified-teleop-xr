using System;
using UnityEngine;
using UnityEngine.Events; // 引入 UnityEvent 命名空间
using TMPro;

namespace Apex.Dashboard
{
    public enum BatteryState
    {
        Normal,
        Low,
        Charging
    }

    public class DashboardUiManager
    {
        public BatteryState CurrentBatteryState { get; private set; } = BatteryState.Normal;
        public int CurrentBatteryPercentage { get; private set; } = 100;

        // ★ 新增：给纯代码监听使用的 C# 事件
        public event Action OnBatteryLow;        // 触发低电量警告时
        public event Action OnBatteryRecovered;  // 从低电量恢复时 (插上电或电量恢复)

        private static DashboardUiManager _instance;
        public static DashboardUiManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new DashboardUiManager();
                return _instance;
            }
        }

        private Transform _centerEyeAnchor;
        private GameObject _batteryNormalUI;
        private GameObject _batteryLowUI;
        private GameObject _batteryChargingUI;
        private TextMeshProUGUI _batteryPercentageText;

        private float _batteryCheckTimer = 0f;
        private float _batteryCheckInterval = 5f;
        
        private bool _isInitialized = false;
        private int _lastValidPercentage = 100;
        
        // ★ 新增：记录上一次的状态，防止每 5 秒重复触发警告
        private BatteryState _previousBatteryState = BatteryState.Normal;

        private DashboardUiManager() { }

        public void Initialize(
            GameObject normalUI,
            GameObject lowUI,
            GameObject chargingUI,
            TextMeshProUGUI percentageText)
        {
            _batteryNormalUI = normalUI;
            _batteryLowUI = lowUI;
            _batteryChargingUI = chargingUI;
            _batteryPercentageText = percentageText;

            _isInitialized = true;
            _previousBatteryState = BatteryState.Normal; // 初始化状态
        }

        public void OnUpdate(float deltaTime)
        {
            if (!_isInitialized) return;

            _batteryCheckTimer -= deltaTime;
            if (_batteryCheckTimer <= 0f)
            {
                _batteryCheckTimer = _batteryCheckInterval;
                CheckDeviceBattery();
            }
        }

        private void CheckDeviceBattery()
        {
            float sysLevel = -1f;
            UnityEngine.BatteryStatus sysStatus = UnityEngine.BatteryStatus.Unknown;
            bool isPlugged = false;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // 使用原生 Android API 获取电池状态，直接绕过 Unity 可能存在的缓存刷新延迟和 bug
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    using (AndroidJavaObject context = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        using (AndroidJavaObject intentFilter = new AndroidJavaObject("android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED"))
                        {
                            using (AndroidJavaObject intent = context.Call<AndroidJavaObject>("registerReceiver", null, intentFilter))
                            {
                                if (intent != null)
                                {
                                    int level = intent.Call<int>("getIntExtra", "level", -1);
                                    int scale = intent.Call<int>("getIntExtra", "scale", -1);
                                    int status = intent.Call<int>("getIntExtra", "status", -1);
                                    int plugged = intent.Call<int>("getIntExtra", "plugged", -1);
                                    //rawAndroidStatus = status;

                                    Logger.LogApp($"[Battery] Android raw -> level={level}, scale={scale}, status={status}, plugged={plugged}");

                                    if (level >= 0 && scale > 0)
                                    {
                                        sysLevel = level / (float)scale;
                                    }

                                    // BatteryManager.BATTERY_STATUS_CHARGING = 2
                                    // BatteryManager.BATTERY_STATUS_DISCHARGING = 3
                                    // BatteryManager.BATTERY_STATUS_NOT_CHARGING = 4
                                    // BatteryManager.BATTERY_STATUS_FULL = 5
                                    if (status == 2) sysStatus = UnityEngine.BatteryStatus.Charging;
                                    else if (status == 3) sysStatus = UnityEngine.BatteryStatus.Discharging;
                                    else if (status == 4) sysStatus = UnityEngine.BatteryStatus.NotCharging;
                                    else if (status == 5) sysStatus = UnityEngine.BatteryStatus.Full;

                                    // ★ plugged > 0 = 接了电源（1=AC充电头; 2=USB; 4=无线充电）
                                    isPlugged = (plugged > 0);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogApp($"[Battery] Android native query failed: {e.Message}", LogType.Warning);
            }
#endif

            // Fallback: 如果原生方法获取失败，则使用 Unity 原生的 SystemInfo
            if (sysLevel < 0)
            {
                sysLevel = SystemInfo.batteryLevel;
            }
            if (sysStatus == UnityEngine.BatteryStatus.Unknown)
            {
                sysStatus = SystemInfo.batteryStatus;
                isPlugged = (sysStatus == UnityEngine.BatteryStatus.Charging ||
                             sysStatus == UnityEngine.BatteryStatus.Full ||
                             sysStatus == UnityEngine.BatteryStatus.NotCharging);
            }

            if (sysLevel >= 0)
            {
                _lastValidPercentage = Mathf.RoundToInt(sysLevel * 100f);
            }

            BatteryState currentState = BatteryState.Normal;

            if (isPlugged)
            {
                currentState = BatteryState.Charging;
            }
            else if (_lastValidPercentage <= 20) 
            {
                currentState = BatteryState.Low;
            }

            CurrentBatteryState = currentState;
            CurrentBatteryPercentage = _lastValidPercentage;

            if (currentState != _previousBatteryState)
            {
                if (currentState == BatteryState.Low)
                {
                    OnBatteryLow?.Invoke(); // 掉电到 20% 以下时触发
                }
                else if (_previousBatteryState == BatteryState.Low)
                {
                    OnBatteryRecovered?.Invoke(); // 插上电源或电量恢复时触发
                }
                _previousBatteryState = currentState;
            }

            UpdateBatteryUI(_lastValidPercentage, currentState);
        }

        /// <summary>
        /// 全局调用：更新电量UI
        /// </summary>
        public void UpdateBatteryUI(int batteryPercentage, BatteryState state)
        {
            if (!_isInitialized) return;

            if (_batteryPercentageText != null)
            {
                if (state == BatteryState.Charging)
                {
                    _batteryPercentageText.text = $"<color=white>{batteryPercentage}</color>";
                }
                else if (state == BatteryState.Low)
                {
                    _batteryPercentageText.text = $"<color=#E16661>{batteryPercentage}</color>";
                }
                else
                {
                    _batteryPercentageText.text = $"{batteryPercentage}";
                }
            }

            if (_batteryNormalUI != null) _batteryNormalUI.SetActive(state == BatteryState.Normal);
            if (_batteryLowUI != null) _batteryLowUI.SetActive(state == BatteryState.Low);
            if (_batteryChargingUI != null) _batteryChargingUI.SetActive(state == BatteryState.Charging);

            // 警告信息的显示/隐藏由 Inspector 上绑定的 onBatteryLowWarning / onBatteryRecovered UnityEvent 控制
        }

        public void UpdateRecordingStatus(bool isRecording) { }
        public void UpdateManipulatorStatus(int status) { }
        public void UpdateOrinStatus(int status) { }
        public void UpdateTeleoperationStatus(int status) { }
    }

    public class DashboardView : MonoBehaviour
    {
        private const string TAG = "[DashboardView] ";
        [Header("Battery UI Settings")]
        public GameObject batteryNormalUI;
        public GameObject batteryLowUI;
        public GameObject batteryChargingUI;
        public TextMeshProUGUI batteryPercentageText;

        [Header("Events")]
        [Tooltip("当电量低于 20% 且未充电时触发此事件一次")]
        public UnityEvent onBatteryLowWarning;
        
        [Tooltip("当处于低电量警告状态后，重新插上电源时触发此事件")]
        public UnityEvent onBatteryRecovered;

        [Header("Window State Events")]
        [Tooltip("当 Dashboard 最小化时触发此事件")]
        public UnityEvent onMinimize;
        
        [Tooltip("当 Dashboard 最大化时触发此事件")]
        public UnityEvent onMaximize;

        private bool _isMinimized = false;

        /// <summary>
        /// 供其它 UnityEvent (如 InputActionTrigger 的 On Action Started) 调用的 Toggle 方法
        /// </summary>
        public void ToggleMinimizeMaximize()
        {
            if (_isMinimized)
            {
                MaximizeWindow();
            }
            else
            {
                MinimizeWindow();
            }
        }

        /// <summary>
        /// 强制最小化窗口
        /// </summary>
        public void MinimizeWindow()
        {
            if (_isMinimized) return; // 避免重复触发
            
            _isMinimized = true;
            onMinimize?.Invoke();
        }

        /// <summary>
        /// 强制最大化窗口
        /// </summary>
        public void MaximizeWindow()
        {
            if (!_isMinimized) return; // 避免重复触发
            
            _isMinimized = false;
            onMaximize?.Invoke();
        }

        private void Start()
        {
            // 初始化 DashboardUiManager（只管理电量和 UI）
            DashboardUiManager.Instance.Initialize(
                batteryNormalUI,
                batteryLowUI,
                batteryChargingUI,
                batteryPercentageText
            );

            // 启动时立即检查一次电量，确保警告 UI 处于正确的初始状态
            // 不依赖5秒轮询计时器，也不依赖"状态变化"差分
            CheckAndFireStartupBatteryEvents();
            MaximizeWindow();
        }

        /// <summary>
        /// 启动时立即读取电量并触发对应 UnityEvent，
        /// 解决启动时电量已低但首次5秒轮询尚未执行导致事件未触发的问题。
        /// </summary>
        private void CheckAndFireStartupBatteryEvents()
        {
            float level = -1f;
            bool isPlugged = false;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var context = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var filter  = new AndroidJavaObject("android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED"))
                using (var intent  = context.Call<AndroidJavaObject>("registerReceiver", null, filter))
                {
                    if (intent != null)
                    {
                        int raw   = intent.Call<int>("getIntExtra", "level",  -1);
                        int scale = intent.Call<int>("getIntExtra", "scale",  -1);
                        int plugged = intent.Call<int>("getIntExtra", "plugged", -1);
                        if (raw >= 0 && scale > 0) level = raw / (float)scale;
                        isPlugged = (plugged > 0);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogApp($"{TAG}Startup battery check (Android) failed: {e.Message}", LogType.Warning);
            }
#endif

            if (level < 0)
            {
                level = SystemInfo.batteryLevel;
                var status = SystemInfo.batteryStatus;
                isPlugged = (status == UnityEngine.BatteryStatus.Charging ||
                             status == UnityEngine.BatteryStatus.Full     ||
                             status == UnityEngine.BatteryStatus.NotCharging);
            }

            int pct = level >= 0 ? Mathf.RoundToInt(level * 100f) : 100;
            bool isLow = !isPlugged && pct <= 20;

            Logger.LogApp($"{TAG}Startup battery check -> {pct}% plugged={isPlugged} isLow={isLow}");

            // 无论当前状态如何，都强制触发一次对应事件，让警告 UI 进入正确的初始状态
            if (isLow)
            {
                Logger.LogApp(TAG + "Battery low at startup — firing onBatteryLowWarning immediately.");
                onBatteryLowWarning?.Invoke();
            }
            else
            {
                Logger.LogApp(TAG + "Battery normal/charging at startup");
                onBatteryRecovered?.Invoke();
            }
        }

        private void OnEnable()
        {
            DashboardUiManager.Instance.OnBatteryLow += TriggerLowWarningEvent;
            DashboardUiManager.Instance.OnBatteryRecovered += TriggerRecoveredEvent;
        }

        private void OnDisable()
        {
            DashboardUiManager.Instance.OnBatteryLow -= TriggerLowWarningEvent;
            DashboardUiManager.Instance.OnBatteryRecovered -= TriggerRecoveredEvent;
        }

        private void TriggerLowWarningEvent() => onBatteryLowWarning?.Invoke();
        private void TriggerRecoveredEvent() => onBatteryRecovered?.Invoke();

        private void Update()
        {
            DashboardUiManager.Instance.OnUpdate(Time.deltaTime);
        }
    }
}