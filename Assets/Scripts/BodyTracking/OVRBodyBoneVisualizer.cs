#if META_XR
using UnityEngine;
using System.Collections.Generic;

namespace Apex.BodyTracking
{
    /// <summary>
    /// Spawns node and skeleton prefabs to track and visualize OVRBody bone poses in real-time.
    /// Provides zero-GC frame updates and Start/Stop tracking methods for Button.onClick.
    /// </summary>
    public class OVRBodyBoneVisualizer : MonoBehaviour
    {
        [Header("Prefabs")]
        [Tooltip("Prefab instantiated at each bone node pose (position & rotation).")]
        [SerializeField] private GameObject _nodePrefab;

        [Tooltip("Prefab instantiated between parent and child bones (1m long along +Y axis by default, scaled along Y).")]
        [SerializeField] private GameObject _skeletonPrefab;

        [Header("Components")]
        [SerializeField] private OVRBody _ovrBody;
        [SerializeField] private OVRSkeleton _ovrSkeleton;

        [Header("Settings")]
        [Tooltip("If true, skeleton prefab center is placed at midpoint between parent and child. If false, placed at parent position.")]
        [SerializeField] private bool _pivotAtCenter = true;

        [SerializeField] private bool _autoStartOnEnable = true;

        private GameObject _visualRoot;
        private Transform[] _nodeTransforms;
        private SkeletonConnection[] _boneConnections;
        private bool _isTrackingActive;
        private bool _prefabsInitialized;
        private float _lastLogTime;

        private struct SkeletonConnection
        {
            public int ParentIndex;
            public int ChildIndex;
            public Transform Transform;
            public Vector3 BaseScale;
        }

        public bool IsTrackingActive => _isTrackingActive;

        private void Awake()
        {
            EnsureComponents();
        }

        private void OnEnable()
        {
            if (_autoStartOnEnable)
            {
                StartBodyTracking();
            }
        }

        private void OnDisable()
        {
            StopBodyTracking();
        }

        private void EnsureComponents()
        {
            if (_ovrBody == null && !TryGetComponent(out _ovrBody))
            {
                _ovrBody = FindAnyObjectByType<OVRBody>();
            }

            if (_ovrSkeleton == null && !TryGetComponent(out _ovrSkeleton))
            {
                _ovrSkeleton = FindAnyObjectByType<OVRSkeleton>();
            }
        }

        public void StartBodyTracking()
        {
            EnsureComponents();

            // Request Body Tracking permission if not granted
            if (!OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.BodyTracking))
            {
                Logger.LogApp("[OVRBodyBoneVisualizer] Requesting BodyTracking permission...");
                OVRPermissionsRequester.Request(new[] { OVRPermissionsRequester.Permission.BodyTracking });
            }

            if (_ovrBody != null) _ovrBody.enabled = true;
            if (_ovrSkeleton != null) _ovrSkeleton.enabled = true;

            // Apply Height Override from PlayerPrefs (Key matches PlayerPrefsConfigSO)
            float heightOverride = PlayerPrefs.GetFloat("Apex_UserHeight", 1.65f);
            OVRBody.SuggestBodyTrackingCalibrationOverride(heightOverride);

            _isTrackingActive = true;

            if (_visualRoot != null)
            {
                _visualRoot.SetActive(true);
            }

            Logger.LogApp($"[OVRBodyBoneVisualizer] Body tracking started. Components - Body: {_ovrBody != null}, Skeleton: {_ovrSkeleton != null}");
        }

        public void StopBodyTracking()
        {
            if (_visualRoot != null)
            {
                _visualRoot.SetActive(false);
            }

            if (_ovrSkeleton != null) _ovrSkeleton.enabled = false;
            if (_ovrBody != null) _ovrBody.enabled = false;

            _isTrackingActive = false;
            Logger.LogApp("[OVRBodyBoneVisualizer] Body tracking stopped.");
        }

        public void ToggleBodyTracking()
        {
            if (_isTrackingActive)
            {
                StopBodyTracking();
            }
            else
            {
                StartBodyTracking();
            }
        }

        private void LateUpdate()
        {
            if (!_isTrackingActive || _ovrSkeleton == null)
            {
                if (_visualRoot != null && _visualRoot.activeSelf) _visualRoot.SetActive(false);
                return;
            }

            if (!_ovrSkeleton.IsInitialized)
            {
                if (Time.time - _lastLogTime > 3.0f)
                {
                    _lastLogTime = Time.time;
                    Logger.LogApp("[OVRBodyBoneVisualizer] Waiting for OVRSkeleton initialization...", LogType.Warning);
                }
                return;
            }

            if (!_prefabsInitialized)
            {
                InitializePrefabs();
                if (!_prefabsInitialized) return;
            }

            bool isDataValid = _ovrSkeleton.IsDataValid;
            if (_visualRoot.activeSelf != isDataValid)
            {
                _visualRoot.SetActive(isDataValid);
            }

            if (isDataValid)
            {
                UpdatePrefabs();
            }
        }

