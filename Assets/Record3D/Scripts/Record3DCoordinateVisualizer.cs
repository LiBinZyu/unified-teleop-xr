using System;
using UnityEngine;

namespace Record3D
{
    /// <summary>
    /// Visualizes the World Origin (0,0,0) and the iPhone Device Pose (Position + Orientation)
    /// both in 3D (interactive axes, phone model, and camera frustum) and via a 2D GUI HUD & Top-Down Radar.
    /// </summary>
    public class Record3DCoordinateVisualizer : IDisposable
    {
        private GameObject _rootObject;
        private GameObject _originObject;
        private GameObject _iphoneObject;
        private LineRenderer _frustumLine;

        private Material _redMaterial;
        private Material _greenMaterial;
        private Material _blueMaterial;
        private Material _yellowMaterial;
        private Material _cyanMaterial;
        private Material _phoneBodyMaterial;
        private Material _phoneScreenMaterial;

        private bool _show3DGizmo = true;
        private bool _showGuiPanel = true;

        private Texture2D _radarBackgroundTex;
        private Texture2D _radarDotTex;
        private GUIStyle _radarBoxStyle;
        private GUIStyle _labelHeaderStyle;
        private GUIStyle _valueStyle;

        private Transform _parentTransform;
        private Record3DManager _manager;

        public bool Show3DGizmo
        {
            get => _show3DGizmo;
            set
            {
                _show3DGizmo = value;
                if (_rootObject != null)
                {
                    _rootObject.SetActive(value);
                }
            }
        }

        public bool ShowGuiPanel
        {
            get => _showGuiPanel;
            set => _showGuiPanel = value;
        }

        public GameObject IPhoneTrackerObject => _iphoneObject;
        public GameObject OriginObject => _originObject;

        public Record3DCoordinateVisualizer(Transform parentTransform = null, Record3DManager manager = null)
        {
            _parentTransform = parentTransform;
            _manager = manager;
            InitMaterials();
            Build3DVisualizers(parentTransform);
            InitRadarTextures();
        }

        private void InitMaterials()
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

            _redMaterial = new Material(unlitShader) { name = "Coord_Red", color = new Color(1.0f, 0.2f, 0.2f, 1.0f) };
            _greenMaterial = new Material(unlitShader) { name = "Coord_Green", color = new Color(0.2f, 1.0f, 0.3f, 1.0f) };
            _blueMaterial = new Material(unlitShader) { name = "Coord_Blue", color = new Color(0.2f, 0.5f, 1.0f, 1.0f) };
            _yellowMaterial = new Material(unlitShader) { name = "Coord_Yellow", color = new Color(1.0f, 0.85f, 0.2f, 1.0f) };
            _cyanMaterial = new Material(unlitShader) { name = "Coord_Cyan", color = new Color(0.0f, 0.9f, 1.0f, 0.85f) };
            _phoneBodyMaterial = new Material(unlitShader) { name = "Coord_PhoneBody", color = new Color(0.12f, 0.13f, 0.16f, 1.0f) };
            _phoneScreenMaterial = new Material(unlitShader) { name = "Coord_PhoneScreen", color = new Color(0.05f, 0.22f, 0.35f, 1.0f) };
        }

