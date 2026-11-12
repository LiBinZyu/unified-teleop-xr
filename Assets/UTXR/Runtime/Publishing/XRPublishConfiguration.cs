using System;
using System.Collections.Generic;
using CycloneDDS;
using UnityEngine;
using UnityEngine.InputSystem;
using UTXR.Devices;

namespace UTXR.Publishing
{
    /// <summary>Scene assembly and message policy. Edit BuildCommon/BuildControllers/BuildHands/BuildBody to compose messages.</summary>
    [ExecuteAlways]
    public sealed class XRPublishConfiguration : MonoBehaviour
    {
        [Serializable]
        public sealed class MessageOption
        {
            public bool enabled = true;
        }

        public readonly struct MessageEntry
        {
            public string Topic { get; }
            public MessageOption Option { get; }
            public MessageEntry(string topic, MessageOption option) { Topic = topic; Option = option; }
        }

        [Header("Messages: enable processing and publishing")]
        public MessageOption headPoseMessage = new();
        public MessageOption headVelocityMessage = new();
        public MessageOption headStateMessage = new();
        public MessageOption eyePoseMessage = new();
        public MessageOption eyeStateMessage = new();
        public MessageOption leftControllerPoseMessage = new();
        public MessageOption leftControllerVelocityMessage = new();
        public MessageOption leftControllerStateMessage = new();
        public MessageOption rightControllerPoseMessage = new();
        public MessageOption rightControllerVelocityMessage = new();
        public MessageOption rightControllerStateMessage = new();
        public MessageOption controllerInputMessage = new();
        public MessageOption leftHandJointsMessage = new();
        public MessageOption leftHandVelocityMessage = new();
        public MessageOption leftHandStateMessage = new();
        public MessageOption rightHandJointsMessage = new();
        public MessageOption rightHandVelocityMessage = new();
        public MessageOption rightHandStateMessage = new();
        public MessageOption bodyPosesMessage = new() { enabled = false };
        public event Action MessagesChanged;
        private volatile bool _settingsDirty;
        private ulong _lastSelection;
        private IReadOnlyList<MessageEntry> _messages;

        // Fixed code-defined UI list. Input mode affects publishing, never the configuration list.
        public IReadOnlyList<MessageEntry> Messages => _messages ??= Array.AsReadOnly(new[] {
            new MessageEntry("/xr/head/pose", headPoseMessage),
            new MessageEntry("/xr/head/velocity", headVelocityMessage),
            new MessageEntry("/xr/head/tracking_state", headStateMessage),
            new MessageEntry("/xr/eye/pose", eyePoseMessage),
            new MessageEntry("/xr/eye/tracking_state", eyeStateMessage),
            new MessageEntry("/xr/left/controller_pose", leftControllerPoseMessage),
            new MessageEntry("/xr/left/controller_velocity", leftControllerVelocityMessage),
            new MessageEntry("/xr/left/controller_tracking_state", leftControllerStateMessage),
            new MessageEntry("/xr/right/controller_pose", rightControllerPoseMessage),
            new MessageEntry("/xr/right/controller_velocity", rightControllerVelocityMessage),
            new MessageEntry("/xr/right/controller_tracking_state", rightControllerStateMessage),
            new MessageEntry("/xr/controller/input", controllerInputMessage),
            new MessageEntry("/xr/left/hand_joints", leftHandJointsMessage),
            new MessageEntry("/xr/left/hand_velocity", leftHandVelocityMessage),
            new MessageEntry("/xr/left/hand_tracking_state", leftHandStateMessage),
            new MessageEntry("/xr/right/hand_joints", rightHandJointsMessage),
            new MessageEntry("/xr/right/hand_velocity", rightHandVelocityMessage),
            new MessageEntry("/xr/right/hand_tracking_state", rightHandStateMessage),
            new MessageEntry("/xr/body/poses", bodyPosesMessage) });

