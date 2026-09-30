using Unity.Collections;
using UnityEngine;
using System;

namespace Apex.BodyTracking
{
    public interface IBodyDataProvider
    {
        bool IsTrackingDataValid { get; }
        
        /// <summary>
        /// Indicates whether joint velocities (linear and angular) are enabled and currently valid.
        /// </summary>
        bool HasJointVelocities { get; }

        /// <summary>
        /// Indicates whether joint accelerations (linear and angular) are enabled and currently valid.
        /// </summary>
        bool HasJointAccelerations { get; }

        void StartProvider();
        void StopProvider();
        void TriggerCalibration();

        /// <summary>
        /// Called every frame by the Coordinate System. The provider should apply its tracking data 
        /// to the given factory (either by updating generatedJoints or initializing external ones).
        /// </summary>
        void UpdateSkeleton(BodyTrackingSkeletonFactory factory, Vector3 originPos, Quaternion invOriginRot, bool applyOriginOffset);

        /// <summary>
        /// Retrieves the linear velocity and angular velocity for a specific joint index (0..23).
        /// Returns true if tracking is valid, velocities are enabled, and index is valid.
        /// </summary>
        bool TryGetJointVelocity(int jointIndex, out Vector3 linearVelocity, out Vector3 angularVelocity);

        /// <summary>
        /// Retrieves the linear acceleration and angular acceleration for a specific joint index (0..23).
        /// Returns true if tracking is valid, accelerations are enabled, and index is valid.
        /// </summary>
        bool TryGetJointAcceleration(int jointIndex, out Vector3 linearAcceleration, out Vector3 angularAcceleration);
    }
}
