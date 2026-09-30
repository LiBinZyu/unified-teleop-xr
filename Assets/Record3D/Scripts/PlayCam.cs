using UnityEngine;
using UnityEngine.InputSystem;

namespace Record3D
{
    /// <summary>
    /// Universal Unity Editor Scene-View style camera controller for Play Mode.
    /// Provides the exact 1:1 interaction feel of Unity's Scene View navigation.
    /// 
    /// Controls (Identical to Unity Editor):
    /// - Hold RMB + Mouse Move       : Free Look (First-Person Flythrough)
    /// - Hold RMB + W / A / S / D    : Fly Forward / Left / Back / Right
    /// - Hold RMB + Q / E            : Fly Down / Up
    /// - Hold RMB + Left Shift       : Fly Sprint Boost
    /// - Hold RMB + Scroll Wheel     : Adjust Fly Speed
    /// - Hold MMB (or Alt + MMB)     : Pan View
    /// - Alt + Hold LMB + Mouse Move : Orbit (Tumble) around Pivot
    /// - Alt + Hold RMB + Mouse Drag : Smooth Zoom towards Pivot
    /// - Mouse Scroll Wheel          : Step Zoom towards Pivot
    /// - F Key                       : Focus / Frame Target
    /// - Left Click (alone)          : Free cursor for UI interaction
    /// </summary>
    public class PlayCam : MonoBehaviour
    {
        public static PlayCam Instance { get; private set; }

        [Header("Movement & Speed")]
        [SerializeField] private float _moveSpeed = 2.0f;
        [SerializeField] private float _boostMultiplier = 3.0f;

        [Header("Sensitivity")]
        [SerializeField] private float _lookSensitivity = 2.5f;
        [SerializeField] private float _orbitSensitivity = 3.0f;
        [SerializeField] private float _panSensitivity = 1.2f;
        [SerializeField] private float _zoomSensitivity = 2.0f;
        [SerializeField] private float _scrollSensitivity = 4.0f;

        [Header("Target & Pivot")]
        [SerializeField] private Vector3 _pivot = new Vector3(0f, 0f, 1.2f);
        [SerializeField] private float _distanceToPivot = 1.6f;

        private float _yaw = 0f;
        private float _pitch = 0f;
        private bool _isCursorLocked = false;

        private void Awake()
        {
            Instance = this;

            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x;
            if (_pitch > 180f) _pitch -= 360f;

            _distanceToPivot = Vector3.Distance(transform.position, _pivot);
            if (_distanceToPivot < 0.1f) _distanceToPivot = 1.6f;

            var cam = GetComponent<Camera>();
            if (cam != null && cam.nearClipPlane > 0.05f)
            {
                cam.nearClipPlane = 0.05f;
            }
        }

