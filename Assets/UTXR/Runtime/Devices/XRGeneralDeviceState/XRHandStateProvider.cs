using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace UTXR.Devices
{
    /// <summary>Reads raw root pose and wrist velocity from the supplied XR Hands subsystem.</summary>
    public sealed class XRHandStateProvider : IXRDeviceStateProvider
    {
        private readonly XRHandSubsystem _subsystem;
        private XRDeviceSample _left, _right;
        private bool _disposed;
        public XRInputMode Mode => XRInputMode.Hand;
        public XRProviderStatus Status { get; private set; }

        public XRHandStateProvider(XRHandSubsystem subsystem) { _subsystem = subsystem; }

        public void Sample()
        {
            ThrowIfDisposed();
            if (_subsystem == null)
                Status = new XRProviderStatus(XRProviderStatusCode.ProviderMissing,
                    "The active XR loader did not supply XRHandSubsystem. Check Hand Tracking support and call RefreshProvider().");
            else if (!_subsystem.running)
                Status = new XRProviderStatus(XRProviderStatusCode.ProviderNotRunning,
                    "The bound XRHandSubsystem is not running. Its owner must start it.");
            else Status = XRProviderStatus.Ready;
            if (!Status.IsReady)
            {
                _left = _right = new XRDeviceSample(Status, InputTrackingState.None, false);
                return;
            }
            _left = SampleHand(_subsystem.leftHand, XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose,
                XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints);
            _right = SampleHand(_subsystem.rightHand, XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose,
                XRHandSubsystem.UpdateSuccessFlags.RightHandJoints);
        }

        private XRDeviceSample SampleHand(XRHand hand, XRHandSubsystem.UpdateSuccessFlags rootFlag,
            XRHandSubsystem.UpdateSuccessFlags jointsFlag)
        {
            bool rootValid = (_subsystem.updateSuccessFlags & rootFlag) != 0;
            bool jointsValid = hand.isTracked && (_subsystem.updateSuccessFlags & jointsFlag) != 0;
            var tracking = rootValid ? InputTrackingState.Position | InputTrackingState.Rotation : InputTrackingState.None;
            var wrist = hand.GetJoint(XRHandJointID.Wrist);
            Vector3? linear = jointsValid && wrist.TryGetLinearVelocity(out var lv) ? lv : (Vector3?)null;
            Vector3? angular = jointsValid && wrist.TryGetAngularVelocity(out var av) ? av : (Vector3?)null;
            if (linear.HasValue) tracking |= InputTrackingState.Velocity;
            if (angular.HasValue) tracking |= InputTrackingState.AngularVelocity;
            return new XRDeviceSample(XRProviderStatus.Ready, tracking, hand.isTracked,
                rootValid ? hand.rootPose : Pose.identity, linear, angular);
        }

        public XRDeviceSample GetSample(XRSide side)
        {
            ThrowIfDisposed();
            switch (side)
            {
                case XRSide.Left: return _left;
                case XRSide.Right: return _right;
                default: throw new ArgumentOutOfRangeException(nameof(side));
            }
        }

        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(XRHandStateProvider)); }
        public void Dispose() { _disposed = true; }
    }
}
