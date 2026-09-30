using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public enum InputActionType
{
    Started,
    Canceled,
    DoubleClick,
    LongPress,
    SingleClick
}

[Serializable]
public class InputActionElement
{
    [Tooltip("Drag the corresponding Input Action Reference here (e.g., XRI RightHand/A Button)")]
    public InputActionReference inputAction;

    [Tooltip("Select the type of interaction to trigger this event.")]
    public InputActionType interactionType = InputActionType.Started;

    [Tooltip("Triggered when the specified interaction occurs.")]
    public UnityEvent onActionTriggered;

    // C# Event for other scripts
    public event Action OnTriggeredEvent;

    internal const float doubleClickInterval = 0.3f;
    internal const float longPressDuration = 0.8f;

    // Internal tracking states
    internal float pressStartTime;
    internal float lastClickTime;
    internal int clickCount;
    internal bool isRegisteredForPress;
    internal bool isRegisteredForPending;
    internal bool isTriggeredLongPress;

    private Action<InputAction.CallbackContext> _startedHandler;
    private Action<InputAction.CallbackContext> _canceledHandler;

    public void Register(InputActionTrigger manager)
    {
        if (inputAction != null && inputAction.action != null)
        {
            _startedHandler = context => 
            {
                float time = Time.unscaledTime;
                
                if (interactionType == InputActionType.Started)
                {
                    Invoke();
                }
                else if (interactionType == InputActionType.LongPress || interactionType == InputActionType.SingleClick)
                {
                    pressStartTime = time;
                    isTriggeredLongPress = false;
                    manager.AddActivePress(this);
                }
                else if (interactionType == InputActionType.DoubleClick)
                {
                    // If the time since last click is too long, reset (just in case Update hasn't caught it yet)
                    if (clickCount > 0 && (time - lastClickTime) > doubleClickInterval)
                    {
                        clickCount = 0;
                        manager.RemovePendingWait(this);
                    }

                    clickCount++;
                    if (clickCount == 1)
                    {
                        lastClickTime = time;
                        manager.AddPendingWait(this);
                    }
                    else if (clickCount >= 2)
                    {
                        Invoke();
                        clickCount = 0;
                        manager.RemovePendingWait(this);
                    }
                }
            };
            
            _canceledHandler = context => 
            {
                if (interactionType == InputActionType.Canceled)
                {
                    Invoke();
                }
                else if (interactionType == InputActionType.LongPress)
                {
                    manager.RemoveActivePress(this);
                }
                else if (interactionType == InputActionType.SingleClick)
                {
                    manager.RemoveActivePress(this);
                    float duration = Time.unscaledTime - pressStartTime;
                    bool hasTriggeredLongPress = isTriggeredLongPress;
                    if (!hasTriggeredLongPress)
                    {
                        var longElem = manager.GetElement(inputAction, InputActionType.LongPress);
                        if (longElem != null && longElem.isTriggeredLongPress)
                        {
                            hasTriggeredLongPress = true;
                        }
                    }

                    if (duration < longPressDuration && !hasTriggeredLongPress)
                    {
                        Invoke();
                    }
                }
            };

            inputAction.action.started += _startedHandler;
            inputAction.action.canceled += _canceledHandler;
            
            inputAction.action.Enable();
        }
    }

    public void Unregister(InputActionTrigger manager)
    {
        if (inputAction != null && inputAction.action != null)
        {
            if (_startedHandler != null)
            {
                inputAction.action.started -= _startedHandler;
                _startedHandler = null;
            }
            if (_canceledHandler != null)
            {
                inputAction.action.canceled -= _canceledHandler;
                _canceledHandler = null;
            }
        }
        manager.RemoveActivePress(this);
        manager.RemovePendingWait(this);
    }

    public void Invoke()
    {
        onActionTriggered?.Invoke();
        OnTriggeredEvent?.Invoke();
    }
}

public class InputActionTrigger : MonoBehaviour
{
    [Tooltip("Define multiple input elements and their corresponding specific interaction types.")]
    [FormerlySerializedAs("inputActions")]
    public List<InputActionElement> inputElements = new List<InputActionElement>();

    // Efficient tracking lists
    private List<InputActionElement> _activePresses = new List<InputActionElement>();
    private List<InputActionElement> _pendingWaits = new List<InputActionElement>();

    private void OnEnable()
    {
        if (inputElements != null)
        {
            foreach (var element in inputElements)
            {
                element.Register(this);
            }
        }
    }

    private void OnDisable()
    {
        if (inputElements != null)
        {
            foreach (var element in inputElements)
            {
                element.Unregister(this);
            }
        }
        _activePresses.Clear();
        _pendingWaits.Clear();
    }

    private void Update()
    {
        if (_activePresses.Count > 0 || _pendingWaits.Count > 0)
        {
            float time = Time.unscaledTime;

            // Monitor long presses & single click hold expiration
            for (int i = _activePresses.Count - 1; i >= 0; i--)
            {
                var element = _activePresses[i];
                if (!element.isTriggeredLongPress && (time - element.pressStartTime) >= InputActionElement.longPressDuration)
                {
                    element.isTriggeredLongPress = true;
                    if (element.interactionType == InputActionType.LongPress)
                    {
                        element.Invoke();
                    }
                    _activePresses.RemoveAt(i);
                    element.isRegisteredForPress = false;
                }
            }

            // Monitor pending double clicks expiration
            for (int i = _pendingWaits.Count - 1; i >= 0; i--)
            {
                var element = _pendingWaits[i];
                if ((time - element.lastClickTime) > InputActionElement.doubleClickInterval)
                {
                    element.clickCount = 0;
                    _pendingWaits.RemoveAt(i);
                    element.isRegisteredForPending = false;
                }
            }
        }
    }

    internal void AddActivePress(InputActionElement element)
    {
        if (!element.isRegisteredForPress)
        {
            _activePresses.Add(element);
            element.isRegisteredForPress = true;
        }
    }

    internal void RemoveActivePress(InputActionElement element)
    {
        if (element.isRegisteredForPress)
        {
            _activePresses.Remove(element);
            element.isRegisteredForPress = false;
        }
    }

    internal void AddPendingWait(InputActionElement element)
    {
        if (!element.isRegisteredForPending)
        {
            _pendingWaits.Add(element);
            element.isRegisteredForPending = true;
        }
    }

    internal void RemovePendingWait(InputActionElement element)
    {
        if (element.isRegisteredForPending)
        {
            _pendingWaits.Remove(element);
            element.isRegisteredForPending = false;
        }
    }

    // Public API for other scripts
    public InputActionElement GetElement(InputActionReference actionRef, InputActionType type)
    {
        if (inputElements == null) return null;
        foreach (var element in inputElements)
        {
            if (element.inputAction == actionRef && element.interactionType == type)
                return element;
        }
        return null;
    }

    public List<InputActionElement> GetElements(InputActionReference actionRef)
    {
        List<InputActionElement> results = new List<InputActionElement>();
        if (inputElements == null) return results;
        foreach (var element in inputElements)
        {
            if (element.inputAction == actionRef)
                results.Add(element);
        }
        return results;
    }

    public void ToggleGameObjectActive(GameObject targetObj)
    {
        if (targetObj != null)
        {
            targetObj.SetActive(!targetObj.activeSelf);
        }
    }
}