        private void InitializePrefabs()
        {
            var bones = _ovrSkeleton.Bones;
            if (bones == null || bones.Count == 0)
            {
                Logger.LogApp("[OVRBodyBoneVisualizer] InitializePrefabs skipped: Bones list is empty.", LogType.Warning);
                return;
            }

            int boneCount = bones.Count;

            _visualRoot = new GameObject("OVRBodyBoneVisualizerRoot");
            _visualRoot.transform.SetParent(transform, false);

            // 1. Spawn Node Prefabs
            if (_nodePrefab != null)
            {
                _nodeTransforms = new Transform[boneCount];
                for (int i = 0; i < boneCount; i++)
                {
                    var nodeGO = Instantiate(_nodePrefab, _visualRoot.transform);
                    nodeGO.name = $"Node_{bones[i].Id}";
                    _nodeTransforms[i] = nodeGO.transform;
                }
            }

            // 2. Spawn Skeleton Prefabs for Bone Connections
            if (_skeletonPrefab != null)
            {
                int connectionCount = 0;
                for (int i = 0; i < boneCount; i++)
                {
                    short parentIdx = bones[i].ParentBoneIndex;
                    if (parentIdx >= 0 && parentIdx < boneCount)
                    {
                        connectionCount++;
                    }
                }

                _boneConnections = new SkeletonConnection[connectionCount];
                int connIdx = 0;

                for (int i = 0; i < boneCount; i++)
                {
                    short parentIdx = bones[i].ParentBoneIndex;
                    if (parentIdx >= 0 && parentIdx < boneCount)
                    {
                        var skeletonGO = Instantiate(_skeletonPrefab, _visualRoot.transform);
                        skeletonGO.name = $"Skeleton_{bones[parentIdx].Id}_to_{bones[i].Id}";

                        _boneConnections[connIdx] = new SkeletonConnection
                        {
                            ParentIndex = parentIdx,
                            ChildIndex = i,
                            Transform = skeletonGO.transform,
                            BaseScale = _skeletonPrefab.transform.localScale
                        };
                        connIdx++;
                    }
                }
            }

            _prefabsInitialized = true;
            Logger.LogApp($"[OVRBodyBoneVisualizer] Prefabs initialized successfully. Bones: {boneCount}, Nodes Spawned: {(_nodeTransforms != null ? _nodeTransforms.Length : 0)}, Skeletons Spawned: {(_boneConnections != null ? _boneConnections.Length : 0)}");
        }

        private void UpdatePrefabs()
        {
            var bones = _ovrSkeleton.Bones;
            if (bones == null) return;

            int boneCount = bones.Count;

            // Update Node Poses
            if (_nodeTransforms != null)
            {
                for (int i = 0; i < _nodeTransforms.Length && i < boneCount; i++)
                {
                    Transform nodeTx = _nodeTransforms[i];
                    if (nodeTx == null) continue;

                    Transform boneTx = bones[i].Transform;
                    if (boneTx != null)
                    {
                        nodeTx.SetPositionAndRotation(boneTx.position, boneTx.rotation);
                    }
                }
            }

            // Update Skeleton Connections
            if (_boneConnections != null)
            {
                for (int i = 0; i < _boneConnections.Length; i++)
                {
                    ref readonly var conn = ref _boneConnections[i];
                    if (conn.Transform == null) continue;

                    if (conn.ParentIndex < 0 || conn.ParentIndex >= boneCount ||
                        conn.ChildIndex < 0 || conn.ChildIndex >= boneCount)
                    {
                        continue;
                    }

                    Transform parentTx = bones[conn.ParentIndex].Transform;
                    Transform childTx = bones[conn.ChildIndex].Transform;

                    if (parentTx == null || childTx == null) continue;

                    Vector3 posA = parentTx.position;
                    Vector3 posB = childTx.position;
                    Vector3 dir = posB - posA;
                    float dist = dir.magnitude;

                    if (dist > 0.0001f)
                    {
                        Vector3 normDir = dir / dist;
                        Quaternion rot = Quaternion.FromToRotation(Vector3.up, normDir);
                        Vector3 pos = _pivotAtCenter ? (posA + posB) * 0.5f : posA;

                        conn.Transform.SetPositionAndRotation(pos, rot);

                        Vector3 scale = conn.BaseScale;
                        scale.y = dist;
                        conn.Transform.localScale = scale;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (_visualRoot != null)
            {
                Destroy(_visualRoot);
            }
        }
    }
}
#endif