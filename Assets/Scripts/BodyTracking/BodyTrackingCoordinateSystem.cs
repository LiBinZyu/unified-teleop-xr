using UnityEngine;
using System.Collections.Generic;
namespace Apex.BodyTracking
{
    public enum ReceiverOriginMode
    {
        XROrigin,
        XRHead,
        FeetMidpoint
    }

    [DefaultExecutionOrder(-10)]
    public class BodyTrackingCoordinateSystem : MonoBehaviour
    {
        [Tooltip("List of MonoBehaviours that implement IBodyDataProvider.")]
        public List<MonoBehaviour> providers = new List<MonoBehaviour>();
        
        [Tooltip("Factory responsible for spawning and managing joint Transforms.")]
        public BodyTrackingSkeletonFactory skeletonFactory;

        public ReceiverOriginMode originMode = ReceiverOriginMode.XROrigin;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent OnTrackingDataValid;
        public UnityEngine.Events.UnityEvent OnTrackingDataInvalid;

        private IBodyDataProvider _activeProvider;
        private bool _wasTrackingDataValid = false;

        public Vector3 CurrentOriginPos { get; private set; } = Vector3.zero;
        public Quaternion CurrentOriginRot { get; private set; } = Quaternion.identity;

        public bool IsTrackingDataValid => _activeProvider != null && _activeProvider.IsTrackingDataValid;
        public bool HasJointVelocities => _activeProvider != null && _activeProvider.HasJointVelocities;
        public bool HasJointAccelerations => _activeProvider != null && _activeProvider.HasJointAccelerations;

        public bool GetJointWorldPose(int index, out Vector3 pos, out Quaternion rot)
        {
            if (skeletonFactory != null && skeletonFactory.generatedJoints != null && index >= 0 && index < skeletonFactory.generatedJoints.Length)
            {
                var t = skeletonFactory.generatedJoints[index];
                if (t == null)
                {
                    pos = Vector3.zero;
                    rot = Quaternion.identity;
                    return false;
                }
                pos = t.position;
                rot = t.rotation;
                return true;
            }
            pos = Vector3.zero;
            rot = Quaternion.identity;
            return false;
        }

        public bool GetJointVelocities(int index, out Vector3 linearVelocity, out Vector3 angularVelocity)
        {
            if (_activeProvider != null && IsTrackingDataValid)
            {
                return _activeProvider.TryGetJointVelocity(index, out linearVelocity, out angularVelocity);
            }
            linearVelocity = Vector3.zero;
            angularVelocity = Vector3.zero;
            return false;
        }

        public bool GetJointAccelerations(int index, out Vector3 linearAcceleration, out Vector3 angularAcceleration)
        {
            if (_activeProvider != null && IsTrackingDataValid)
            {
                return _activeProvider.TryGetJointAcceleration(index, out linearAcceleration, out angularAcceleration);
            }
            linearAcceleration = Vector3.zero;
            angularAcceleration = Vector3.zero;
            return false;
        }
        
        [ContextMenu("Trigger Current Provider Calibration")]
        public void TriggerCurrentProviderCalibration()
        {
            if (_activeProvider != null)
            {
                _activeProvider.TriggerCalibration();
            }
        }

        void Start()
        {
            int jointCount = 24;
            if (skeletonFactory != null)
            {
                skeletonFactory.Initialize(jointCount);
            }
        }

        public void EnableProvider(int index)
        {
            if (index < 0 || index >= providers.Count)
            {
                Logger.LogApp($"[BodyTrackingCoordinateSystem] Invalid provider index: {index}", LogType.Error);
                return;
            }

            IBodyDataProvider newProvider = providers[index] as IBodyDataProvider;
            if (newProvider == null)
            {
                Logger.LogApp($"[BodyTrackingCoordinateSystem] Provider at index {index} does not implement IBodyDataProvider!", LogType.Error);
                return;
            }

            if (_activeProvider == newProvider) return;

            if (_activeProvider != null)
            {
                _activeProvider.StopProvider();
            }

            if (skeletonFactory != null)
            {
                skeletonFactory.RevertToInternalTransforms();
            }

            _activeProvider = newProvider;
            _activeProvider.StartProvider();
        }

        public void DisableProvider()
        {
            if (_activeProvider != null)
            {
                _activeProvider.StopProvider();
            }
            
            if (skeletonFactory != null) 
            {
                skeletonFactory.RevertToInternalTransforms();
                skeletonFactory.SetVisibility(false);
            }
            _activeProvider = null;
        }

        void LateUpdate()
        {
            if (_activeProvider == null)
            {
                if (skeletonFactory != null) skeletonFactory.SetVisibility(false);
                
                if (_wasTrackingDataValid)
                {
                    _wasTrackingDataValid = false;
                    OnTrackingDataInvalid?.Invoke();
                }
                return;
            }

            Vector3 originPos = Vector3.zero;
            Quaternion originRot = Quaternion.identity;

            if (originMode == ReceiverOriginMode.XRHead)
            {
                // Note: using GetJointWorldPose here has a 1-frame delay due to it reading the finalized position.
                if (GetJointWorldPose(15, out Vector3 headPos, out Quaternion headRot))
                {
                    originPos = headPos;
                    originRot = headRot;
                }
            }
            else if (originMode == ReceiverOriginMode.FeetMidpoint)
            {
                if (GetJointWorldPose(10, out Vector3 leftFoot, out _) && GetJointWorldPose(11, out Vector3 rightFoot, out _))
                {
                    originPos = (leftFoot + rightFoot) * 0.5f;
                    originRot = Quaternion.identity;
                }
            }

            CurrentOriginPos = originPos;
            CurrentOriginRot = originRot;

            _activeProvider.UpdateSkeleton(skeletonFactory, originPos, Quaternion.Inverse(originRot), originMode != ReceiverOriginMode.XROrigin);

            bool isValidNow = _activeProvider.IsTrackingDataValid;
            
            if (isValidNow && !_wasTrackingDataValid)
            {
                _wasTrackingDataValid = true;
                OnTrackingDataValid?.Invoke();
            }
            else if (!isValidNow && _wasTrackingDataValid)
            {
                _wasTrackingDataValid = false;
                OnTrackingDataInvalid?.Invoke();
            }

            if (skeletonFactory != null)
            {
                skeletonFactory.SetVisibility(isValidNow);
            }
        }

        void OnDestroy()
        {
            if (_activeProvider != null)
            {
                _activeProvider.StopProvider();
            }
        }
    }
}
