#if PICO_OPENXR
using UnityEngine;
using Unity.Collections;
using System;
using Unity.XR.OpenXR.Features.PICOSupport;
using BodyTrackerRole = Unity.XR.OpenXR.Features.PICOSupport.BodyTrackerRole;
using BodyActionList = Unity.XR.OpenXR.Features.PICOSupport.BodyActionList;
#if UNITY_EDITOR
using Unity.XR.PICO.LivePreview;
#endif

namespace Apex.BodyTracking
{
    public class PicoBodyDataProvider : MonoBehaviour, IBodyDataProvider
    {
        private const string TAG = "[PicoBodyDataProvider] ";

        public bool applyPicoRotationalOffsets = true;

        [Tooltip("Manual Euler offsets applied to each of the 24 joints if applyPicoRotationalOffsets is true.")]
        public Vector3[] jointRotationOffsets = new Vector3[24];

        public bool enableJointVelocities = true;
        public bool enableJointAccelerations = true;
        public bool enableJointActions = true;

        [Tooltip("If true, swaps Left (index 4) and Right (index 5) knee action data to match physical leg assignment.")]
        public bool swapLeftRightKneeAction = true;

        public bool IsTrackingDataValid { get; private set; }
        public bool HasJointVelocities => _trackingStarted && IsTrackingDataValid && enableJointVelocities;
        public bool HasJointAccelerations => _trackingStarted && IsTrackingDataValid && enableJointAccelerations;
        public bool HasJointActions => _trackingStarted && IsTrackingDataValid && enableJointActions;

        /// <summary>
        /// 左膝 (Left Knee) 动作状态位 (已按真实左右腿对齐)
        /// </summary>
        public BodyActionList LeftKneeAction { get; private set; }

        /// <summary>
        /// 右膝 (Right Knee) 动作状态位 (已按真实左右腿对齐)
        /// </summary>
        public BodyActionList RightKneeAction { get; private set; }

        public bool HasLeftActionFlag(BodyActionList flag) => (LeftKneeAction & flag) != 0;
        public bool HasRightActionFlag(BodyActionList flag) => (RightKneeAction & flag) != 0;

        // --- 便捷地面与静态姿态判断 ---
        public bool IsLeftGrounded => HasLeftActionFlag(BodyActionList.PxrTouchGround);
        public bool IsRightGrounded => HasRightActionFlag(BodyActionList.PxrTouchGround);
        public bool IsBothGrounded => IsLeftGrounded && IsRightGrounded;

        public bool IsLeftStatic => HasLeftActionFlag(BodyActionList.PxrKeepStatic);
        public bool IsRightStatic => HasRightActionFlag(BodyActionList.PxrKeepStatic);
        public bool IsBothStatic => IsLeftStatic && IsRightStatic;

        public bool IsStandingStill => IsBothGrounded && IsBothStatic;

        // 语义别名
        public bool IsLeftFootGrounded => IsLeftGrounded;
        public bool IsRightFootGrounded => IsRightGrounded;
        public bool IsBothFeetGrounded => IsBothGrounded;
        public bool IsLeftFootStatic => IsLeftStatic;
        public bool IsRightFootStatic => IsRightStatic;
        public bool IsBothFeetStatic => IsBothStatic;

        private bool _trackingStarted = false;

        private readonly Vector3[] _jointLinearVelocities = new Vector3[24];
        private readonly Vector3[] _jointAngularVelocities = new Vector3[24];
        private readonly Vector3[] _jointLinearAccelerations = new Vector3[24];
        private readonly Vector3[] _jointAngularAccelerations = new Vector3[24];

        public bool TryGetJointVelocity(int jointIndex, out Vector3 linearVelocity, out Vector3 angularVelocity)
        {
            if (HasJointVelocities && jointIndex >= 0 && jointIndex < 24)
            {
                linearVelocity = _jointLinearVelocities[jointIndex];
                angularVelocity = _jointAngularVelocities[jointIndex];
                return true;
            }
            linearVelocity = Vector3.zero;
            angularVelocity = Vector3.zero;
            return false;
        }

        public bool TryGetJointAcceleration(int jointIndex, out Vector3 linearAcceleration, out Vector3 angularAcceleration)
        {
            if (HasJointAccelerations && jointIndex >= 0 && jointIndex < 24)
            {
                linearAcceleration = _jointLinearAccelerations[jointIndex];
                angularAcceleration = _jointAngularAccelerations[jointIndex];
                return true;
            }
            linearAcceleration = Vector3.zero;
            angularAcceleration = Vector3.zero;
            return false;
        }