        private void Build3DVisualizers(Transform parentTransform)
        {
            _rootObject = new GameObject("Record3D_Coordinates");
            if (parentTransform != null)
            {
                _rootObject.transform.SetParent(parentTransform, false);
            }

            // -------------------------------------------------------------
            // 1. World Origin Coordinate Frame (0, 0, 0)
            // -------------------------------------------------------------
            _originObject = new GameObject("Origin_Anchor");
            _originObject.transform.SetParent(_rootObject.transform, false);
            _originObject.transform.localPosition = Vector3.zero;
            _originObject.transform.localRotation = Quaternion.identity;

            // Center marker sphere
            var originSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            originSphere.name = "Origin_Marker";
            originSphere.transform.SetParent(_originObject.transform, false);
            originSphere.transform.localScale = Vector3.one * 0.04f;
            originSphere.GetComponent<MeshRenderer>().material = _yellowMaterial;
            UnityEngine.Object.DestroyImmediate(originSphere.GetComponent<Collider>());

            // Origin Axes: Red +X, Green +Y, Blue +Z (0.25m long)
            CreateAxisBox(_originObject.transform, "Axis_X_Red", Vector3.right, 0.25f, 0.008f, _redMaterial);
            CreateAxisBox(_originObject.transform, "Axis_Y_Green", Vector3.up, 0.25f, 0.008f, _greenMaterial);
            CreateAxisBox(_originObject.transform, "Axis_Z_Blue", Vector3.forward, 0.25f, 0.008f, _blueMaterial);

            // -------------------------------------------------------------
            // 2. iPhone Device Pose Visualizer
            // -------------------------------------------------------------
            _iphoneObject = new GameObject("iPhone_Device");
            _iphoneObject.transform.SetParent(_rootObject.transform, false);
            _iphoneObject.transform.localPosition = new Vector3(0, 0, 0.5f);
            _iphoneObject.transform.localRotation = Quaternion.identity;

            // Device Local Axes: Red +X, Green +Y, Blue +Z (pointing forward)
            CreateAxisBox(_iphoneObject.transform, "Device_X_Red", Vector3.right, 0.15f, 0.006f, _redMaterial);
            CreateAxisBox(_iphoneObject.transform, "Device_Y_Green", Vector3.up, 0.15f, 0.006f, _greenMaterial);
            CreateAxisBox(_iphoneObject.transform, "Device_Z_Blue", Vector3.forward, 0.25f, 0.006f, _blueMaterial);

            // Stylized iPhone Body (approx iPhone 14/15 Pro: 71.5mm x 147.5mm x 7.85mm)
            var phoneBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
            phoneBody.name = "iPhone_Body";
            phoneBody.transform.SetParent(_iphoneObject.transform, false);
            phoneBody.transform.localPosition = new Vector3(0, 0, -0.004f);
            phoneBody.transform.localScale = new Vector3(0.072f, 0.148f, 0.008f);
            phoneBody.GetComponent<MeshRenderer>().material = _phoneBodyMaterial;
            UnityEngine.Object.DestroyImmediate(phoneBody.GetComponent<Collider>());

            // Screen face (facing user / back)
            var phoneScreen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            phoneScreen.name = "iPhone_Screen";
            phoneScreen.transform.SetParent(phoneBody.transform, false);
            phoneScreen.transform.localPosition = new Vector3(0, 0, -0.52f);
            phoneScreen.transform.localScale = new Vector3(0.92f, 0.94f, 0.05f);
            phoneScreen.GetComponent<MeshRenderer>().material = _phoneScreenMaterial;
            UnityEngine.Object.DestroyImmediate(phoneScreen.GetComponent<Collider>());

            // Camera module bump (facing forward along +Z)
            var cameraBump = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cameraBump.name = "Camera_Bump";
            cameraBump.transform.SetParent(phoneBody.transform, false);
            cameraBump.transform.localPosition = new Vector3(-0.24f, 0.32f, 0.52f);
            cameraBump.transform.localScale = new Vector3(0.42f, 0.26f, 0.08f);
            cameraBump.GetComponent<MeshRenderer>().material = _yellowMaterial;
            UnityEngine.Object.DestroyImmediate(cameraBump.GetComponent<Collider>());

            // -------------------------------------------------------------
            // 3. Camera Viewing Frustum Wireframe (projecting forward along +Z)
            // -------------------------------------------------------------
            var frustumGo = new GameObject("Camera_Frustum");
            frustumGo.transform.SetParent(_iphoneObject.transform, false);
            _frustumLine = frustumGo.AddComponent<LineRenderer>();
            _frustumLine.useWorldSpace = false;
            _frustumLine.startWidth = 0.003f;
            _frustumLine.endWidth = 0.003f;
            _frustumLine.material = _cyanMaterial;
            _frustumLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _frustumLine.receiveShadows = false;

            // Frustum wireframe path:
            // Apex (0,0,0) -> TL -> TR -> Apex -> BR -> BL -> Apex -> TL -> BL -> BR -> TR -> TL
            float fZ = 0.35f;
            float fW = 0.15f;
            float fH = 0.15f;
            Vector3 apex = Vector3.zero;
            Vector3 tl = new Vector3(-fW, fH, fZ);
            Vector3 tr = new Vector3(fW, fH, fZ);
            Vector3 br = new Vector3(fW, -fH, fZ);
            Vector3 bl = new Vector3(-fW, -fH, fZ);

            Vector3[] points = new Vector3[]
            {
                apex, tl, tr, apex, br, bl, apex, tl, bl, br, tr, tl
            };

            _frustumLine.positionCount = points.Length;
            _frustumLine.SetPositions(points);
        }

