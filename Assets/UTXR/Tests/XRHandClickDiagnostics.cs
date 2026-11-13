using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace UTXR.Devices
{
    /// <summary>Read-only tracing of hand input through XRI to UI. Disable after diagnosis.</summary>
    [DisallowMultipleComponent]
    public sealed class XRHandClickDiagnostics : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private bool logToConsole = true;
        [SerializeField, Min(0.05f)] private float sampleInterval = 0.1f;
        private readonly Dictionary<string, string> _previous = new Dictionary<string, string>();
        private readonly List<InputAction> _actions = new List<InputAction>();
        private NearFarInteractor[] _interactors;
        private XRUIInputModule _module;
        private float _nextSample, _nextHeartbeat;
        private bool _heartbeat;
        private static readonly string[] Controls =
        {
            "isTracked", "pinchValue", "pinchTouched", "pinchReady",
            "pointerActivateValue", "pointerActivated", "pointerActivateReady",
            "indexPressed", "pinchStrengthIndex"
        };

        private void OnEnable()
        {
            _previous.Clear();
            _nextSample = _nextHeartbeat = 0f;
            _interactors = FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var side in new[] { "Left", "Right" })
            foreach (var name in new[] { "Select", "Select Value", "UI Press", "UI Press Value" })
            {
                var action = inputActions != null ? inputActions.FindAction($"XRI {side} Interaction/{name}", false) : null;
                if (action == null)
                {
                    Write($"CONFIG missing action: XRI {side} Interaction/{name}");
                    continue;
                }
                _actions.Add(action);
                action.started += OnAction;
                action.performed += OnAction;
                action.canceled += OnAction;
            }
            // Observe only: never enable actions, change bindings, or inject UI events.
        }

        private void OnDisable()
        {
            foreach (var action in _actions)
            {
                action.started -= OnAction;
                action.performed -= OnAction;
                action.canceled -= OnAction;
            }
            _actions.Clear();
            BindModule(null);
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextSample) return;
            _nextSample = Time.unscaledTime + Mathf.Max(0.05f, sampleInterval);
            _heartbeat = Time.unscaledTime >= _nextHeartbeat;
            if (_heartbeat) _nextHeartbeat = Time.unscaledTime + 2f;
            var current = EventSystem.current;
            BindModule(current != null ? current.currentInputModule as XRUIInputModule : null);
            Snapshot("EVENTSYSTEM", $"focused={Application.isFocused} module={(current != null && current.currentInputModule != null ? current.currentInputModule.GetType().Name : "none")}");

            int handCount = 0;
            foreach (var device in InputSystem.devices)
            {
                if (!(device.TryGetChildControl<AxisControl>("pinchValue") != null ||
                      device.TryGetChildControl<AxisControl>("pinchStrengthIndex") != null)) continue;
                ++handCount;
                var line = new StringBuilder($"layout={device.layout} usages={string.Join(",", device.usages)} enabled={device.enabled}");
                foreach (var name in Controls)
                {
                    var control = device.TryGetChildControl<AxisControl>(name);
                    line.Append($" {name}={(control != null ? Number(control.ReadValue()) : "unavailable")}");
                }
                Snapshot($"RAW device={device.deviceId}", line.ToString());
            }
            Snapshot("DEVICES", $"handInputDevices={handCount}");
            foreach (var action in _actions)
            {
                var controls = new StringBuilder();
                foreach (var control in action.controls) controls.Append(control.path).Append(';');
                Snapshot($"ACTION {action.actionMap.name}/{action.name}",
                    $"enabled={action.enabled} phase={action.phase} value={Number(action.ReadValue<float>())} active={action.activeControl?.path ?? "none"} resolved=[{controls}]");
            }
            foreach (var interactor in _interactors)
            {
                if (interactor == null || interactor.gameObject.scene != gameObject.scene) continue;
                interactor.TryGetCurrentUIRaycastResult(out var hit);
                var reader = interactor.uiPressInput;
                string modelState = "unregistered";
                if (_module != null && _module.GetTrackedDeviceModel(interactor, out var model))
                    modelState = $"pointerId={model.pointerId} select={model.select}";
                Snapshot($"INTERACTOR {Path(interactor.gameObject)}",
                    $"active={interactor.isActiveAndEnabled} uiEnabled={interactor.enableUIInteraction} " +
                    $"readerMode={reader?.inputSourceMode} pressed={reader?.ReadIsPerformed()} value={(reader != null ? Number(reader.ReadValue()) : "none")} " +
                    $"model=[{modelState}] hit={Path(hit.gameObject)} {TargetState(hit.gameObject)}");
            }
        }

        private void BindModule(XRUIInputModule module)
        {
            if (_module == module) return;
            if (_module != null)
            {
                _module.pointerDown -= OnDown;
                _module.pointerUp -= OnUp;
                _module.pointerClick -= OnClick;
            }
            _module = module;
            if (_module == null) return;
            _module.pointerDown += OnDown;
            _module.pointerUp += OnUp;
            _module.pointerClick += OnClick;
        }

        private void OnAction(InputAction.CallbackContext context)
        {
            // Value actions may fire every frame. Snapshot them at sampleInterval instead.
            if (context.action.type != UnityEngine.InputSystem.InputActionType.Button) return;
            Write($"EDGE {context.action.actionMap.name}/{context.action.name} phase={context.phase} " +
                  $"value={Number(context.ReadValue<float>())} control={context.control?.path}");
        }

        private void OnDown(GameObject target, PointerEventData data) => UIEvent("DOWN", target, data);
        private void OnUp(GameObject target, PointerEventData data) => UIEvent("UP", target, data);
        private void OnClick(GameObject target, PointerEventData data) => UIEvent("CLICK", target, data);
        private void UIEvent(string kind, GameObject target, PointerEventData data)
        {
            Write($"UI {kind} pointerId={data.pointerId} target={Path(target)} " +
                  $"eligible={data.eligibleForClick} dragging={data.dragging} {TargetState(target)}");
        }

        private static string TargetState(GameObject target)
        {
            if (target == null) return "selectable=none";
            var selectable = target.GetComponentInParent<Selectable>();
            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            return $"clickHandler={Path(clickHandler)} selectable={(selectable != null ? selectable.GetType().Name : "none")} " +
                   $"interactable={(selectable != null ? selectable.IsInteractable().ToString() : "n/a")}";
        }

        private static string Path(GameObject target)
        {
            if (target == null) return "none";
            string path = target.name;
            for (var parent = target.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        private static string Number(float value) => value.ToString("F2", CultureInfo.InvariantCulture);
        private void Snapshot(string key, string value)
        {
            if (!_heartbeat && _previous.TryGetValue(key, out var previous) && previous == value) return;
            _previous[key] = value;
            Write(key + " " + value);
        }

        private void Write(string message)
        {
            string line = $"[HandClickDebug] frame={Time.frameCount} {message}";
            UTXR.Core.Logger.LogApp(line);
            if (logToConsole) Debug.Log(line, this);
        }
    }
}