        private void Start()
        {
            if (transform.position.z <= -4.0f)
            {
                Focus();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;

            bool alt = keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;
            bool lmb = mouse.leftButton.isPressed;
            bool rmb = mouse.rightButton.isPressed;
            bool mmb = mouse.middleButton.isPressed;

            bool isAltOrbit = alt && lmb;
            bool isAltZoom = alt && rmb;
            bool isFlythrough = !alt && rmb;
            bool isPanning = mmb || (alt && mmb);

            // 1. Cursor Lock State
            bool shouldLockCursor = isFlythrough || isAltOrbit || isAltZoom || isPanning;
            UpdateCursorLock(shouldLockCursor);

            // 新版 Input System 鼠标像素位移与旧版 GetAxis("Mouse X/Y") 对齐（乘以 0.05f 保证原有灵敏度手感）
            Vector2 mouseDelta = mouse.delta.ReadValue() * 0.05f;
            float mouseX = mouseDelta.x;
            float mouseY = mouseDelta.y;

            // 新版 Input System 滚轮单格通常为 120，旧版约为 0.1，除以 1200f 对齐缩放比例
            float mouseScroll = mouse.scroll.ReadValue().y / 1200f;

            // 2. Alt + LMB: Orbit around Pivot (Unity Scene View Alt-Tumble)
            if (isAltOrbit)
            {
                float rotX = mouseX * _orbitSensitivity;
                float rotY = mouseY * _orbitSensitivity;

                _yaw += rotX;
                _pitch -= rotY;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);

                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                transform.position = _pivot - transform.forward * _distanceToPivot;
                return;
            }

            // 3. Alt + RMB: Smooth Drag Zoom
            if (isAltZoom)
            {
                float zoomDelta = (mouseX + mouseY) * _zoomSensitivity * (_distanceToPivot * 0.05f + 0.05f);

                _distanceToPivot = Mathf.Max(0.05f, _distanceToPivot - zoomDelta);
                transform.position = _pivot - transform.forward * _distanceToPivot;
                return;
            }

            // 4. MMB: Pan View (Unity Scene View Pan)
            if (isPanning)
            {
                float panX = mouseX * _panSensitivity * (_distanceToPivot * 0.04f + 0.04f);
                float panY = mouseY * _panSensitivity * (_distanceToPivot * 0.04f + 0.04f);

                Vector3 pan = -transform.right * panX - transform.up * panY;
                transform.position += pan;
                _pivot += pan;
                return;
            }

            // 5. RMB: Flythrough Look & Flight (Unity Scene View Flythrough)
            if (isFlythrough)
            {
                // Free look rotation
                float rotX = mouseX * _lookSensitivity;
                float rotY = mouseY * _lookSensitivity;

                _yaw += rotX;
                _pitch -= rotY;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);

                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

                // Adjust fly speed with scroll wheel while holding RMB
                float scrollInFly = mouseScroll;
                if (Mathf.Abs(scrollInFly) > 0.001f)
                {
                    _moveSpeed = Mathf.Clamp(_moveSpeed * Mathf.Pow(1.25f, scrollInFly * 10f), 0.1f, 50.0f);
                }

                // WASD + QE flight
                bool isBoosting = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                float speed = _moveSpeed * (isBoosting ? _boostMultiplier : 1.0f) * Time.unscaledDeltaTime;
                Vector3 move = Vector3.zero;

                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move += transform.forward;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move -= transform.forward;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move += transform.right;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move -= transform.right;
                if (keyboard.eKey.isPressed || keyboard.spaceKey.isPressed) move += Vector3.up;
                if (keyboard.qKey.isPressed || keyboard.cKey.isPressed) move -= Vector3.up;

                if (move.sqrMagnitude > 0.0001f)
                {
                    Vector3 delta = move.normalized * speed;
                    transform.position += delta;
                    _pivot += delta;
                }

                return;
            }

            // 6. Scroll Wheel Zoom (When not holding RMB)
            float scroll = mouseScroll;
            if (Mathf.Abs(scroll) > 0.0005f)
            {
                float factor = Mathf.Pow(0.85f, scroll * _scrollSensitivity);
                _distanceToPivot = Mathf.Clamp(_distanceToPivot * factor, 0.05f, 100.0f);
                transform.position = _pivot - transform.forward * _distanceToPivot;
            }

            // 7. Focus Target (F Key)
            if (keyboard.fKey.wasPressedThisFrame)
            {
                Focus();
            }
        }

        private void UpdateCursorLock(bool shouldLock)
        {
            if (shouldLock && !_isCursorLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                _isCursorLocked = true;
            }
            else if (!shouldLock && _isCursorLocked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _isCursorLocked = false;
            }
        }

        public void Focus(Vector3? target = null, Vector3? position = null)
        {
            _pivot = target ?? new Vector3(0f, 0f, 1.2f);
            Vector3 pos = position ?? (_pivot - new Vector3(0f, -0.2f, 1.6f));

            transform.position = pos;
            transform.LookAt(_pivot);

            _distanceToPivot = Vector3.Distance(transform.position, _pivot);
            if (_distanceToPivot < 0.1f) _distanceToPivot = 1.6f;

            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x;
            if (_pitch > 180f) _pitch -= 360f;

            var cam = GetComponent<Camera>();
            if (cam != null) cam.nearClipPlane = 0.05f;
            Record3DLogger.Info(Record3DLogCategory.General, $"PlayCam focused to {transform.position}, looking at {_pivot}");
        }

        private void OnDisable()
        {
            if (_isCursorLocked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _isCursorLocked = false;
            }
        }

        private void OnDestroy()
        {
            if (_isCursorLocked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _isCursorLocked = false;
            }
        }
    }

    [System.Obsolete("Record3DCameraController has been renamed to PlayCam.")]
    public class Record3DCameraController : PlayCam
    {
    }
}