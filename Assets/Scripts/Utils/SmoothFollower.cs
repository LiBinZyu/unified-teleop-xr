using UnityEngine;
using System.Runtime.CompilerServices;

namespace Apex.Dashboard
{
    [ExecuteAlways]
    public class SmoothFollower : MonoBehaviour
    {
        public enum FollowMode
        {
            Planar,
            Floating,
            Spherical
        }

        public Transform target;
        public FollowMode mode = FollowMode.Planar;

        public float distance = 1.5f;
        public float heightOffset = 0f;

        public float posDeadzone = 0.08f;
        public float rotDeadzone = 8f;

        public float moveSpeed = 4f;
        public float rotSpeed = 5f;

        [Header("Debug")]
        public bool previewInEditor = false;
        public bool showGizmo = false;

        private bool _initialized;
        private float _initialY;

        private Vector3 _anchorPos;
        private Quaternion _anchorRot;

        private Vector3 _desiredPos;
        private Quaternion _desiredRot;
        private Vector3 _velPos;

        private Transform _t;
        private float _lastPosDz;
        private float _lastRotDz;
        private float _posDzSqr;
        private float _rotDzDot;

#if UNITY_EDITOR
        private float _editorBaseY;
        
        private void OnValidate()
        {
            if (previewInEditor && !Application.isPlaying)
            {
                _editorBaseY = transform.position.y;
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.update += EditorUpdate;
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.update -= EditorUpdate;
        }

        private void EditorUpdate()
        {
            if (previewInEditor && !Application.isPlaying)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
            }
        }
#endif

        private void Start()
        {
            if (Application.isPlaying)
            {
                Initialize();
            }
        }

        public void Initialize()
        {
            if (target == null) target = Camera.main != null ? Camera.main.transform : null;
            if (target == null) return;

            _t = transform;
            _initialY = _t.position.y;
            _anchorPos = target.position;
            _anchorRot = target.rotation;
            
            _desiredPos = _t.position;
            _desiredRot = _t.rotation;
            
            UpdateCachedValues();
            _initialized = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateCachedValues()
        {
            if (_lastPosDz != posDeadzone || _lastRotDz != rotDeadzone)
            {
                _lastPosDz = posDeadzone;
                _lastRotDz = rotDeadzone;
                _posDzSqr = posDeadzone * posDeadzone;
                _rotDzDot = Mathf.Cos(rotDeadzone * 0.5f * Mathf.Deg2Rad);
            }
        }

        private void LateUpdate()
        {
            if (target == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!previewInEditor) return;
                
                if (!_initialized || _t == null)
                {
                    Initialize();
                }
            }
#endif

            if (!_initialized) return;
            
            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
            if (dt <= 0f) dt = 0.016f;

            UpdateCachedValues();

            Vector3 tPos = target.position;
            Quaternion tRot = target.rotation;

            Vector3 diff = _anchorPos - tPos;
            if (diff.sqrMagnitude > _posDzSqr)
            {
                _anchorPos = Vector3.MoveTowards(tPos, _anchorPos, posDeadzone);
            }

            if (Mathf.Abs(Quaternion.Dot(_anchorRot, tRot)) < _rotDzDot)
            {
                _anchorRot = Quaternion.RotateTowards(tRot, _anchorRot, rotDeadzone);
            }

            CalculatePose(out _desiredPos, out _desiredRot);

            float smoothTime = 1f / Mathf.Max(moveSpeed, 0.01f);
            
            Vector3 nextPos = Vector3.SmoothDamp(_t.position, _desiredPos, ref _velPos, smoothTime, Mathf.Infinity, dt);
            Quaternion nextRot = Quaternion.RotateTowards(_t.rotation, _desiredRot, rotSpeed * 60f * dt);
            
            _t.SetPositionAndRotation(nextPos, nextRot);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CalculatePose(out Vector3 pos, out Quaternion rot)
        {
            Vector3 fwd = _anchorRot * Vector3.forward;

            if (mode == FollowMode.Planar || mode == FollowMode.Floating)
            {
                Vector3 planarFwd = new Vector3(fwd.x, 0, fwd.z);
                if (planarFwd.sqrMagnitude > 0.0001f)
                {
                    float invMag = 1f / Mathf.Sqrt(planarFwd.sqrMagnitude);
                    planarFwd.x *= invMag;
                    planarFwd.z *= invMag;
                }
                else
                {
                    Vector3 up = _anchorRot * Vector3.up;
                    planarFwd = new Vector3(up.x, 0, up.z).normalized;
                }
                
                pos.x = _anchorPos.x + planarFwd.x * distance;
                pos.z = _anchorPos.z + planarFwd.z * distance;
                
                if (mode == FollowMode.Planar)
                {
#if UNITY_EDITOR
                    pos.y = (Application.isPlaying ? _initialY : _editorBaseY) + heightOffset;
#else
                    pos.y = _initialY + heightOffset;
#endif
                }
                else
                {
                    pos.y = _anchorPos.y + heightOffset;
                }
            }
            else
            {
                pos.x = _anchorPos.x + fwd.x * distance;
                pos.y = _anchorPos.y + fwd.y * distance + heightOffset;
                pos.z = _anchorPos.z + fwd.z * distance;
            }

            Vector3 dirToUI = pos - _anchorPos;
            if (dirToUI.sqrMagnitude > 0.001f)
            {
                rot = Quaternion.LookRotation(dirToUI);

                if (mode == FollowMode.Planar || mode == FollowMode.Floating)
                {
                    Vector3 euler = rot.eulerAngles;
                    rot = Quaternion.Euler(0f, euler.y, 0f);
                }
                else
                {
                    Vector3 euler = rot.eulerAngles;
                    rot = Quaternion.Euler(euler.x, euler.y, 0f);
                }
            }
            else
            {
                rot = _t.rotation;
            }
        }

        public void Snap()
        {
            if (!_initialized) return;
            _t.SetPositionAndRotation(_desiredPos, _desiredRot);
            _velPos = Vector3.zero;
        }

        public void ResetBaseHeight() => _initialY = _t.position.y;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!showGizmo || target == null || (!Application.isPlaying && !previewInEditor)) return;
            Gizmos.color = new Color(0f, 1f, 0f, 0.7f);
            Gizmos.DrawSphere(_desiredPos, 0.04f);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, _desiredPos);
        }
#endif
    }
}
