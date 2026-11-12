using System;
using UnityEngine;
using UnityEngine.XR;

namespace UTXR.Devices
{
    public enum XRInputMode { Controller, Hand }
    public enum XRSide { Left, Right }

    [Flags]
    public enum XRGesture { None = 0, Swing = 1, TripleSwing = 2 }

    /// <summary>Raw provider data in XR tracking space. Missing runtime velocity is null.</summary>
    public readonly struct XRDeviceSample
    {
        public XRProviderStatus Status { get; }
        public InputTrackingState TrackingState { get; }
        public bool IsTracked { get; }
        public Pose Pose { get; }
        public Vector3? LinearVelocity { get; } // metres/second
        public Vector3? AngularVelocity { get; } // radians/second

        public XRDeviceSample(XRProviderStatus status, InputTrackingState trackingState, bool isTracked,
            Pose pose = default, Vector3? linearVelocity = null, Vector3? angularVelocity = null)
        {
            Status = status;
            TrackingState = trackingState;
            IsTracked = isTracked;
            Pose = pose;
            LinearVelocity = linearVelocity;
            AngularVelocity = angularVelocity;
        }
    }

    /// <summary>Processed state. Gestures are pulses for the current sample, not latched values.</summary>
    public readonly struct XRDeviceState
    {
        public XRDeviceSample Raw { get; }
        public XRProviderStatus Status { get; }
        public InputTrackingState RawTrackingState => Raw.TrackingState;
        public bool IsTracked { get; }
        public bool IsWarning { get; }
        public Pose Pose => IsTracked ? Raw.Pose : UnityEngine.Pose.identity;
        public Vector3? LinearVelocity => IsTracked ? Raw.LinearVelocity : null;
        public Vector3? AngularVelocity => IsTracked ? Raw.AngularVelocity : null;
        public XRGesture Gestures { get; }

        public XRDeviceState(XRDeviceSample raw, bool isTracked, bool isWarning = false,
            XRGesture gestures = XRGesture.None, XRProviderStatus? status = null)
        {
            Raw = raw;
            Status = status ?? raw.Status;
            IsTracked = Status.IsReady && isTracked;
            IsWarning = Status.IsReady && isWarning;
            Gestures = IsTracked ? gestures : XRGesture.None;
        }

        internal bool HasSameTrackingState(XRDeviceState other) => Status.Equals(other.Status) &&
            RawTrackingState == other.RawTrackingState && IsTracked == other.IsTracked && IsWarning == other.IsWarning;
    }
}