        public void SetMessageEnabled(string topic, bool enabled)
        {
            foreach (var message in Messages)
            {
                if (message.Topic != topic) continue;
                if (message.Option.enabled == enabled) return;
                message.Option.enabled = enabled;
                SynchronizeOptions();
                return;
            }
            throw new ArgumentException($"Unknown publishing topic: {topic}", nameof(topic));
        }

        private void OnEnable() => SynchronizeOptions();
        private void OnValidate() { _messages = null; _settingsDirty = true; }
        private void Update() { if (_settingsDirty) SynchronizeOptions(); }
        private void SynchronizeOptions()
        {
            _settingsDirty = false;
            ulong selection = 0;
            for (int i = 0; i < Messages.Count; i++)
                if (Messages[i].Option.enabled) selection |= 1UL << i;
            if (selection == _lastSelection) return;
            _lastSelection = selection;
            MessagesChanged?.Invoke();
        }

        [Header("Sources and Input Actions")]
        public XRInputActionDataSource actionSource;
        public XRHandJointDataSource handSource;
        public XRBodyPoseDataSource bodySource;
        public InputActionReference headPosition;
        public InputActionReference headRotation;
        public InputActionReference headLinearVelocity;
        public InputActionReference headAngularVelocity;
        public InputActionReference headTrackingState;
        public InputActionReference eyePosition;
        public InputActionReference eyeRotation;
        public InputActionReference eyeTrackingState;
        public InputActionReference leftPosition;
        public InputActionReference leftRotation;
        public InputActionReference leftLinearVelocity;
        public InputActionReference leftAngularVelocity;
        public InputActionReference leftTrackingState;
        public InputActionReference rightPosition;
        public InputActionReference rightRotation;
        public InputActionReference rightLinearVelocity;
        public InputActionReference rightAngularVelocity;
        public InputActionReference rightTrackingState;
        public InputActionReference leftThumbstick;
        public InputActionReference rightThumbstick;
        public InputActionReference leftTrigger;
        public InputActionReference rightTrigger;
        public InputActionReference leftGrip;
        public InputActionReference rightGrip;
        public InputActionReference buttonX;
        public InputActionReference buttonY;
        public InputActionReference buttonA;
        public InputActionReference buttonB;
        public InputActionReference leftThumbstickClick;
        public InputActionReference rightThumbstickClick;

        // Validate only enabled Action messages; inactive-mode sources are validated by CreatePlan.
        public void ValidateReferences()
        {
            foreach (var message in BuildCommon()) { }
            foreach (var message in BuildControllers()) { }
        }

        public XRPublishPlan CreatePlan(XRInputMode mode)
        {
            if (mode != XRInputMode.Controller && mode != XRInputMode.Hand) throw new ArgumentOutOfRangeException(nameof(mode));
            var messages = new List<ActionMessage>(BuildCommon());
            if (mode == XRInputMode.Controller) messages.AddRange(BuildControllers());
            var inputs = new List<XRActionInput>();
            foreach (var message in messages) inputs.AddRange(message.Inputs);
            if (messages.Count > 0) RequireSource(actionSource, nameof(actionSource));
            var source = actionSource; // Freeze component references as well as the message definitions.
            var selectedInputs = inputs.AsReadOnly();
            var actions = new XRPublishSource("Input actions", () =>
            {
                RequireSource(source, nameof(actionSource));
                return source.OpenCapture(selectedInputs);
            });
            var routes = new List<XRPublishRoute>();
            foreach (var message in messages) routes.Add(message.Bind(actions));
            if (mode == XRInputMode.Hand) routes.AddRange(BuildHands());
            if (bodyPosesMessage.enabled) routes.AddRange(BuildBody());
            return new XRPublishPlan(routes);
        }

        private static void RequireSource(Behaviour source, string field)
        {
            if (source == null || !source.isActiveAndEnabled)
                throw new InvalidOperationException($"Assign and enable '{field}' for the selected publishing plan.");
        }

