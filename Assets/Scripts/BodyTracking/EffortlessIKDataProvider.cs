using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections;
using RootMotion.FinalIK;
using Unity.Collections;

namespace Apex.BodyTracking
{
    /// <summary>
    /// VRIK-based auto-proportioning IK solver that acts as a BodyDataProvider.
    /// Replaces PicoBodyDataProvider when in Effortless_Fullbody mode.
    /// </summary>
    [DefaultExecutionOrder(32000)] // Must run after VRIK solver (order 0) and all other LateUpdates
    public class EffortlessIKDataProvider : MonoBehaviour, IBodyDataProvider
    {
        [Tooltip("Reference to the VRIK component on the character.")]
        public VRIK ik;
        [Tooltip("Reference to the root of the generated SMPL skeleton (SMPL_Skeleton).")]
        public Transform smplRoot;
        
        public Transform controller_l_tracker;
        public Transform controller_r_tracker;
        public Transform head_tracker;

        [Tooltip("Settings used during calibration (offsets, weights, etc.)")]
        public VRIKCalibrator.Settings calibrationSettings = new VRIKCalibrator.Settings();

        [Tooltip("How many seconds to measure the arm reach after clicking Calibrate.")]
        public float calibrationDuration = 5f;
        
        [Header("Mode Configuration")]
        [Tooltip("If true, simulates wearing the tracker on the neck by adding an offset to the head_tracker.")]
        public bool useNeckWornMode = false;
        
        [Tooltip("The local offset applied to the head_tracker when in Neck Worn Mode.")]
        public Vector3 neckWornOffset = new Vector3(0f, 0.1f, -0.05f);
        
        [Header("Calibration Events")]
        public UnityEvent onCalibrationStarted = new UnityEvent();
        public UnityEvent onCalibrationSucceeded = new UnityEvent();
        public UnityEvent onCalibrationFailed = new UnityEvent();
        public UnityEvent onCalibrationSessionEnded = new UnityEvent();
        
        // IBodyDataProvider Implementation
        public bool IsTrackingDataValid { get; private set; }

        /// <summary>
        /// VRIK solver computes bone transforms only; it does not provide native joint velocity interface.
        /// </summary>
        public bool HasJointVelocities => false;

        /// <summary>
        /// VRIK solver computes bone transforms only; it does not provide native joint acceleration interface.
        /// </summary>
        public bool HasJointAccelerations => false;
        
        private Transform virtualNeckTracker;
        private bool isCalibrating = false;
        
        // Cache the 24 SMPL joints for quick access in LateUpdate
        private Transform[] _smplJoints;

        public bool TryGetJointVelocity(int jointIndex, out Vector3 linearVelocity, out Vector3 angularVelocity)
        {
            linearVelocity = Vector3.zero;
            angularVelocity = Vector3.zero;
            return false;
        }

        public bool TryGetJointAcceleration(int jointIndex, out Vector3 linearAcceleration, out Vector3 angularAcceleration)
        {
            linearAcceleration = Vector3.zero;
            angularAcceleration = Vector3.zero;
            return false;
        }

        private void Start()
        {
            // Ensure VRIK solver is disabled on start if tracking provider has not been started yet
            if (!IsTrackingDataValid && ik != null)
            {
                ik.enabled = false;
            }
        }

        private void OnDisable()
        {
            StopProvider();
        }

        public void StartProvider()
        {
            UpdateBoneMappingFromVRIK();
            
            // Auto-assign VRIK references if not set by the user in the Inspector
            if (ik != null && ik.references.root == null)
            {
                AssignSMPLReferences();
                UpdateBoneMappingFromVRIK();
            }

            if (ik != null)
            {
                ik.enabled = true;
            }

            IsTrackingDataValid = true;
        }

        public void StopProvider()
        {
            IsTrackingDataValid = false;

            if (isCalibrating)
            {
                StopAllCoroutines();
                isCalibrating = false;
                onCalibrationSessionEnded?.Invoke();
            }

            if (ik != null)
            {
                ik.enabled = false;
            }
        }

        public void TriggerCalibration()
        {
            if (!isCalibrating)
                StartCoroutine(CalibrationRoutine());
        }

        public void UpdateSkeleton(BodyTrackingSkeletonFactory factory, Vector3 originPos, Quaternion invOriginRot, bool applyOriginOffset)
        {
            if (!IsTrackingDataValid) return;

            if (factory != null && !factory.IsUsingExternalTransforms)
            {
                if (_smplJoints != null && _smplJoints.Length == 24)
                {
                    factory.InitializeFromExistingTransforms(_smplJoints);
                }
            }
        }

