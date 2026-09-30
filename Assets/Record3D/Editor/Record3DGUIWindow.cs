#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Record3D.Editor
{
    /// <summary>
    /// Streamlined Editor Window and Game-View Overlay for Record3D.
    /// Focuses purely on high-level operational controls.
    /// </summary>
    public class Record3DGUIWindow : EditorWindow
    {
        [MenuItem("Window/Record3D/Control Window", priority = 2050)]
        [MenuItem("Record3D/Control Window", priority = 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<Record3DGUIWindow>("Record3D Control");
            window.minSize = new Vector2(300, 260);
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void RegisterGameViewOverlay()
        {
            Record3DManager.OnDrawDebugGUI -= DrawGameViewOverlay;
            Record3DManager.OnDrawDebugGUI += DrawGameViewOverlay;
        }

        private void OnEnable()
        {
            EditorApplication.update += RepaintOnPlay;
            RegisterGameViewOverlay();
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintOnPlay;
        }

        private void RepaintOnPlay()
        {
            if (Application.isPlaying) Repaint();
        }

        // =========================================================================
        // 1. Editor Window GUI (High-Level Controls)
        // =========================================================================
        private void OnGUI()
        {
            Record3DManager manager = Record3DManager.Instance;

            EditorGUILayout.LabelField("<b>Record3D Pipeline</b>", new GUIStyle(EditorStyles.boldLabel) { richText = true });

            if (manager == null || !Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to run live stream reconstruction.", MessageType.Info);
                if (GUILayout.Button("Start Play Mode", GUILayout.Height(30))) EditorApplication.isPlaying = true;
                return;
            }

            // Status & Performance
            var gpu = manager.GpuProcessor;
            string resStr = gpu != null ? $"{gpu.CurrentRgbWidth}x{gpu.CurrentRgbHeight}" : "N/A";
            EditorGUILayout.LabelField("Status", manager.StatusMessage);
            EditorGUILayout.LabelField("Performance", $"FPS: {manager.Fps:F1} | Stream: {resStr}");

            EditorGUILayout.Space(6);

            // High-Level Mode Selection
            GUILayout.BeginHorizontal();
            GUI.color = (manager.RenderingMode == RenderingMode.Points) ? Color.cyan : Color.white;
            if (GUILayout.Button("Point Cloud", GUILayout.Height(28))) manager.SwitchRenderingMode(RenderingMode.Points);
            GUI.color = (manager.RenderingMode == RenderingMode.Mesh) ? Color.cyan : Color.white;
            if (GUILayout.Button("Solid Mesh", GUILayout.Height(28))) manager.SwitchRenderingMode(RenderingMode.Mesh);
            GUI.color = (manager.RenderingMode == RenderingMode.RoomScan) ? Color.cyan : Color.white;
            if (GUILayout.Button("RoomScan", GUILayout.Height(28))) manager.SwitchRenderingMode(RenderingMode.RoomScan);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Core Logic Toggles
            GUILayout.BeginHorizontal();
            GUI.color = manager.FlipY ? Color.yellow : Color.white;
            if (GUILayout.Button(manager.FlipY ? "Flip Y: ON" : "Flip Y: OFF", GUILayout.Height(24))) manager.FlipY = !manager.FlipY;
            GUI.color = manager.UseDevicePose ? Color.yellow : Color.white;
            if (GUILayout.Button(manager.UseDevicePose ? "Pose: ON" : "Pose: OFF", GUILayout.Height(24))) manager.UseDevicePose = !manager.UseDevicePose;
            GUI.color = manager.EnableDithering ? Color.cyan : Color.white;
            if (GUILayout.Button(manager.EnableDithering ? "Dither: ON" : "Dither: OFF", GUILayout.Height(24))) manager.EnableDithering = !manager.EnableDithering;
            GUI.color = manager.EnableAutoProbe ? Color.green : Color.white;
            if (GUILayout.Button(manager.EnableAutoProbe ? "AutoProbe: ON" : "AutoProbe: OFF", GUILayout.Height(24))) manager.EnableAutoProbe = !manager.EnableAutoProbe;
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Camera Navigation & Origin Calibration
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Focus Camera")) PlayCam.Instance?.Focus();
            if (GUILayout.Button("Focus iPhone")) manager.CoordinateVisualizer?.FocusOnDevice(manager.LatestMetadata.cameraPosition);
            if (GUILayout.Button("Focus Origin")) manager.CoordinateVisualizer?.FocusOnOrigin();
            GUI.color = new Color(0.2f, 1f, 0.6f);
            if (GUILayout.Button("Reset Pose")) manager.ResetIPhonePose();
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Pipeline & Connection
            GUILayout.BeginHorizontal();
            manager.DeviceIp = EditorGUILayout.TextField("IP", manager.DeviceIp);
            manager.DevicePort = EditorGUILayout.IntField("Port", manager.DevicePort, GUILayout.Width(110));
            bool isConnectedEditor = manager.Receiver != null && manager.Receiver.IsConnected;
            GUI.color = isConnectedEditor ? Color.yellow : Color.white;
            if (GUILayout.Button(isConnectedEditor ? "Disconnect" : "Connect", GUILayout.Width(80)))
            {
                if (isConnectedEditor) manager.Disconnect();
                else manager.Connect();
            }
            GUI.color = manager.IsPipelineRunning ? Color.green : Color.red;
            if (GUILayout.Button(manager.IsPipelineRunning ? "Pipeline: ON" : "Pipeline: OFF", GUILayout.Width(100)))
            {
                manager.TogglePipeline();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            manager.IngestFps = EditorGUILayout.IntSlider("Ingest FPS (Throttle)", manager.IngestFps, 5, 60);

            if (manager.RenderingMode == RenderingMode.RoomScan)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("<b>RoomScan Persistent Reconstruction</b>", new GUIStyle(EditorStyles.miniBoldLabel) { richText = true });
                manager.RoomScanAnchors = EditorGUILayout.IntSlider("Anchors (Sliding Window)", manager.RoomScanAnchors, 1, 16);
                manager.RoomScanMeshLod = EditorGUILayout.IntSlider("RoomScan LOD", manager.RoomScanMeshLod, 1, 6);
            }

            EditorGUILayout.Space(4);
            manager.enablegui = EditorGUILayout.ToggleLeft("Show In-Game Overlay (enablegui)", manager.enablegui);
        }

        // =========================================================================
        // 2. In-Game View Overlay (High-Level Controls Only)
        // =========================================================================
        public static void DrawGameViewOverlay(Record3DManager manager)
        {
            if (manager == null || !manager.enablegui || !Application.isPlaying) return;

            const int width = 280;
            const int height = 155;
            GUI.Box(new Rect(15, 15, width, height), "Record3D Controls");
            GUILayout.BeginArea(new Rect(22, 38, width - 14, height - 28));

            GUILayout.Label($"FPS: {manager.Fps:F1} | {manager.RenderingMode} | {manager.StatusMessage}");

            // Mode
            GUILayout.BeginHorizontal();
            GUI.color = (manager.RenderingMode == RenderingMode.Points) ? Color.cyan : Color.white;
            if (GUILayout.Button("Points")) manager.SwitchRenderingMode(RenderingMode.Points);
            GUI.color = (manager.RenderingMode == RenderingMode.Mesh) ? Color.cyan : Color.white;
            if (GUILayout.Button("Mesh")) manager.SwitchRenderingMode(RenderingMode.Mesh);
            GUI.color = (manager.RenderingMode == RenderingMode.RoomScan) ? Color.cyan : Color.white;
            if (GUILayout.Button("RoomScan")) manager.SwitchRenderingMode(RenderingMode.RoomScan);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            // Toggles
            GUILayout.BeginHorizontal();
            GUI.color = manager.FlipY ? Color.yellow : Color.white;
            if (GUILayout.Button(manager.FlipY ? "Flip: On" : "Flip: Off")) manager.FlipY = !manager.FlipY;
            GUI.color = manager.UseDevicePose ? Color.yellow : Color.white;
            if (GUILayout.Button(manager.UseDevicePose ? "Pose: On" : "Pose: Off")) manager.UseDevicePose = !manager.UseDevicePose;
            GUI.color = manager.EnableDithering ? Color.cyan : Color.white;
            if (GUILayout.Button(manager.EnableDithering ? "Dither: On" : "Dither: Off")) manager.EnableDithering = !manager.EnableDithering;
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            // Operations
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Focus")) PlayCam.Instance?.Focus();
            GUI.color = new Color(0.2f, 1f, 0.6f);
            if (GUILayout.Button("Recenter")) manager.RecenterOrigin();
            GUI.color = Color.white;
            bool isConnectedGame = manager.Receiver != null && manager.Receiver.IsConnected;
            GUI.color = isConnectedGame ? Color.yellow : Color.white;
            if (GUILayout.Button(isConnectedGame ? "Discon" : "Connect"))
            {
                if (isConnectedGame) manager.Disconnect();
                else manager.Connect();
            }
            GUI.color = manager.IsPipelineRunning ? Color.green : Color.red;
            if (GUILayout.Button(manager.IsPipelineRunning ? "Pipe: ON" : "Pipe: OFF")) manager.TogglePipeline();
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            // 2D Radar HUD
            var meta = manager.LatestMetadata;
            manager.CoordinateVisualizer?.DrawGUI(meta.cameraPosition, meta.cameraRotation, manager.UseDevicePose);
        }
    }
}
#endif