        // Disabled messages never construct inputs, allocate message buffers or acquire sources.
        private IEnumerable<ActionMessage> BuildCommon()
        {
            if (headPoseMessage.enabled) yield return Pose("rt/xr/head/pose", headPosition, headRotation, headTrackingState, DdsQosPreset.SensorData);
            if (headVelocityMessage.enabled) yield return Velocity("rt/xr/head/velocity", headLinearVelocity, headAngularVelocity, headTrackingState, DdsQosPreset.SensorData);
            if (headStateMessage.enabled) yield return State("rt/xr/head/tracking_state", headTrackingState, DdsQosPreset.StateTransient);
            if (eyePoseMessage.enabled) yield return Pose("rt/xr/eye/pose", eyePosition, eyeRotation, eyeTrackingState, DdsQosPreset.SensorData);
            if (eyeStateMessage.enabled) yield return State("rt/xr/eye/tracking_state", eyeTrackingState, DdsQosPreset.StateTransient);
        }

        private IEnumerable<ActionMessage> BuildControllers()
        {
            if (leftControllerPoseMessage.enabled) yield return Pose("rt/xr/left/controller_pose", leftPosition, leftRotation, leftTrackingState, DdsQosPreset.SensorData);
            if (leftControllerVelocityMessage.enabled) yield return Velocity("rt/xr/left/controller_velocity", leftLinearVelocity, leftAngularVelocity, leftTrackingState, DdsQosPreset.SensorData);
            if (leftControllerStateMessage.enabled) yield return State("rt/xr/left/controller_tracking_state", leftTrackingState, DdsQosPreset.StateTransient);
            if (rightControllerPoseMessage.enabled) yield return Pose("rt/xr/right/controller_pose", rightPosition, rightRotation, rightTrackingState, DdsQosPreset.SensorData);
            if (rightControllerVelocityMessage.enabled) yield return Velocity("rt/xr/right/controller_velocity", rightLinearVelocity, rightAngularVelocity, rightTrackingState, DdsQosPreset.SensorData);
            if (rightControllerStateMessage.enabled) yield return State("rt/xr/right/controller_tracking_state", rightTrackingState, DdsQosPreset.StateTransient);
            if (controllerInputMessage.enabled) yield return ControllerInputs(DdsQosPreset.Default);
        }

        private IEnumerable<XRPublishRoute> BuildHands()
        {
            var left = HandFeatures(leftHandJointsMessage, leftHandVelocityMessage, leftHandStateMessage);
            var right = HandFeatures(rightHandJointsMessage, rightHandVelocityMessage, rightHandStateMessage);
            if (left == XRHandCaptureFeatures.None && right == XRHandCaptureFeatures.None) yield break;
            RequireSource(handSource, nameof(handSource));
            var source = handSource;
            var hands = new XRPublishSource("XR Hands", () => { RequireSource(source, nameof(handSource)); return source.OpenCapture(left, right); });
            if (leftHandJointsMessage.enabled) yield return HandJoints(hands, "rt/xr/left/hand_joints", true, DdsQosPreset.SensorData);
            if (rightHandJointsMessage.enabled) yield return HandJoints(hands, "rt/xr/right/hand_joints", false, DdsQosPreset.SensorData);
            if (leftHandVelocityMessage.enabled) yield return HandVelocity(hands, "rt/xr/left/hand_velocity", true, DdsQosPreset.SensorData);
            if (rightHandVelocityMessage.enabled) yield return HandVelocity(hands, "rt/xr/right/hand_velocity", false, DdsQosPreset.SensorData);
            if (leftHandStateMessage.enabled) yield return HandState(hands, "rt/xr/left/hand_tracking_state", true);
            if (rightHandStateMessage.enabled) yield return HandState(hands, "rt/xr/right/hand_tracking_state", false);
        }