        private void CreateAxisBox(Transform parent, string name, Vector3 direction, float length, float thickness, Material mat)
        {
            var axisGo = new GameObject(name);
            axisGo.transform.SetParent(parent, false);

            // Cylinder stem
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stem.name = "Stem";
            stem.transform.SetParent(axisGo.transform, false);
            stem.transform.localPosition = direction * (length * 0.5f);

            // Orient cube along direction
            if (direction == Vector3.right) stem.transform.localScale = new Vector3(length, thickness, thickness);
            else if (direction == Vector3.up) stem.transform.localScale = new Vector3(thickness, length, thickness);
            else stem.transform.localScale = new Vector3(thickness, thickness, length);

            stem.GetComponent<MeshRenderer>().material = mat;
            UnityEngine.Object.DestroyImmediate(stem.GetComponent<Collider>());

            // Arrow head
            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrow.name = "Tip";
            arrow.transform.SetParent(axisGo.transform, false);
            arrow.transform.localPosition = direction * length;
            arrow.transform.localScale = Vector3.one * (thickness * 2.2f);
            arrow.GetComponent<MeshRenderer>().material = mat;
            UnityEngine.Object.DestroyImmediate(arrow.GetComponent<Collider>());
        }

        private void InitRadarTextures()
        {
            // 2D Radar background texture (dark grid)
            int size = 120;
            _radarBackgroundTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color bg = new Color(0.08f, 0.10f, 0.14f, 0.90f);
            Color grid = new Color(0.20f, 0.25f, 0.35f, 0.60f);
            Color axisCol = new Color(0.35f, 0.45f, 0.60f, 0.90f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isBorder = (x == 0 || x == size - 1 || y == 0 || y == size - 1);
                    bool isCenterAxis = (x == size / 2 || y == size / 2);
                    bool isSubGrid = (x % 30 == 0 || y % 30 == 0);

                    if (isBorder || isCenterAxis)
                        _radarBackgroundTex.SetPixel(x, y, axisCol);
                    else if (isSubGrid)
                        _radarBackgroundTex.SetPixel(x, y, grid);
                    else
                        _radarBackgroundTex.SetPixel(x, y, bg);
                }
            }
            _radarBackgroundTex.Apply();

