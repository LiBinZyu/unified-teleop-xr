using UnityEditor;

namespace Record3D.Editor
{
    /// <summary>
    /// Unity tears XR down very early while leaving Play Mode. Release Record3D's WebRTC
    /// connection at the first editor notification so native video callbacks cannot overlap
    /// OpenXR and WebRTC plugin unloading.
    /// </summary>
    [InitializeOnLoad]
    internal static class Record3DPlayModeGuard
    {
        static Record3DPlayModeGuard()
        {
            EditorApplication.update -= MonitorPendingPlayModeExit;
            EditorApplication.update += MonitorPendingPlayModeExit;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void MonitorPendingPlayModeExit()
        {
            // isPlayingOrWillChangePlaymode turns false as soon as Stop is requested. This runs
            // before the playModeStateChanged chain where XR begins native subsystem teardown.
            if (EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Record3DManager.PrepareForEditorPlayModeExit();
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Record3DManager.PrepareForEditorPlayModeExit();
            }
        }
    }
}
