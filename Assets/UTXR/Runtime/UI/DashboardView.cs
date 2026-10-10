using UnityEngine;
using UnityEngine.Events;

namespace UTXR.UI
{
    /// <summary>
    /// Dashboard window controller.
    /// Manages window minimize and maximize states and invokes corresponding events.
    /// </summary>
    public class DashboardView : MonoBehaviour
    {
        [Header("Window State Events")]
        [Tooltip("Invoked when Dashboard is minimized.")]
        public UnityEvent onMinimize;

        [Tooltip("Invoked when Dashboard is maximized.")]
        public UnityEvent onMaximize;

        private bool _isMinimized = false;

        private void Start()
        {
            MaximizeWindow();
        }

        /// <summary>
        /// Toggles between minimized and maximized window states.
        /// Can be hooked to InputActionTrigger.
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
        /// Forces the dashboard window into minimized state.
        /// </summary>
        public void MinimizeWindow()
        {
            if (_isMinimized) return;
            _isMinimized = true;
            onMinimize?.Invoke();
        }

        /// <summary>
        /// Forces the dashboard window into maximized state.
        /// </summary>
        public void MaximizeWindow()
        {
            if (!_isMinimized) return;
            _isMinimized = false;
            onMaximize?.Invoke();
        }
    }
}