            // Dot texture for origin and device
            _radarDotTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            for (int i = 0; i < 16; i++) _radarDotTex.SetPixel(i % 4, i / 4, Color.white);
            _radarDotTex.Apply();
        }

        /// <summary>
        /// Updates the 3D position & rotation of the iPhone tracker object, aligned with UseDevicePose.
        /// </summary>
        public void UpdatePose(Vector3 devicePos, Quaternion deviceRot, bool useDevicePose = true)
        {
            if (_iphoneObject != null)
            {
                if (useDevicePose)
                {
                    _iphoneObject.transform.localPosition = devicePos;
                    _iphoneObject.transform.localRotation = deviceRot;
                }
                else
                {
                    // Locked to anchor origin when device pose is disabled in Manager
                    _iphoneObject.transform.localPosition = Vector3.zero;
                    _iphoneObject.transform.localRotation = Quaternion.identity;
                }
            }
        }

        /// <summary>
        /// Draws the on-screen GUI Coordinate System card and 2D Top-Down Radar, strictly aligned with Manager settings.
        /// </summary>
        public void DrawGUI(Vector3 devicePos, Quaternion deviceRot, bool usePose)
        {
            // Sync visibility with Manager's enableGui
            if (_manager != null)
            {
                _showGuiPanel = _manager.enableGui;
            }

            if (!_showGuiPanel)
                return;

            EnsureGuiStyles();

            const int panelWidth = 320;
            const int panelHeight = 265;
            Rect panelRect = new Rect(Screen.width - panelWidth - 20, 20, panelWidth, panelHeight);

            GUI.Box(panelRect, "Record3D Coordinate Space (iPhone & Origin)");
            GUILayout.BeginArea(new Rect(panelRect.x + 10, panelRect.y + 25, panelWidth - 20, panelHeight - 30));

            // Origin Information - Aligned with Manager's Transform in World Space
            Vector3 worldOrigin = _parentTransform != null ? _parentTransform.position : Vector3.zero;
            GUILayout.Label($"<b>Anchor (Origin):</b> (0.000, 0.000, 0.000) | <b>World:</b> ({worldOrigin.x:+0.00;-0.00}, {worldOrigin.y:+0.00;-0.00}, {worldOrigin.z:+0.00;-0.00})", _labelHeaderStyle);

            Vector3 effectivePos = usePose ? devicePos : Vector3.zero;
            Quaternion effectiveRot = usePose ? deviceRot : Quaternion.identity;
            Vector3 euler = effectiveRot.eulerAngles;
            float distToOrigin = effectivePos.magnitude;

            // Device Numerical Coordinates
            GUILayout.Space(2);
            if (usePose)
            {
                GUILayout.Label($"<b>iPhone Pos:</b> X: <color=#ff5252>{effectivePos.x:+0.000;-0.000}m</color>  Y: <color=#69f0ae>{effectivePos.y:+0.000;-0.000}m</color>  Z: <color=#40c4ff>{effectivePos.z:+0.000;-0.000}m</color>", _valueStyle);
                GUILayout.Label($"<b>iPhone Rot:</b> P: {euler.x:F1}°  Y: {euler.y:F1}°  R: {euler.z:F1}°", _valueStyle);
                GUILayout.Label($"<b>Dist to Origin:</b> <color=#ffd740>{distToOrigin:F3} m</color> | <b>Pose Sync:</b> <color=#69f0ae>Active</color>", _valueStyle);
            }
            else
            {
                GUILayout.Label("<b>iPhone Pos:</b> <color=#888888>Locked to Anchor (0.000, 0.000, 0.000)</color>", _valueStyle);
                GUILayout.Label("<b>iPhone Rot:</b> <color=#888888>Locked to Anchor (P: 0.0° Y: 0.0° R: 0.0°)</color>", _valueStyle);
                GUILayout.Label("<b>Dist to Origin:</b> <color=#888888>0.000 m</color> | <b>Pose Sync:</b> <color=#ffd740>Anchor (Disabled)</color>", _valueStyle);
            }

            GUILayout.Space(4);

            // Controls & 2D Top-Down Radar layout
            GUILayout.BeginHorizontal();

            // Left side: Buttons
            GUILayout.BeginVertical(GUILayout.Width(130));
            GUI.color = _show3DGizmo ? Color.cyan : Color.white;
            if (GUILayout.Button(_show3DGizmo ? "3D Gizmo: ON" : "3D Gizmo: OFF", GUILayout.Height(24)))
            {
                Show3DGizmo = !Show3DGizmo;
            }
            GUI.color = Color.white;

            if (GUILayout.Button("Focus iPhone", GUILayout.Height(24)))
            {
                FocusOnDevice(effectivePos);
            }
            if (GUILayout.Button("Focus Origin", GUILayout.Height(24)))
            {
                FocusOnOrigin();
            }
            GUI.color = new Color(0.2f, 1f, 0.6f);
            if (GUILayout.Button("Reset Pose", GUILayout.Height(24)))
            {
                if (_manager != null) _manager.ResetIPhonePose();
                else Record3DManager.ResetCurrentPose();
            }
            GUI.color = Color.white;
            GUILayout.EndVertical();

            GUILayout.Space(10);

            // Right side: 2D Top-Down Radar (XZ plane) - strictly reflecting effective position
            DrawRadarView(effectivePos, effectiveRot);

            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        public void DrawRadarView(Vector3 devicePos, Quaternion deviceRot, float radarSize = 100f)
        {
            Rect radarRect = GUILayoutUtility.GetRect(radarSize, radarSize);

            if (_radarBackgroundTex != null)
            {
                GUI.DrawTexture(radarRect, _radarBackgroundTex);
            }

            Vector2 center = new Vector2(radarRect.x + radarSize * 0.5f, radarRect.y + radarSize * 0.5f);

            // Draw Origin marker at center (Yellow)
            GUI.color = Color.yellow;
            GUI.DrawTexture(new Rect(center.x - 3, center.y - 3, 6, 6), _radarDotTex);

            // Map iPhone position: Range ±2.0m on radar
            const float maxRange = 2.0f;
            float normX = Mathf.Clamp(devicePos.x / maxRange, -1f, 1f);
            float normZ = Mathf.Clamp(devicePos.z / maxRange, -1f, 1f);

            // In top-down radar: X right, Z up (negative GUI Y is up)
            Vector2 phoneGuiPos = new Vector2(center.x + normX * (radarSize * 0.45f), center.y - normZ * (radarSize * 0.45f));

            // Draw iPhone marker (Cyan)
            GUI.color = Color.cyan;
            GUI.DrawTexture(new Rect(phoneGuiPos.x - 4, phoneGuiPos.y - 4, 8, 8), _radarDotTex);

            // Draw heading direction pointer
            Vector3 forward = deviceRot * Vector3.forward;
            Vector2 forward2D = new Vector2(forward.x, -forward.z).normalized * 12f;
            Vector2 arrowTip = phoneGuiPos + forward2D;

            GUI.color = Color.white;
            GUI.Label(new Rect(radarRect.x + 4, radarRect.y + 2, 60, 16), "<size=9><color=#888888>Top View</color></size>");
            GUI.Label(new Rect(radarRect.x + radarSize - 28, radarRect.y + radarSize - 16, 30, 16), "<size=9><color=#888888>2m</color></size>");
        }

        public void FocusOnDevice(Vector3 devicePos)
        {
            Vector3 worldPos = (_iphoneObject != null) ? _iphoneObject.transform.position : devicePos;
            if (PlayCam.Instance != null)
            {
                PlayCam.Instance.Focus(worldPos, worldPos + new Vector3(0, 0.2f, -0.6f));
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) return;
            cam.transform.position = worldPos + new Vector3(0, 0.2f, -0.6f);
            cam.transform.LookAt(worldPos);
        }

        public void FocusOnOrigin()
        {
            Vector3 worldOrigin = (_originObject != null) ? _originObject.transform.position : Vector3.zero;
            if (PlayCam.Instance != null)
            {
                PlayCam.Instance.Focus(worldOrigin, worldOrigin + new Vector3(0, 0.4f, -1.0f));
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) return;
            cam.transform.position = worldOrigin + new Vector3(0, 0.4f, -1.0f);
            cam.transform.LookAt(worldOrigin);
        }

        private void EnsureGuiStyles()
        {
            if (_labelHeaderStyle == null)
            {
                _labelHeaderStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    richText = true,
                    wordWrap = true
                };
            }
            if (_valueStyle == null)
            {
                _valueStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    richText = true
                };
            }
        }

        public void Dispose()
        {
            if (_rootObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_rootObject);
                _rootObject = null;
            }

            if (_radarBackgroundTex != null)
            {
                UnityEngine.Object.DestroyImmediate(_radarBackgroundTex);
                _radarBackgroundTex = null;
            }
            if (_radarDotTex != null)
            {
                UnityEngine.Object.DestroyImmediate(_radarDotTex);
                _radarDotTex = null;
            }

            if (_redMaterial != null) UnityEngine.Object.DestroyImmediate(_redMaterial);
            if (_greenMaterial != null) UnityEngine.Object.DestroyImmediate(_greenMaterial);
            if (_blueMaterial != null) UnityEngine.Object.DestroyImmediate(_blueMaterial);
            if (_yellowMaterial != null) UnityEngine.Object.DestroyImmediate(_yellowMaterial);
            if (_cyanMaterial != null) UnityEngine.Object.DestroyImmediate(_cyanMaterial);
            if (_phoneBodyMaterial != null) UnityEngine.Object.DestroyImmediate(_phoneBodyMaterial);
            if (_phoneScreenMaterial != null) UnityEngine.Object.DestroyImmediate(_phoneScreenMaterial);
        }
    }
}
