using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UTXR.Publishing
{
    public enum XRActionValueKind { Float, Integer, Vector2, Vector3, Quaternion, Button }

    public readonly struct XRActionInput
    {
        public InputAction Action { get; }
        public XRActionValueKind Kind { get; }
        public XRActionInput(InputActionReference reference, XRActionValueKind kind)
        {
            Action = reference != null ? reference.action : null;
            if (Action == null || Action.bindings.Count == 0)
                throw new ArgumentException("A bound InputActionReference is required.", nameof(reference));
            Kind = kind;
        }
    }

    public readonly struct XRActionValue
    {
        public bool Available { get; }
        public Vector2 Vector2 { get; }
        public Vector3 Vector3 { get; }
        public Quaternion Rotation { get; }
        public float Float { get; }
        public int Integer { get; }
        internal XRActionValue(InputAction action, XRActionValueKind kind)
        {
            this = default;
            if (!action.enabled) throw new InvalidOperationException($"Owned action '{action.name}' was disabled.");
            if (action.controls.Count == 0) return; // A valid binding can have no connected device.
            Available = true;
            switch (kind)
            {
                case XRActionValueKind.Float: Float = action.ReadValue<float>(); break;
                case XRActionValueKind.Integer: Integer = action.ReadValue<int>(); break;
                case XRActionValueKind.Vector2: Vector2 = action.ReadValue<Vector2>(); break;
                case XRActionValueKind.Vector3: Vector3 = action.ReadValue<Vector3>(); break;
                case XRActionValueKind.Quaternion: Rotation = action.ReadValue<Quaternion>(); break;
                case XRActionValueKind.Button: Integer = action.IsPressed() ? 1 : 0; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    /// <summary>Creates owned raw-action captures. Has no knowledge of messages, DDS or input modes.</summary>
    [DisallowMultipleComponent]
    public sealed class XRInputActionDataSource : MonoBehaviour
    {
        public Capture OpenCapture(IReadOnlyList<XRActionInput> inputs) => new(inputs);

        public sealed class Capture : IXRDataCapture
        {
            private sealed class Entry
            {
                public InputAction Action;
                public XRActionValueKind Kind;
                public XRActionValue Value;
            }
            private readonly Dictionary<InputAction, Entry> _entries = new();
            private bool _disposed;
            private bool _refreshed;

            internal Capture(IReadOnlyList<XRActionInput> inputs)
            {
                if (inputs == null || inputs.Count == 0) throw new ArgumentException("Provide raw inputs to capture.", nameof(inputs));
                try
                {
                    foreach (var input in inputs)
                    {
                        var original = input.Action ?? throw new ArgumentException("Missing action.");
                        if (_entries.TryGetValue(original, out var existing))
                        {
                            if (existing.Kind != input.Kind) throw new InvalidOperationException($"Action '{original.name}' has conflicting value kinds.");
                            continue;
                        }
                        if (original.actionMap?.bindingMask != null || original.actionMap?.asset?.bindingMask != null || original.actionMap?.devices != null)
                            throw new InvalidOperationException($"Action '{original.name}' uses inherited masks/devices; use explicitly bound actions.");
                        var clone = original.Clone();
                        _entries.Add(original, new Entry { Action = clone, Kind = input.Kind });
                        clone.bindingMask = original.bindingMask;
                        clone.Enable();
                    }
                }
                catch (Exception cause)
                {
                    try { Dispose(); }
                    catch (Exception cleanup) { throw new AggregateException(cause, cleanup); }
                    throw;
                }
            }

            public void Refresh()
            {
                ThrowIfDisposed();
                _refreshed = false;
                foreach (var entry in _entries.Values)
                {
                    try { entry.Value = new XRActionValue(entry.Action, entry.Kind); }
                    catch (Exception error) { throw new InvalidOperationException($"Read action '{entry.Action.name}' as {entry.Kind} failed.", error); }
                }
                _refreshed = true;
            }

            public XRActionValue Read(XRActionInput input)
            {
                ThrowIfDisposed();
                if (!_refreshed) throw new InvalidOperationException("Refresh the capture before reading its values.");
                if (!_entries.TryGetValue(input.Action, out var entry) || entry.Kind != input.Kind)
                    throw new InvalidOperationException($"Action '{input.Action?.name}' was not acquired with kind {input.Kind}.");
                return entry.Value;
            }

            private void ThrowIfDisposed()
            {
                if (_disposed) throw new ObjectDisposedException(nameof(Capture));
            }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                List<Exception> errors = null;
                foreach (var entry in _entries.Values)
                {
                    try { entry.Action.Dispose(); }
                    catch (Exception error) { (errors ??= new()).Add(error); }
                }
                _entries.Clear();
                if (errors != null) throw new AggregateException("Action capture teardown failed.", errors);
            }
        }
    }
}