        private static XRHandCaptureFeatures HandFeatures(MessageOption joints, MessageOption velocity, MessageOption state) =>
            (joints.enabled ? XRHandCaptureFeatures.Joints : 0) |
            (velocity.enabled ? XRHandCaptureFeatures.Velocity : 0) |
            (state.enabled ? XRHandCaptureFeatures.TrackingState : 0);

        private static XRPublishRoute HandState(XRPublishSource source, string topic, bool left) =>
            XRPublishRoute.Create<XRHandJointDataSource.Capture>(source,
                new XRPublishChannel(topic, DdsMessageType.StdUInt32, DdsQosPreset.StateTransient), "Whole-hand tracking flags", capture =>
                    capture.HasFrame ? new XRPublishSample(integer: (left ? capture.Left : capture.Right).TrackingState) : null);

        private IEnumerable<XRPublishRoute> BuildBody()
        {
            RequireSource(bodySource, nameof(bodySource));
            var source = bodySource;
            var body = new XRPublishSource("Body", () => { RequireSource(source, nameof(bodySource)); return source.OpenCapture(); });
            float[] buffer = null;
            return new[] { XRPublishRoute.Create<XRBodyPoseDataSource.Capture>(body,
                new XRPublishChannel("rt/xr/body/poses", DdsMessageType.StdFloat32MultiArray, DdsQosPreset.SensorData),
                "Scene-world poses in configured skeleton order", capture =>
                {
                    buffer ??= new float[3 + capture.Frame.JointPoses.Length * 8];
                    XRPublishEncoding.Body(capture.Frame, buffer);
                    return new XRPublishSample(array: buffer);
                }) };
        }

        private const int PoseBits = (int)(UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation);
        private const int VelocityBits = (int)(UnityEngine.XR.InputTrackingState.Velocity | UnityEngine.XR.InputTrackingState.AngularVelocity);
        private static XRActionInput Input(InputActionReference reference, XRActionValueKind kind) => new(reference, kind);

        private static ActionMessage Pose(string topic, InputActionReference position, InputActionReference rotation, InputActionReference state, DdsQosPreset qos) =>
            new(new XRPublishChannel(topic, DdsMessageType.GeometryPoseStamped, qos, "xr_tracking", true),
                new[] { Input(position, XRActionValueKind.Vector3), Input(rotation, XRActionValueKind.Quaternion), Input(state, XRActionValueKind.Integer) },
                a => (a[2].Integer & PoseBits) == PoseBits ? new XRPublishSample(vector: a[0].Vector3, rotation: a[1].Rotation) : null);

        private static ActionMessage Velocity(string topic, InputActionReference linear, InputActionReference angular, InputActionReference state, DdsQosPreset qos) =>
            new(new XRPublishChannel(topic, DdsMessageType.GeometryTwistStamped, qos, "xr_tracking", true),
                new[] { Input(linear, XRActionValueKind.Vector3), Input(angular, XRActionValueKind.Vector3), Input(state, XRActionValueKind.Integer) },
                a => (a[2].Integer & VelocityBits) == VelocityBits ? new XRPublishSample(vector: a[0].Vector3, angular: a[1].Vector3) : null);

        private static ActionMessage State(string topic, InputActionReference state, DdsQosPreset qos) =>
            new(new XRPublishChannel(topic, DdsMessageType.StdUInt32, qos), new[] { Input(state, XRActionValueKind.Integer) },
                a => new XRPublishSample(integer: a[0].Integer));