        public void StartProvider()
        {
            if (_trackingStarted) return;
#if UNITY_EDITOR
            InitializeLivePreviewTracking();
#else
            InitializeOpenXRTracking();
#endif
        }

        public void StopProvider()
        {
            if (!_trackingStarted) return;
#if !UNITY_EDITOR
            BodyTrackingFeature.StopBodyTracking();
#endif
            _trackingStarted = false;
            IsTrackingDataValid = false;
            ClearVelocitiesAndAccelerations();
        }

        public void TriggerCalibration()
        {
#if !UNITY_EDITOR
            int result = BodyTrackingFeature.StartMotionTrackerCalibApp();
            if (result == 0)
                Logger.LogApp(TAG + "StartMotionTrackerCalibApp succeeded.");
            else
                Logger.LogApp($"{TAG}StartMotionTrackerCalibApp returned {result}. isEnable=" + BodyTrackingFeature.isEnable, LogType.Warning);
#else
            Logger.LogApp(TAG + "OpenBodyTrackingApp() is a no-op in the Editor or non-PICO branch.");
#endif
        }

        public void UpdateSkeleton(BodyTrackingSkeletonFactory factory, Vector3 originPos, Quaternion invOriginRot, bool applyOriginOffset)
        {
            if (!_trackingStarted)
            {
                IsTrackingDataValid = false;
                ClearVelocitiesAndAccelerations();
                return;
            }

            if (applyPicoRotationalOffsets && (jointRotationOffsets == null || jointRotationOffsets.Length < 24))
            {
                Reset(); // ensure array size
            }

#if UNITY_EDITOR
            UpdateLivePreviewTracking(factory, originPos, invOriginRot, applyOriginOffset);
#else
            UpdateOpenXRTracking(factory, originPos, invOriginRot, applyOriginOffset);
#endif
        }

#if UNITY_EDITOR
        private void InitializeLivePreviewTracking()
        {
            var activeLoader = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager?.activeLoader;
            if (activeLoader == null || !activeLoader.GetType().FullName.Contains("PXR_PTLoader"))
            {
                Logger.LogApp(TAG + "PXR_PTLoader is not the active XR loader in Editor. Skipping LivePreview body tracking to prevent native crash.", LogType.Warning);
                _trackingStarted = false;
                return;
            }

            try
            {
                Logger.LogApp(TAG + "Using Unity.XR.PICO.LivePreview mode for Body Tracking.");
                Unity.XR.PICO.LivePreview.BodyTrackingBoneLength ptLength = new Unity.XR.PICO.LivePreview.BodyTrackingBoneLength();
                
                var method = typeof(Unity.XR.PICO.LivePreview.PXR_PTApi).GetMethod("LP_StartBodyTracking", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (method != null)
                {
                    method.Invoke(null, new object[] { 2, ptLength, 1 });
                }
                else
                {
                    Unity.XR.PICO.LivePreview.PXR_PTApi.UPxr_StartBodyTracking();
                }
                
                _trackingStarted = true;
            }
            catch (Exception ex)
            {
                Logger.LogApp(TAG + "Failed to initialize LivePreview Body Tracking (Preview streaming not connected): " + ex.Message, LogType.Warning);
                _trackingStarted = false;
            }
        }

        private void UpdateLivePreviewTracking(BodyTrackingSkeletonFactory factory, Vector3 originPos, Quaternion invOriginRot, bool applyOriginOffset)
        {
            if (!_trackingStarted) return;

            var activeLoader = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager?.activeLoader;
            if (activeLoader == null || !activeLoader.GetType().FullName.Contains("PXR_PTLoader"))
            {
                IsTrackingDataValid = false;
                return;
            }

            Unity.XR.PICO.LivePreview.BodyTrackerResult ptResult = new Unity.XR.PICO.LivePreview.BodyTrackerResult();
            ptResult.trackingdata = new Unity.XR.PICO.LivePreview.BodyTrackerTransform[24];
            
            try
            {
                Unity.XR.PICO.LivePreview.PXR_PTApi.UPxr_GetBodyTrackingPose(ref ptResult);
            }
            catch
            {
                IsTrackingDataValid = false;
                return;
            }

            if (ptResult.trackingdata != null && ptResult.trackingdata.Length >= 24)
            {
                IsTrackingDataValid = true;
                for (int i = 0; i < 24; i++)
                {
                    var data = ptResult.trackingdata[i];
                    var pose = data.localpose;
                    Vector3 pos = new Vector3((float)pose.PosX, (float)pose.PosY, -(float)pose.PosZ);
                    Quaternion qu = new Quaternion((float)pose.RotQx, (float)pose.RotQy, -(float)pose.RotQz, -(float)pose.RotQw);

                    ApplyJointData(i, pos, qu, factory, originPos, invOriginRot, applyOriginOffset);

                    if (enableJointVelocities || enableJointAccelerations)
                    {
                        Vector3 linearVel = Vector3.zero;
                        Vector3 angularVel = Vector3.zero;
                        Vector3 linearAccel = Vector3.zero;
                        Vector3 angularAccel = Vector3.zero;

                        if (data.velo != null && data.velo.Length >= 3)
                            linearVel = new Vector3((float)data.velo[0], (float)data.velo[1], -(float)data.velo[2]);
                        if (data.wvelo != null && data.wvelo.Length >= 3)
                            angularVel = new Vector3((float)data.wvelo[0], (float)data.wvelo[1], -(float)data.wvelo[2]);
                        if (data.acce != null && data.acce.Length >= 3)
                            linearAccel = new Vector3((float)data.acce[0], (float)data.acce[1], -(float)data.acce[2]);
                        if (data.wacce != null && data.wacce.Length >= 3)
                            angularAccel = new Vector3((float)data.wacce[0], (float)data.wacce[1], -(float)data.wacce[2]);

                        StoreJointDynamics(i, linearVel, angularVel, linearAccel, angularAccel, invOriginRot, applyOriginOffset);
                    }
                }

                if (enableJointActions)
                {
                    var rawKnee4 = (BodyActionList)ptResult.trackingdata[(int)BodyTrackerRole.LEFT_KNEE].Action;
                    var rawKnee5 = (BodyActionList)ptResult.trackingdata[(int)BodyTrackerRole.RIGHT_KNEE].Action;

                    if (swapLeftRightKneeAction)
                    {
                        LeftKneeAction = rawKnee5;
                        RightKneeAction = rawKnee4;
                    }
                    else
                    {
                        LeftKneeAction = rawKnee4;
                        RightKneeAction = rawKnee5;
                    }
                }
            }
            else
            {
                IsTrackingDataValid = false;
                ClearVelocitiesAndAccelerations();
            }
        }
#else
        private void InitializeOpenXRTracking()
        {
            if (BodyTrackingFeature.isEnable)
            {
                Unity.XR.OpenXR.Features.PICOSupport.BodyTrackingBoneLength boneLength = new Unity.XR.OpenXR.Features.PICOSupport.BodyTrackingBoneLength();
                int res = BodyTrackingFeature.StartBodyTracking(BodyJointSet.BODY_JOINT_SET_BODY_FULL_START, boneLength);
                if (res == 0)
                {
                    Logger.LogApp(TAG + "Body tracking started successfully.");
                    _trackingStarted = true;
                }
                else
                {
                    Logger.LogApp(TAG + "Failed to start body tracking.", LogType.Error);
                }
            }
            else
            {
                Logger.LogApp(TAG + "Pico Body Tracking OpenXR feature is not enabled!", LogType.Error);
            }
        }

        private void UpdateOpenXRTracking(BodyTrackingSkeletonFactory factory, Vector3 originPos, Quaternion invOriginRot, bool applyOriginOffset)
        {
            bool isTracking = false;
            BodyTrackingStatus state = new BodyTrackingStatus();
            int stateResult = BodyTrackingFeature.GetBodyTrackingState(ref isTracking, ref state);

            if (stateResult == 0 && (!isTracking || state.stateCode == BodyTrackingStatusCode.BT_INVALID))
            {
                IsTrackingDataValid = false;
                ClearVelocitiesAndAccelerations();
                return;
            }

            BodyTrackingGetDataInfo getInfo = new BodyTrackingGetDataInfo { displayTime = 0 };
            BodyTrackingData data = new BodyTrackingData();

            if (BodyTrackingFeature.GetBodyTrackingData(ref getInfo, ref data) == 0)
            {
                IsTrackingDataValid = true;
                for (int i = 0; i < 24; i++)
                {
                    var roleData = data.roleDatas[i];
                    var pose = roleData.localPose;
                    Vector3 pos = new Vector3((float)pose.PosX, (float)pose.PosY, (float)pose.PosZ);
                    Quaternion qu = new Quaternion((float)pose.RotQx, (float)pose.RotQy, (float)pose.RotQz, (float)pose.RotQw);

                    ApplyJointData(i, pos, qu, factory, originPos, invOriginRot, applyOriginOffset);

                    if (enableJointVelocities || enableJointAccelerations)
                    {
                        Vector3 linearVel;
                        Vector3 angularVel;
                        Vector3 linearAccel;
                        Vector3 angularAccel;

                        unsafe
                        {
                            linearVel = new Vector3((float)roleData.velo[0], (float)roleData.velo[1], (float)roleData.velo[2]);
                            angularVel = new Vector3((float)roleData.wvelo[0], (float)roleData.wvelo[1], (float)roleData.wvelo[2]);
                            linearAccel = new Vector3((float)roleData.acce[0], (float)roleData.acce[1], (float)roleData.acce[2]);
                            angularAccel = new Vector3((float)roleData.wacce[0], (float)roleData.wacce[1], (float)roleData.wacce[2]);
                        }

                        StoreJointDynamics(i, linearVel, angularVel, linearAccel, angularAccel, invOriginRot, applyOriginOffset);
                    }
                }

                if (enableJointActions)
                {
                    var rawKnee4 = (BodyActionList)data.roleDatas[(int)BodyTrackerRole.LEFT_KNEE].bodyAction;
                    var rawKnee5 = (BodyActionList)data.roleDatas[(int)BodyTrackerRole.RIGHT_KNEE].bodyAction;

                    if (swapLeftRightKneeAction)
                    {
                        LeftKneeAction = rawKnee5;
                        RightKneeAction = rawKnee4;
                    }
                    else
                    {
                        LeftKneeAction = rawKnee4;
                        RightKneeAction = rawKnee5;
                    }
                }
            }
            else
            {
                IsTrackingDataValid = false;
                ClearVelocitiesAndAccelerations();
            }
        }
#endif

        private void StoreJointDynamics(int index, Vector3 linearVel, Vector3 angularVel, Vector3 linearAccel, Vector3 angularAccel, Quaternion invOriginRot, bool applyOriginOffset)
        {
            if (applyOriginOffset)
            {
                linearVel = invOriginRot * linearVel;
                angularVel = invOriginRot * angularVel;
                linearAccel = invOriginRot * linearAccel;
                angularAccel = invOriginRot * angularAccel;
            }

            _jointLinearVelocities[index] = FixNaN(linearVel);
            _jointAngularVelocities[index] = FixNaN(angularVel);
            _jointLinearAccelerations[index] = FixNaN(linearAccel);
            _jointAngularAccelerations[index] = FixNaN(angularAccel);
        }

        private Vector3 FixNaN(Vector3 v)
        {
            if (float.IsNaN(v.x) || float.IsInfinity(v.x)) v.x = 0f;
            if (float.IsNaN(v.y) || float.IsInfinity(v.y)) v.y = 0f;
            if (float.IsNaN(v.z) || float.IsInfinity(v.z)) v.z = 0f;
            return v;
        }

        private void ClearVelocitiesAndAccelerations()
        {
            Array.Clear(_jointLinearVelocities, 0, 24);
            Array.Clear(_jointAngularVelocities, 0, 24);
            Array.Clear(_jointLinearAccelerations, 0, 24);
            Array.Clear(_jointAngularAccelerations, 0, 24);
            LeftKneeAction = default;
            RightKneeAction = default;
        }

        private void ApplyJointData(int i, Vector3 pos, Quaternion qu, BodyTrackingSkeletonFactory factory, Vector3 originPos, Quaternion invOriginRot, bool applyOriginOffset)
        {
            if (float.IsNaN(pos.x) || float.IsInfinity(pos.x)) return;
            if (float.IsNaN(qu.x) || float.IsInfinity(qu.x)) return;

            if (applyPicoRotationalOffsets)
            {
                Quaternion offset = Quaternion.Euler(jointRotationOffsets[i]);
                qu = qu * offset;
            }

            if (applyOriginOffset)
            {
                pos = invOriginRot * (pos - originPos);
                qu = invOriginRot * qu;
            }

            if (factory != null && factory.generatedJoints != null && i < factory.generatedJoints.Length)
            {
                Transform joint = factory.generatedJoints[i];
                if (joint != null)
                {
                    joint.position = pos;
                    joint.rotation = qu;
                }
            }
        }

        private void Reset()
        {
            if (jointRotationOffsets == null || jointRotationOffsets.Length != 24)
            {
                jointRotationOffsets = new Vector3[24];
            }
        }
    }
}
#endif