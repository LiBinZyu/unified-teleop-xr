using UnityEngine;
using UnityEngine.Jobs;

namespace Apex.BodyTracking
{
    /// <summary>
    /// Manages joint GameObjects used for skeleton visualization and data reads.
    /// CRITICAL EXECUTION ORDER: Unity's Animator resets bones to A-Pose in Update.
    /// VRIK solves IK and moves the bones in LateUpdate (order ~0-10).
    /// By running at 32001, we guarantee this factory reads the final VRIK-solved poses
    /// instead of the dirty A-Pose data from the start of the frame.
    /// </summary>
    [DefaultExecutionOrder(32001)]
    public class BodyTrackingSkeletonFactory : MonoBehaviour
    {
        [Tooltip("Optional prefab to spawn at each tracked joint position. If empty, empty GameObjects will be created.")]
        public GameObject jointPrefab;

        [Tooltip("Prefab used to render lines between connected bone pairs")]
        public GameObject skeletonLinePrefab;
        
        /// <summary>
        /// The data-source joints.
        /// • Pico path   : the instantiated prefab GameObjects (written by ApplyJointTransformsJob).
        /// • Effortless  : the VRIK bone Transforms (set via InitializeFromExistingTransforms).
        /// Always use this for GetJointWorldPose reads.
        /// </summary>
        public Transform[] generatedJoints { get; private set; }

        /// <summary>
        /// Always the instantiated prefab GameObjects, regardless of mode.
        /// In Effortless mode these are NOT the data source; they are purely visual markers
        /// and are updated every frame via SyncSpawnedJointsFromGeneratedJoints().
        /// </summary>
        public Transform[] spawnedJoints { get; private set; }

        public bool IsUsingExternalTransforms { get; private set; }

        private GameObject[] skeletonLines;
        
        private bool _isVisible = true;

        public void SetVisibility(bool visible)
        {
            if (_isVisible == visible) return;
            _isVisible = visible;

            if (spawnedJoints != null)
            {
                for (int i = 0; i < spawnedJoints.Length; i++)
                {
                    if (spawnedJoints[i] != null)
                    {
                        spawnedJoints[i].gameObject.SetActive(visible);
                    }
                }
            }

            if (skeletonLines != null)
            {
                for (int i = 0; i < skeletonLines.Length; i++)
                {
                    if (skeletonLines[i] != null)
                    {
                        skeletonLines[i].SetActive(visible);
                    }
                }
            }
        }
        
        private static readonly int[] parentIndices = new int[]
        {
            -1, // 0: Pelvis
            0,  // 1: L_Hip
            0,  // 2: R_Hip
            0,  // 3: Spine1
            1,  // 4: L_Knee
            2,  // 5: R_Knee
            -1,  // 6: Spine2 (Skip BoneLine_3_to_6)
            4,  // 7: L_Ankle
            5,  // 8: R_Ankle
            6,  // 9: Spine3
            7,  // 10: L_Foot
            8,  // 11: R_Foot
            9,  // 12: Neck
            9,  // 13: L_Collar
            9,  // 14: R_Collar
            12, // 15: Head
            13, // 16: L_Shoulder
            14, // 17: R_Shoulder
            16, // 18: L_Elbow
            17, // 19: R_Elbow
            18, // 20: L_Wrist
            19, // 21: R_Wrist
            -1, // 22: L_Hand (Skip extra hand line)
            -1  // 23: R_Hand (Skip extra hand line)
        };

        public void Initialize(int jointCount)
        {
            if (generatedJoints == null || generatedJoints.Length != jointCount)
            {
                generatedJoints = new Transform[jointCount];
                spawnedJoints   = new Transform[jointCount];
                for (int i = 0; i < jointCount; i++)
                {
                    GameObject jointObj;
                    if (jointPrefab != null)
                    {
                        jointObj = Instantiate(jointPrefab, this.transform);
                    }
                    else
                    {
                        jointObj = new GameObject();
                        jointObj.transform.SetParent(this.transform, false);
                    }

                    jointObj.name = $"Joint_{i}";
                    generatedJoints[i] = jointObj.transform;
                    spawnedJoints[i]   = jointObj.transform; // same reference — Pico mode
                    generatedJoints[i].localPosition = Vector3.zero;
                    generatedJoints[i].localRotation = Quaternion.identity;
                    jointObj.SetActive(_isVisible);
                }
            }

            if (skeletonLinePrefab != null && skeletonLines == null)
            {
                skeletonLines = new GameObject[jointCount];
                int createdCount = 0;
                for (int i = 1; i < jointCount; i++)
                {
                    if (i < parentIndices.Length && parentIndices[i] != -1)
                    {
                        GameObject lineObj = Instantiate(skeletonLinePrefab, this.transform);
                        lineObj.name = $"BoneLine_{parentIndices[i]}_to_{i}";
                        lineObj.SetActive(_isVisible);
                        skeletonLines[i] = lineObj;
                        createdCount++;
                    }
                }
                Debug.Log($"[Factory] Created {createdCount} skeleton lines. Parent scale: {this.transform.lossyScale}");
            }
        }