        private void UpdateBoneMappingFromVRIK()
        {
            if (ik == null || ik.references.root == null) return;
            
            _smplJoints = new Transform[24];
            
            // Safely map VRIK references to Pico's 24-joint format
            _smplJoints[0] = ik.references.pelvis;
            _smplJoints[1] = ik.references.leftThigh;
            _smplJoints[2] = ik.references.rightThigh;
            _smplJoints[3] = ik.references.spine;
            _smplJoints[4] = ik.references.leftCalf;
            _smplJoints[5] = ik.references.rightCalf;
            _smplJoints[6] = ik.references.spine; 
            _smplJoints[7] = ik.references.leftFoot;
            _smplJoints[8] = ik.references.rightFoot;
            _smplJoints[9] = ik.references.chest;
            _smplJoints[10] = ik.references.leftToes != null ? ik.references.leftToes : ik.references.leftFoot;
            _smplJoints[11] = ik.references.rightToes != null ? ik.references.rightToes : ik.references.rightFoot;
            _smplJoints[12] = ik.references.neck;
            _smplJoints[13] = ik.references.leftShoulder;
            _smplJoints[14] = ik.references.rightShoulder;
            _smplJoints[15] = ik.references.head;
            _smplJoints[16] = ik.references.leftUpperArm;
            _smplJoints[17] = ik.references.rightUpperArm;
            _smplJoints[18] = ik.references.leftForearm;
            _smplJoints[19] = ik.references.rightForearm;
            _smplJoints[20] = ik.references.leftHand;
            _smplJoints[21] = ik.references.rightHand;
            _smplJoints[22] = ik.references.leftHand; // Fallbacks for Pico's extra hand joints
            _smplJoints[23] = ik.references.rightHand;
        }


        [ContextMenu("Assign SMPL References to VRIK")]
        public void AssignSMPLReferences()
        {
            if (ik == null || smplRoot == null) return;

            VRIK.References refs = new VRIK.References();
            refs.root = smplRoot;
            
            Transform[] allBones = smplRoot.GetComponentsInChildren<Transform>(true);
            
            foreach (Transform t in allBones)
            {
                switch (t.name)
                {
                    case "Pelvis": refs.pelvis = t; break;
                    case "Spine1": refs.spine = t; break;
                    case "Spine3": refs.chest = t; break;
                    case "Neck": refs.neck = t; break;
                    case "Head": refs.head = t; break;
                    
                    case "L_Collar": refs.leftShoulder = t; break;
                    case "L_Shoulder": refs.leftUpperArm = t; break;
                    case "L_Elbow": refs.leftForearm = t; break;
                    case "L_Wrist": refs.leftHand = t; break;
                    
                    case "R_Collar": refs.rightShoulder = t; break;
                    case "R_Shoulder": refs.rightUpperArm = t; break;
                    case "R_Elbow": refs.rightForearm = t; break;
                    case "R_Wrist": refs.rightHand = t; break;
                    
                    case "L_Hip": refs.leftThigh = t; break;
                    case "L_Knee": refs.leftCalf = t; break;
                    case "L_Ankle": refs.leftFoot = t; break;
                    case "L_Foot": refs.leftToes = t; break;
                    
                    case "R_Hip": refs.rightThigh = t; break;
                    case "R_Knee": refs.rightCalf = t; break;
                    case "R_Ankle": refs.rightFoot = t; break;
                    case "R_Foot": refs.rightToes = t; break;
                }
            }

            ik.references = refs;
            Debug.Log("[EffortlessIK] SMPL skeleton bones mapped to VRIK references successfully!");
        }
        
        [ContextMenu("Toggle Neck Worn Mode")]
        public void ToggleTrackerMode()
        {
            useNeckWornMode = !useNeckWornMode;
            Debug.Log($"[EffortlessIK] Switched Mode. Use Neck Worn Mode: {useNeckWornMode}");
            if (!isCalibrating && IsTrackingDataValid) InstantCalibrate();
        }
        
        public void SetNeckWornMode(bool useNeckWorn)
        {
            useNeckWornMode = useNeckWorn;
            if (!isCalibrating && IsTrackingDataValid) InstantCalibrate();
        }
        

        [ContextMenu("Start Calibration")]
        public void Calibrate()
        {
            if (isCalibrating) return;
            StartCoroutine(CalibrationRoutine());
        }