        private ActionMessage ControllerInputs(DdsQosPreset qos)
        {
            var axes = new float[8];
            var buttons = new int[6];
            return new ActionMessage(new XRPublishChannel("rt/xr/controller/input", DdsMessageType.SensorJoy, qos, "xr_tracking"),
                new[] { Input(leftThumbstick, XRActionValueKind.Vector2), Input(leftTrigger, XRActionValueKind.Float), Input(leftGrip, XRActionValueKind.Float),
                    Input(rightThumbstick, XRActionValueKind.Vector2), Input(rightTrigger, XRActionValueKind.Float), Input(rightGrip, XRActionValueKind.Float),
                    Input(buttonX, XRActionValueKind.Button), Input(buttonY, XRActionValueKind.Button), Input(buttonA, XRActionValueKind.Button),
                    Input(buttonB, XRActionValueKind.Button), Input(leftThumbstickClick, XRActionValueKind.Button), Input(rightThumbstickClick, XRActionValueKind.Button) }, a =>
                {
                    axes[0] = a[0].Vector2.x; axes[1] = a[0].Vector2.y; axes[2] = a[1].Float; axes[3] = a[2].Float;
                    axes[4] = a[3].Vector2.x; axes[5] = a[3].Vector2.y; axes[6] = a[4].Float; axes[7] = a[5].Float;
                    for (int i = 0; i < buttons.Length; ++i) buttons[i] = a[i + 6].Integer;
                    return new XRPublishSample(array: axes, buttons: buttons);
                }, acceptPartialInputs: true);
        }

        private static XRPublishRoute HandJoints(XRPublishSource source, string topic, bool left, DdsQosPreset qos)
        {
            var buffer = new float[XRHandFrame.JointCount * 8];
            return XRPublishRoute.Create<XRHandJointDataSource.Capture>(source,
                new XRPublishChannel(topic, DdsMessageType.StdFloat32MultiArray, qos), "Dynamic joint states and poses", capture =>
                {
                    if (!capture.HasFrame) return null;
                    XRPublishEncoding.Hand(left ? capture.Left : capture.Right, buffer);
                    return new XRPublishSample(array: buffer);
                });
        }

        private static XRPublishRoute HandVelocity(XRPublishSource source, string topic, bool left, DdsQosPreset qos) =>
            XRPublishRoute.Create<XRHandJointDataSource.Capture>(source,
                new XRPublishChannel(topic, DdsMessageType.GeometryTwistStamped, qos, "xr_tracking", true), "Native wrist linear + angular velocity", capture =>
                {
                    var frame = left ? capture.Left : capture.Right;
                    return capture.HasFrame && (frame.TrackingState & VelocityBits) == VelocityBits
                        ? new XRPublishSample(vector: frame.LinearVelocity, angular: frame.AngularVelocity) : null;
                });

        // Message declarations bind cached raw values to wire samples. The source never sees this class.
        private sealed class ActionMessage
        {
            private readonly XRPublishChannel _channel;
            public XRActionInput[] Inputs { get; }
            private readonly Func<XRActionValue[], XRPublishSample?> _compose;
            private readonly bool _acceptPartialInputs;

            public ActionMessage(XRPublishChannel channel, XRActionInput[] inputs, Func<XRActionValue[], XRPublishSample?> compose,
                bool acceptPartialInputs = false)
            {
                _channel = channel;
                Inputs = inputs;
                _compose = compose;
                _acceptPartialInputs = acceptPartialInputs;
            }

            public XRPublishRoute Bind(XRPublishSource source)
            {
                var values = new XRActionValue[Inputs.Length];
                var descriptions = new string[Inputs.Length];
                for (int i = 0; i < Inputs.Length; ++i)
                {
                    var paths = new List<string>();
                    foreach (var binding in Inputs[i].Action.bindings) paths.Add(binding.effectivePath);
                    descriptions[i] = $"{Inputs[i].Action.name} ({Inputs[i].Kind}): {string.Join(", ", paths)}";
                }
                return XRPublishRoute.Create<XRInputActionDataSource.Capture>(source, _channel, string.Join("; ", descriptions), capture =>
                {
                    bool hasInput = false;
                    for (int i = 0; i < Inputs.Length; ++i)
                    {
                        values[i] = capture.Read(Inputs[i]);
                        if (!values[i].Available && !_acceptPartialInputs) return null;
                        hasInput |= values[i].Available;
                    }
                    return hasInput ? _compose(values) : null;
                });
            }
        }
    }
}