        /// <summary>
        /// For Effortless mode: redirect generatedJoints to VRIK bone Transforms.
        /// spawnedJoints (the visual prefab GameObjects) are kept intact and updated
        /// each frame via SyncSpawnedJointsFromGeneratedJoints().
        /// </summary>
        public void InitializeFromExistingTransforms(Transform[] transforms)
        {
            if (transforms == null || transforms.Length == 0) return;

            generatedJoints = transforms;       // data source = VRIK bones
            IsUsingExternalTransforms = true;   // spawnedJoints still = prefab GameObjects
        }

        /// <summary>
        /// Reverts back to using the internal spawned GameObjects as the data source.
        /// Call this when switching from a provider that uses external transforms (Effortless)
        /// back to one that uses internal transforms (Pico).
        /// </summary>
        public void RevertToInternalTransforms()
        {
            if (!IsUsingExternalTransforms) return;

            IsUsingExternalTransforms = false;
            generatedJoints = spawnedJoints;
        }

        /// <summary>
        /// Copies world position/rotation from generatedJoints (VRIK bones) to spawnedJoints
        /// (the visual prefab GameObjects) so they visually follow the skeleton.
        /// Only does work when IsUsingExternalTransforms == true (Effortless mode).
        /// Call this in LateUpdate AFTER the data source has been updated.
        /// </summary>
        public void SyncSpawnedJointsFromGeneratedJoints()
        {
            if (!IsUsingExternalTransforms || spawnedJoints == null || generatedJoints == null) return;

            int count = Mathf.Min(spawnedJoints.Length, generatedJoints.Length);
            for (int i = 0; i < count; i++)
            {
                Transform src = generatedJoints[i];
                Transform dst = spawnedJoints[i];
                if (src == null || dst == null) continue;
                dst.position = src.position;
                dst.rotation = src.rotation;
            }
        }

        void LateUpdate()
        {
            if (!_isVisible) return;

            if (IsUsingExternalTransforms)
            {
                SyncSpawnedJointsFromGeneratedJoints();
            }
            
            UpdateSkeletonLines();
        }

        private void UpdateSkeletonLines()
        {
            if (skeletonLines == null)
            {
                return;
            }
            if (spawnedJoints == null) return;
            
            for (int i = 1; i < skeletonLines.Length; i++)
            {
                if (skeletonLines[i] != null && i < spawnedJoints.Length && parentIndices[i] < spawnedJoints.Length)
                {
                    if (spawnedJoints[i] != null && spawnedJoints[parentIndices[i]] != null)
                    {
                        Vector3 startPos = spawnedJoints[parentIndices[i]].position;
                        Vector3 endPos = spawnedJoints[i].position;
                        
                        var lr = skeletonLines[i].GetComponent<LineRenderer>();
                        if (lr != null)
                        {
                            lr.SetPosition(0, startPos);
                            lr.SetPosition(1, endPos);
                        }
                        else
                        {
                            // Treat it as a 3D Mesh prefab (like a Cylinder)
                            Vector3 dir = endPos - startPos;
                            float dist = dir.magnitude;
                            
                            if (dist > 0.0001f)
                            {
                                Transform lineT = skeletonLines[i].transform;
                                // Position at the midpoint
                                lineT.position = startPos + dir / 2f;
                                
                                // Assume prefab is oriented along the Y axis (default Unity Cylinder)
                                lineT.up = dir.normalized;
                                
                                // To make the world-space Y scale exactly 'dist',
                                // we must counteract the parent's world scale.
                                Vector3 parentScale = lineT.parent != null ? lineT.parent.lossyScale : Vector3.one;
                                
                                Vector3 scale = lineT.localScale;
                                // Assume the prefab's unscaled length is 1 meter.
                                // If it's a default Unity Cylinder (2m), the user must have already baked the scale 
                                // or we will just stretch it by dist. We use dist / parentScale.y.
                                scale.y = dist / parentScale.y;
                                lineT.localScale = scale;
                                
                                // Make sure it's enabled and active
                                if (!skeletonLines[i].activeSelf) skeletonLines[i].SetActive(true);
                            }
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
        }

        void OnDestroy()
        {
            Dispose();
        }
    }
}
