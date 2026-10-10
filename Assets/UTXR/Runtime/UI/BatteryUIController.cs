using TMPro;
using UnityEngine;
using UTXR.Devices;

namespace UTXR.UI
{
    /// <summary>
    /// High-performance visual controller for battery UI elements.
    /// Uses BatteryStringCache for Zero-GC percentage string updates and dirty-checking
    /// to avoid redundant SetActive calls on UI hierarchies.
    /// </summary>
    [DisallowMultipleComponent]
    public class BatteryUIController : MonoBehaviour
    {
        [Header("UI Element References")]
        [Tooltip("Active when battery is above low threshold and not charging.")]
        public GameObject batteryNormalUI;

        [Tooltip("Active when battery is at or below low threshold and not charging.")]
        public GameObject batteryLowUI;

        [Tooltip("Active when battery is connected to a power source.")]
        public GameObject batteryChargingUI;

        [Tooltip("TextMeshPro label to display battery percentage.")]
        public TextMeshProUGUI batteryPercentageText;

        [Header("Options")]
        [Tooltip("Automatically subscribe to global BatteryMonitorCore events when enabled.")]
        public bool autoSubscribe = true;

        private int _lastPercentage = -1;
        private BatteryState _lastState = (BatteryState)(-1);
        private bool _isFirstUpdate = true;

        private void OnEnable()
        {
            if (autoSubscribe)
            {
                BatteryMonitorCore.Instance.OnBatteryChanged += HandleBatteryChanged;
                // Render current cached state if already evaluated
                UpdateUI(BatteryMonitorCore.Instance.CurrentBatteryPercentage, BatteryMonitorCore.Instance.CurrentBatteryState);
            }
        }

        private void OnDisable()
        {
            if (autoSubscribe)
            {
                BatteryMonitorCore.Instance.OnBatteryChanged -= HandleBatteryChanged;
            }
        }

        private void HandleBatteryChanged(BatteryInfo info)
        {
            UpdateUI(info.Percentage, info.State);
        }

        /// <summary>
        /// Updates the battery UI elements with the provided percentage and state.
        /// Optimized for Zero-GC and minimal canvas hierarchy invalidation.
        /// </summary>
        public void UpdateUI(int percentage, BatteryState state)
        {
            bool stateChanged = _isFirstUpdate || state != _lastState;
            bool percentageChanged = _isFirstUpdate || percentage != _lastPercentage;

            if (stateChanged)
            {
                if (batteryNormalUI != null) batteryNormalUI.SetActive(state == BatteryState.Normal);
                if (batteryLowUI != null) batteryLowUI.SetActive(state == BatteryState.Low);
                if (batteryChargingUI != null) batteryChargingUI.SetActive(state == BatteryState.Charging);
                _lastState = state;
            }

            if (percentageChanged || stateChanged)
            {
                if (batteryPercentageText != null)
                {
                    batteryPercentageText.text = BatteryStringCache.GetFormattedString(percentage, state);
                }
                _lastPercentage = percentage;
            }

            _isFirstUpdate = false;
        }
    }
}
