using System;
using UnityEngine;
using UnityEngine.Events;
using UTXR.Core;
using Logger = UTXR.Core.Logger;

namespace UTXR.Devices
{
    /// <summary>
    /// High-performance MonoBehaviour component for periodic battery monitoring.
    /// Manages Unity lifecycle timer and Inspector-bindable UnityEvents.
    /// Pure querying and state evaluation is delegated to BatteryMonitorCore / BatteryService.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public class BatteryMonitor : MonoBehaviour
    {
        private const string TAG = "[BatteryMonitor] ";

        [Header("Settings")]
        [Tooltip("Battery polling interval in seconds.")]
        [Min(1.0f)]
        public float checkInterval = 5.0f;

        [Tooltip("Percentage threshold at or below which battery is considered low.")]
        [Range(1, 50)]
        public int lowBatteryThreshold = 20;

        [Tooltip("Perform an immediate battery query on startup without waiting for the timer interval.")]
        public bool checkOnStartup = true;

        [Header("Events")]
        [Tooltip("Invoked when battery level falls below low threshold while not charging.")]
        public UnityEvent onBatteryLowWarning;

        [Tooltip("Invoked when battery transitions from Low to Charging or Normal.")]
        public UnityEvent onBatteryRecovered;

        [Tooltip("Invoked on any battery state or percentage evaluation update.")]
        public UnityEvent<int, BatteryState> onBatteryChanged;

        private float _timer = 0f;
        private BatteryMonitorCore _core;

        public BatteryMonitorCore Core => _core ??= BatteryMonitorCore.Instance;

        public BatteryState CurrentBatteryState => Core.CurrentBatteryState;
        public int CurrentBatteryPercentage => Core.CurrentBatteryPercentage;

        private void Awake()
        {
            _core = BatteryMonitorCore.Instance;
            _core.LowBatteryThreshold = lowBatteryThreshold;
        }

        private void OnEnable()
        {
            Core.OnBatteryLow += HandleBatteryLow;
            Core.OnBatteryRecovered += HandleBatteryRecovered;
            Core.OnBatteryChanged += HandleBatteryChanged;
        }

        private void OnDisable()
        {
            if (_core != null)
            {
                _core.OnBatteryLow -= HandleBatteryLow;
                _core.OnBatteryRecovered -= HandleBatteryRecovered;
                _core.OnBatteryChanged -= HandleBatteryChanged;
            }
        }

        private void Start()
        {
            _timer = checkInterval;
            if (checkOnStartup)
            {
                ForceCheck(forceNotify: true);
            }
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = checkInterval;
                Core.LowBatteryThreshold = lowBatteryThreshold;
                Core.Evaluate(forceNotify: false);
            }
        }

        /// <summary>
        /// Manually triggers an immediate battery evaluation.
        /// </summary>
        public void ForceCheck(bool forceNotify = false)
        {
            Core.LowBatteryThreshold = lowBatteryThreshold;
            BatteryInfo info = Core.Evaluate(forceNotify);
            Logger.LogApp($"{TAG}Evaluated -> {info.Percentage}% State={info.State} Plugged={info.IsPlugged}");
        }

        private void HandleBatteryLow() => onBatteryLowWarning?.Invoke();
        private void HandleBatteryRecovered() => onBatteryRecovered?.Invoke();
        private void HandleBatteryChanged(BatteryInfo info) => onBatteryChanged?.Invoke(info.Percentage, info.State);
    }
}
