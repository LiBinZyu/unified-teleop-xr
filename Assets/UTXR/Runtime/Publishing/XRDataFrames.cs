using System;
using UnityEngine;

namespace UTXR.Publishing
{
    /// <summary>Owned, main-thread capture. Refresh once before reading a publishing tick.</summary>
    public interface IXRDataCapture : IDisposable
    {
        void Refresh();
    }

    /// <summary>Raw tracking-space measurements. Storage belongs to the capture until its next update.</summary>
    public sealed class XRHandFrame
    {
        public const int JointCount = 26;
        public int TrackingState { get; internal set; }
        public Vector3 LinearVelocity { get; internal set; }
        public Vector3 AngularVelocity { get; internal set; }
        public ReadOnlyMemory<Pose> JointPoses => Poses;
        public ReadOnlyMemory<int> JointTrackingStates => States;
        internal readonly Pose[] Poses;
        internal readonly int[] States;
        internal readonly bool[] PoseValid;
        public XRHandFrame(bool captureJoints = true)
        {
            Poses = captureJoints ? new Pose[JointCount] : Array.Empty<Pose>();
            States = captureJoints ? new int[JointCount] : Array.Empty<int>();
            PoseValid = captureJoints ? new bool[JointCount] : Array.Empty<bool>();
        }

        internal void Clear()
        {
            TrackingState = 0;
            LinearVelocity = AngularVelocity = default;
            Array.Clear(Poses, 0, Poses.Length);
            Array.Clear(States, 0, States.Length);
            Array.Clear(PoseValid, 0, PoseValid.Length);
        }
    }

    /// <summary>Raw scene-world poses, in the configured body's joint order.</summary>
    public sealed class XRBodyFrame
    {
        public bool IsValid { get; internal set; }
        public ReadOnlyMemory<Pose> JointPoses => Poses;
        internal readonly Pose[] Poses;
        internal XRBodyFrame(int count) => Poses = new Pose[count];
    }
}
