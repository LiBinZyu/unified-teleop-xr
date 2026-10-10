using System;
using UnityEngine;
using UnityEngine.Events;

namespace UTXR.Devices
{
    /// <summary>
    /// Event dispatcher for XR devices and controllers.
    /// Broadcasts tracking state changes, warnings, swings, and combo shakes.
    /// Provides both UnityEvents for Inspector bindings and high-performance C# Action delegates for code listeners.
    /// </summary>
    [DisallowMultipleComponent]
    public class XRDeviceEventDispatcher : MonoBehaviour
    {
        [Header("Tracking Status UnityEvents")]
        public UnityEvent OnLeftTracked;
        public UnityEvent OnLeftUntracked;
        public UnityEvent OnRightTracked;
        public UnityEvent OnRightUntracked;
        public UnityEvent OnControllerTrackWarn;

        [Header("Shake / Swing UnityEvents")]
        public UnityEvent OnLeftSwing;
        public UnityEvent OnRightSwing;
        public UnityEvent OnLeftTripleSwing;
        public UnityEvent OnRightTripleSwing;
        public UnityEvent OnAnyShake;

        #region High-Performance C# Action Events (Zero GC)

        public event Action<bool> OnLeftTrackingChanged;
        public event Action<bool> OnRightTrackingChanged;
        public event Action<bool, bool> OnLeftTrackingStateChanged;
        public event Action<bool, bool> OnRightTrackingStateChanged;
        public event Action OnLeftTrackedAction;
        public event Action OnLeftUntrackedAction;
        public event Action OnRightTrackedAction;
        public event Action OnRightUntrackedAction;
        public event Action OnControllerTrackWarnAction;

        public event Action OnLeftSwingAction;
        public event Action OnRightSwingAction;
        public event Action OnLeftTripleSwingAction;
        public event Action OnRightTripleSwingAction;
        public event Action OnAnyShakeAction;

        #endregion

        #region Event Dispatch Methods

        /// <summary>
        /// Dispatches tracking state changes for the specified hand.
        /// </summary>
        public void DispatchTrackingChanged(bool isLeft, bool isTracked)
        {
            if (isLeft)
            {
                OnLeftTrackingChanged?.Invoke(isTracked);
                if (isTracked)
                {
                    OnLeftTracked?.Invoke();
                    OnLeftTrackedAction?.Invoke();
                }
                else
                {
                    OnLeftUntracked?.Invoke();
                    OnLeftUntrackedAction?.Invoke();
                }
            }
            else
            {
                OnRightTrackingChanged?.Invoke(isTracked);
                if (isTracked)
                {
                    OnRightTracked?.Invoke();
                    OnRightTrackedAction?.Invoke();
                }
                else
                {
                    OnRightUntracked?.Invoke();
                    OnRightUntrackedAction?.Invoke();
                }
            }
        }

        /// <summary>
        /// Dispatches detailed tracking and warning states for the specified hand.
        /// </summary>
        public void DispatchTrackingState(bool isLeft, bool isTracked, bool isWarning)
        {
            if (isLeft)
            {
                OnLeftTrackingStateChanged?.Invoke(isTracked, isWarning);
            }
            else
            {
                OnRightTrackingStateChanged?.Invoke(isTracked, isWarning);
            }
        }

        /// <summary>
        /// Dispatches controller tracking warning event.
        /// </summary>
        public void DispatchControllerTrackWarn()
        {
            OnControllerTrackWarn?.Invoke();
            OnControllerTrackWarnAction?.Invoke();
        }

        /// <summary>
        /// Dispatches a single swing event.
        /// </summary>
        public void DispatchSwing(bool isLeft)
        {
            if (isLeft)
            {
                OnLeftSwing?.Invoke();
                OnLeftSwingAction?.Invoke();
            }
            else
            {
                OnRightSwing?.Invoke();
                OnRightSwingAction?.Invoke();
            }
        }

        /// <summary>
        /// Dispatches a 3-swing combo event.
        /// </summary>
        public void DispatchTripleSwing(bool isLeft)
        {
            if (isLeft)
            {
                OnLeftTripleSwing?.Invoke();
                OnLeftTripleSwingAction?.Invoke();
            }
            else
            {
                OnRightTripleSwing?.Invoke();
                OnRightTripleSwingAction?.Invoke();
            }
        }

        /// <summary>
        /// Dispatches any shake/gesture trigger event.
        /// </summary>
        public void DispatchAnyShake()
        {
            OnAnyShake?.Invoke();
            OnAnyShakeAction?.Invoke();
        }

        #endregion
    }
}
