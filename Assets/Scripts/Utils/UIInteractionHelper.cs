using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// General UI interaction event helper.
/// Integrates pointer, focus, navigation, and drag events supported by both
/// TrackedDeviceGraphicRaycaster (XR Ray/Hand) and GraphicRaycaster (Mouse/Touch).
/// Compatible with Button, Toggle, InputField, Dropdown (both standard and TextMeshPro).
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/UI Interaction Helper")]
public class UIInteractionHelper : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerClickHandler,
    ISelectHandler,
    IDeselectHandler,
    ISubmitHandler,
    ICancelHandler,
    IScrollHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    [System.Serializable]
    public class BoolEvent : UnityEvent<bool> { }

    [System.Serializable]
    public class Vector2Event : UnityEvent<Vector2> { }

    [System.Serializable]
    public class PointerDataEvent : UnityEvent<PointerEventData> { }

    #region Settings

    private bool AllowHoverWhenDisabled = true;

    private bool AllowPressWhenDisabled = false;

    private float DoubleClickInterval = 0.3f;

    #endregion

    #region Hover Events

    [Header("Hover Events")]
    [Tooltip("Invoked on hover state changes. Supports dynamic bool (e.g. GameObject.SetActive for hints).")]
    public BoolEvent onHoverChanged = new BoolEvent();

    [Tooltip("Invoked when pointer / XR ray enters.")]
    public UnityEvent onPointerEnter = new UnityEvent();

    [Tooltip("Invoked when pointer / XR ray exits.")]
    public UnityEvent onPointerExit = new UnityEvent();

    #endregion

    #region Press Events

    [Header("Press Events")]
    [Tooltip("Invoked on press state changes (true = pressed, false = released). Supports dynamic bool.")]
    public BoolEvent onPressChanged = new BoolEvent();

    [Tooltip("Invoked when pointer / trigger is pressed down.")]
    public UnityEvent onPointerDown = new UnityEvent();

    [Tooltip("Invoked when pointer / trigger is released.")]
    public UnityEvent onPointerUp = new UnityEvent();

    #endregion

    #region Click Events

    [Header("Click Events")]
    [Tooltip("Invoked on standard click.")]
    public UnityEvent onClick = new UnityEvent();

    [Tooltip("Invoked on double click.")]
    public UnityEvent onDoubleClick = new UnityEvent();

    [Tooltip("Invoked on right click / secondary action.")]
    public UnityEvent onRightClick = new UnityEvent();

    #endregion

    #region Focus & Navigation Events

    [Header("Focus & Navigation Events")]
    [Tooltip("Invoked on focus state changes. Supports dynamic bool.")]
    public BoolEvent onFocusChanged = new BoolEvent();

    [Tooltip("Invoked when selected / focused (e.g. gamepad navigation, Tab key, or code selection).")]
    public UnityEvent onSelect = new UnityEvent();

    [Tooltip("Invoked when deselected / lost focus.")]
    public UnityEvent onDeselect = new UnityEvent();

    [Tooltip("Invoked on submit action (Enter key / Gamepad A).")]
    public UnityEvent onSubmit = new UnityEvent();

    [Tooltip("Invoked on cancel action (Escape key / Gamepad B).")]
    public UnityEvent onCancel = new UnityEvent();

    #endregion

    #region Scroll & Drag Events

    [Header("Scroll & Drag Events")]
    [Tooltip("Invoked on scroll wheel or joystick scroll.")]
    public Vector2Event onScroll = new Vector2Event();

    [Tooltip("Invoked when drag begins.")]
    public UnityEvent onBeginDrag = new UnityEvent();

    [Tooltip("Invoked when drag ends.")]
    public UnityEvent onEndDrag = new UnityEvent();

    #endregion

    // Internal state tracking
    private Selectable _cachedSelectable;
    private bool _isHovered = false;
    private bool _isPressed = false;
    private bool _isFocused = false;
    private float _lastClickTime = -1f;

    /// <summary> True if currently hovered. </summary>
    public bool IsHovered => _isHovered;

    /// <summary> True if currently pressed down. </summary>
    public bool IsPressed => _isPressed;

    /// <summary> True if currently focused/selected. </summary>
    public bool IsFocused => _isFocused;

    /// <summary> Current interactable state of the attached Selectable (true if none). </summary>
    public bool IsInteractable
    {
        get
        {
            if (_cachedSelectable == null)
            {
                _cachedSelectable = GetComponent<Selectable>();
            }
            return _cachedSelectable == null || _cachedSelectable.interactable;
        }
    }

    private void Awake()
    {
        _cachedSelectable = GetComponent<Selectable>();
    }

    private void OnEnable()
    {
        _isHovered = false;
        _isPressed = false;
        _isFocused = false;
        onHoverChanged?.Invoke(false);
    }

    private void Start()
    {
        onHoverChanged?.Invoke(_isHovered);
    }

    /// <summary>
    /// Refreshes and invokes onHoverChanged with the current hover state.
    /// </summary>
    public void RefreshHoverState()
    {
        onHoverChanged?.Invoke(_isHovered);
    }

    #region IPointerEnterHandler & IPointerExitHandler

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!AllowHoverWhenDisabled && !IsInteractable)
            return;

        if (!_isHovered)
        {
            _isHovered = true;
            onHoverChanged?.Invoke(true);
            onPointerEnter?.Invoke();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_isHovered)
        {
            _isHovered = false;
            onHoverChanged?.Invoke(false);
            onPointerExit?.Invoke();
        }
    }

    #endregion

    #region IPointerDownHandler & IPointerUpHandler

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!AllowPressWhenDisabled && !IsInteractable)
            return;

        if (!_isPressed)
        {
            _isPressed = true;
            onPressChanged?.Invoke(true);
            onPointerDown?.Invoke();
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (_isPressed)
        {
            _isPressed = false;
            onPressChanged?.Invoke(false);
            onPointerUp?.Invoke();
        }
    }

    #endregion

    #region IPointerClickHandler

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!AllowPressWhenDisabled && !IsInteractable)
            return;

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            onRightClick?.Invoke();
            return;
        }

        onClick?.Invoke();

        float currentTime = Time.unscaledTime;
        if (eventData.clickCount == 2 || (currentTime - _lastClickTime <= DoubleClickInterval && _lastClickTime > 0))
        {
            onDoubleClick?.Invoke();
            _lastClickTime = -1f;
        }
        else
        {
            _lastClickTime = currentTime;
        }
    }

    #endregion

    #region ISelectHandler & IDeselectHandler

    public void OnSelect(BaseEventData eventData)
    {
        if (!_isFocused)
        {
            _isFocused = true;
            onFocusChanged?.Invoke(true);
            onSelect?.Invoke();
        }
    }

    public void OnDeselect(BaseEventData eventData)
    {
        if (_isFocused)
        {
            _isFocused = false;
            onFocusChanged?.Invoke(false);
            onDeselect?.Invoke();
        }
    }

    #endregion

    #region ISubmitHandler & ICancelHandler

    public void OnSubmit(BaseEventData eventData)
    {
        if (!AllowPressWhenDisabled && !IsInteractable)
            return;

        onSubmit?.Invoke();
    }

    public void OnCancel(BaseEventData eventData)
    {
        onCancel?.Invoke();
    }

    #endregion

    #region IScrollHandler & Drag Handlers

    public void OnScroll(PointerEventData eventData)
    {
        onScroll?.Invoke(eventData.scrollDelta);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        onBeginDrag?.Invoke();
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Required for EventSystem drag dispatching
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        onEndDrag?.Invoke();
    }

    #endregion

    #region Safety Reset on Disable

    private void OnDisable()
    {
        // Reset active states on disable to avoid sticky hints or pressed states
        if (_isHovered)
        {
            _isHovered = false;
            onHoverChanged?.Invoke(false);
            onPointerExit?.Invoke();
        }

        if (_isPressed)
        {
            _isPressed = false;
            onPressChanged?.Invoke(false);
            onPointerUp?.Invoke();
        }

        if (_isFocused)
        {
            _isFocused = false;
            onFocusChanged?.Invoke(false);
            onDeselect?.Invoke();
        }

        _lastClickTime = -1f;
    }

    #endregion
}