        private IEnumerator CalibrationRoutine()
        {
            isCalibrating = true;
            IsTrackingDataValid = false;
            onCalibrationStarted?.Invoke();
            
            Debug.Log($"[EffortlessIK] Calibration started. Please stretch arms fully (T-Pose) for {calibrationDuration} seconds...");
            
            float timer = 0f;
            float maxLeftReach = 0f;
            float maxRightReach = 0f;
            
            Transform activeHeadTracker = GetActiveHeadTracker();
            
            // Measure the maximum reach over N seconds
            while (timer < calibrationDuration)
            {
                timer += Time.deltaTime;
                
                if (activeHeadTracker != null && controller_l_tracker != null && controller_r_tracker != null)
                {
                    float leftDist = Vector3.Distance(activeHeadTracker.position, controller_l_tracker.position);
                    float rightDist = Vector3.Distance(activeHeadTracker.position, controller_r_tracker.position);
                    
                    if (leftDist > maxLeftReach) maxLeftReach = leftDist;
                    if (rightDist > maxRightReach) maxRightReach = rightDist;
                }
                
                yield return null;
            }
            
            // Validate Head Height Drift Check
            if (activeHeadTracker == null || activeHeadTracker.position.y > 2.0f)
            {
                float invalidHeight = activeHeadTracker != null ? activeHeadTracker.position.y : 0f;
                Debug.LogError($"[EffortlessIK] Calibration Failed. Head tracker height is invalid ({invalidHeight}m > 2.0m). Potential XR Drift.");
                onCalibrationFailed?.Invoke();
                yield return new WaitForSeconds(2f);
                onCalibrationSessionEnded?.Invoke();
                isCalibrating = false;
                yield break;
            }
            
            Debug.Log($"[EffortlessIK] Measurement complete. Max Left Reach: {maxLeftReach:F2}m, Max Right Reach: {maxRightReach:F2}m");
            
            // 1. Initial Calibration to get the correct Root Scale based on Head height
            InstantCalibrate();
            
            // Disable VRIK before modifying bone lengths and re-initiating to prevent native crashes
            ik.enabled = false;
            
            // 2. Adjust local bone lengths to match the measured maximum reach
            AdjustArmLengths(maxLeftReach, maxRightReach);
            
            // 3. Re-initiate IK solver so it reads the new bone lengths
            ik.solver.Initiate(ik.transform);
            
            ik.enabled = true;
            
            // 4. Run Calibration again so IK targets are correctly placed on the newly sized arms
            InstantCalibrate();
            
            IsTrackingDataValid = true;
            

            onCalibrationSucceeded?.Invoke();
            Debug.Log("[EffortlessIK] Auto-Proportion Calibration Complete!");
            yield return new WaitForSeconds(2f);
            onCalibrationSessionEnded?.Invoke();
            isCalibrating = false;
        }

        private void AdjustArmLengths(float maxLeftReach, float maxRightReach)
        {
            float handOffsetWorld = 0.05f; 
            float scale = ik.references.root.localScale.y;
            if (scale <= 0.001f) scale = 1f;
            
            // Use the maximum reach of both arms to ensure symmetrical arm lengths
            float maxReach = Mathf.Max(maxLeftReach, maxRightReach);
            
            // Left Arm
            float leftShoulderDist = Vector3.Distance(ik.references.head.position, ik.references.leftUpperArm.position);
            float realLeftArmLength = maxReach - leftShoulderDist - handOffsetWorld;
            realLeftArmLength = Mathf.Clamp(realLeftArmLength, 0.4f * scale, 1.0f * scale); 
            
            float localLeftArmLength = realLeftArmLength / scale;
            Vector3 leftArmDir = new Vector3(-1f, -1f, 0f).normalized; 
            
            ik.references.leftForearm.localPosition = leftArmDir * (localLeftArmLength / 2f);
            ik.references.leftHand.localPosition = leftArmDir * (localLeftArmLength / 2f);
            
            // Right Arm
            float rightShoulderDist = Vector3.Distance(ik.references.head.position, ik.references.rightUpperArm.position);
            float realRightArmLength = maxReach - rightShoulderDist - handOffsetWorld;
            realRightArmLength = Mathf.Clamp(realRightArmLength, 0.4f * scale, 1.0f * scale);
            
            float localRightArmLength = realRightArmLength / scale;
            Vector3 rightArmDir = new Vector3(1f, -1f, 0f).normalized;
            
            ik.references.rightForearm.localPosition = rightArmDir * (localRightArmLength / 2f);
            ik.references.rightHand.localPosition = rightArmDir * (localRightArmLength / 2f);
        }

        private Transform GetActiveHeadTracker()
        {
            Transform activeHeadTracker = head_tracker;
            
            if (useNeckWornMode)
            {
                if (virtualNeckTracker == null)
                {
                    GameObject go = new GameObject("Virtual_Neck_Tracker");
                    virtualNeckTracker = go.transform;
                    virtualNeckTracker.parent = head_tracker;
                }
                virtualNeckTracker.localPosition = neckWornOffset;
                virtualNeckTracker.localRotation = Quaternion.identity;
                
                activeHeadTracker = virtualNeckTracker;
            }
            
            return activeHeadTracker;
        }

        private void InstantCalibrate()
        {
            if (ik == null || head_tracker == null) return;
            
            Transform activeHeadTracker = GetActiveHeadTracker();
            
            VRIKCalibrator.CalibrationData data = VRIKCalibrator.Calibrate(
                ik, 
                calibrationSettings, 
                activeHeadTracker, 
                null, 
                controller_l_tracker, 
                controller_r_tracker, 
                null, 
                null
            );
            
            if (data == null)
            {
                Debug.LogError("[EffortlessIK] VRIK Instant Calibration failed.");
            }
        }
    }
}
