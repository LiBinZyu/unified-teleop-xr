using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace UTXR.Publishing
{
    [Flags]
    public enum XRHandCaptureFeatures { None = 0, Joints = 1, Velocity = 2, TrackingState = 4, All = Joints | Velocity | TrackingState }

    /// <summary>Creates only the requested raw Dynamic hand captures; borrows the active subsystem.</summary>
    [DisallowMultipleComponent]
    public sealed class XRHandJointDataSource : MonoBehaviour
    {
        public Capture OpenCapture() => OpenCapture(XRHandCaptureFeatures.All, XRHandCaptureFeatures.All);
        public Capture OpenCapture(XRHandCaptureFeatures left, XRHandCaptureFeatures right)
        {
            var loader = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager?.activeLoader : null;
            return OpenCapture(loader != null ? loader.GetLoadedSubsystem<XRHandSubsystem>() : null, left, right);
        }
        public Capture OpenCapture(XRHandSubsystem subsystem) => new(subsystem, XRHandCaptureFeatures.All, XRHandCaptureFeatures.All);
        public Capture OpenCapture(XRHandSubsystem subsystem, XRHandCaptureFeatures left, XRHandCaptureFeatures right) => new(subsystem, left, right);

        public sealed class Capture : IXRDataCapture
        {
            private XRHandSubsystem _subsystem;
            private Exception _error;
            private bool _pending;
            public bool HasFrame { get; private set; }
            private readonly XRHandCaptureFeatures _leftFeatures, _rightFeatures;
            public XRHandFrame Left { get; }
            public XRHandFrame Right { get; }

            internal Capture(XRHandSubsystem subsystem, XRHandCaptureFeatures left, XRHandCaptureFeatures right)
            {
                if (subsystem == null || !subsystem.running)
                    throw new InvalidOperationException("The active XR loader must provide a running XRHandSubsystem.");
                _leftFeatures = left; _rightFeatures = right;
                Left = new XRHandFrame((left & XRHandCaptureFeatures.Joints) != 0);
                Right = new XRHandFrame((right & XRHandCaptureFeatures.Joints) != 0);
                _subsystem = subsystem;
                subsystem.updatedHands += OnHandsUpdated;
            }

            private void OnHandsUpdated(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags flags, XRHandSubsystem.UpdateType type)
            {
                if (type != XRHandSubsystem.UpdateType.Dynamic || _error != null) return;
                try
                {
                    if (_leftFeatures != XRHandCaptureFeatures.None)
                        Fill(Left, subsystem.leftHand, flags, XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose,
                            XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints, _leftFeatures);
                    if (_rightFeatures != XRHandCaptureFeatures.None)
                        Fill(Right, subsystem.rightHand, flags, XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose,
                            XRHandSubsystem.UpdateSuccessFlags.RightHandJoints, _rightFeatures);
                    _pending = true;
                }
                catch (Exception error) { _error = error; _pending = false; }
            }

            private static void Fill(XRHandFrame frame, XRHand hand, XRHandSubsystem.UpdateSuccessFlags flags, XRHandSubsystem.UpdateSuccessFlags root, XRHandSubsystem.UpdateSuccessFlags joints, XRHandCaptureFeatures features)
            {
                frame.Clear();
                if (!hand.isTracked || (flags & root) != root) return;
                frame.TrackingState = 3; // Unity InputTrackingState.Position | Rotation
                if ((features & XRHandCaptureFeatures.Velocity) != 0)
                {
                    var wrist = hand.GetJoint(XRHandJointID.Wrist);
                    if (wrist.TryGetLinearVelocity(out var linear)) { frame.LinearVelocity = linear; frame.TrackingState |= 4; }
                    if (wrist.TryGetAngularVelocity(out var angular)) { frame.AngularVelocity = angular; frame.TrackingState |= 8; }
                    if (!Devices.XRDeviceStateCore.IsFinite(frame.LinearVelocity) || !Devices.XRDeviceStateCore.IsFinite(frame.AngularVelocity))
                        throw new InvalidOperationException("Non-finite wrist velocity.");
                }
                if ((features & XRHandCaptureFeatures.Joints) == 0 || (flags & joints) != joints) return;
                for (int i = 0; i < XRHandFrame.JointCount; ++i)
                {
                    var joint = hand.GetJoint(XRHandJointIDUtility.FromIndex(i));
                    frame.States[i] = (int)joint.trackingState;
                    frame.PoseValid[i] = joint.TryGetPose(out frame.Poses[i]);
                }
            }

            public void Refresh()
            {
                HasFrame = false;
                if (_subsystem == null) throw new ObjectDisposedException(nameof(Capture));
                if (!_subsystem.running) throw new InvalidOperationException("The bound XRHandSubsystem stopped.");
                if (_error != null) throw new InvalidOperationException("Hand capture failed.", _error);
                HasFrame = _pending;
                _pending = false;
            }

            public void Dispose()
            {
                if (_subsystem != null) _subsystem.updatedHands -= OnHandsUpdated;
                _subsystem = null;
                HasFrame = _pending = false;
            }
        }
    }
}
