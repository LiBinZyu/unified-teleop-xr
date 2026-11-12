using System;
using UnityEngine;
using UTXR.BodyTracking;

namespace UTXR.Publishing
{
    /// <summary>Raw body poses from an explicitly assigned coordinate system. Never starts a provider.</summary>
    [DisallowMultipleComponent]
    public sealed class XRBodyPoseDataSource : MonoBehaviour
    {
        public BodyTrackingCoordinateSystem coordinateSystem;
        public Capture OpenCapture() => new(coordinateSystem);

        public sealed class Capture : IXRDataCapture
        {
            private BodyTrackingCoordinateSystem _coordinates;
            private readonly Transform[] _joints;
            public XRBodyFrame Frame { get; }
            internal Capture(BodyTrackingCoordinateSystem coordinates)
            {
                if (coordinates == null || coordinates.skeletonFactory?.generatedJoints == null || coordinates.skeletonFactory.generatedJoints.Length == 0)
                    throw new InvalidOperationException("Assign an initialized, nonempty body skeleton.");
                _coordinates = coordinates;
                _joints = (Transform[])coordinates.skeletonFactory.generatedJoints.Clone();
                Frame = new XRBodyFrame(coordinates.skeletonFactory.generatedJoints.Length);
            }

            public void Refresh()
            {
                Frame.IsValid = false;
                if (_coordinates == null || _coordinates.skeletonFactory?.generatedJoints == null || _coordinates.skeletonFactory.generatedJoints.Length != Frame.Poses.Length)
                    throw new InvalidOperationException("The captured body skeleton was removed or changed.");
                Frame.IsValid = _coordinates.IsTrackingDataValid;
                Array.Clear(Frame.Poses, 0, Frame.Poses.Length);
                for (int i = 0; i < Frame.Poses.Length; ++i)
                {
                    if (_coordinates.skeletonFactory.generatedJoints[i] != _joints[i])
                        throw new InvalidOperationException($"Body joint {i} changed during publishing.");
                    if (!Frame.IsValid) continue;
                    if (!_coordinates.GetJointWorldPose(i, out var position, out var rotation))
                        throw new InvalidOperationException($"Body joint {i} is missing from a valid skeleton.");
                    Frame.Poses[i] = new Pose(position, rotation);
                }
            }
            public void Dispose() => _coordinates = null;
        }
    }
}
