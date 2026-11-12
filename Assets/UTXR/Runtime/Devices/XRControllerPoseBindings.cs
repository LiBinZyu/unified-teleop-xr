using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace UTXR.Devices
{
    [System.Flags]
    public enum XRControllerBindingProfiles
    {
        None = 0,
        MetaQuestTouchPlus = 1 << 0,
        MetaQuestTouch = 1 << 1,
        MetaQuestTouchPro = 1 << 2,
        Pico4 = 1 << 3,
        Pico4Ultra = 1 << 4,
        HTCVive = 1 << 5
    }

    /// <summary>Writes explicit controller Aim pose bindings in the editor. No runtime mode management.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UTXR/Devices/XR Controller Pose Bindings")]
    public sealed class XRControllerPoseBindings : MonoBehaviour
    {
        [SerializeField] private TrackedPoseDriver leftController;
        [SerializeField] private TrackedPoseDriver rightController;

        [SerializeField] private XRControllerBindingProfiles controllerProfiles =
            XRControllerBindingProfiles.MetaQuestTouch | XRControllerBindingProfiles.MetaQuestTouchPlus | XRControllerBindingProfiles.MetaQuestTouchPro;

#if UNITY_EDITOR
        /// <summary>Replaces the targets' three pose actions with inline bindings. Supports Undo and prefab overrides.</summary>
        [ContextMenu("Apply Controller Pose Bindings")]
        public void ApplyBindings()
        {
            if (Application.isPlaying)
                throw new System.InvalidOperationException("Apply controller pose bindings outside Play Mode, then save the scene or prefab.");
            if (leftController == null || rightController == null || leftController == rightController)
                throw new System.InvalidOperationException("Assign two distinct left and right TrackedPoseDriver targets.");
            var profiles = System.Enum.GetValues(typeof(XRControllerBindingProfiles)).Cast<XRControllerBindingProfiles>()
                .Where(profile => profile != XRControllerBindingProfiles.None).ToArray();
            var supported = profiles.Aggregate(XRControllerBindingProfiles.None, (mask, profile) => mask | profile);
            // Unity's flags dropdown represents Everything as -1.
            if (controllerProfiles == XRControllerBindingProfiles.None ||
                (controllerProfiles != (XRControllerBindingProfiles)(-1) && (controllerProfiles & ~supported) != 0))
                throw new System.InvalidOperationException("Select at least one valid controller binding profile.");

            var paths = profiles.Where(profile => (controllerProfiles & profile) != 0).Select(GetPaths).ToArray();
            UnityEditor.Undo.RecordObjects(new Object[] { leftController, rightController }, "Apply Controller Pose Bindings");
            ApplyTo(leftController, paths.Select(p => p.LeftPosition), paths.Select(p => p.LeftRotation), paths.Select(p => p.LeftTrackingState));
            ApplyTo(rightController, paths.Select(p => p.RightPosition), paths.Select(p => p.RightRotation), paths.Select(p => p.RightTrackingState));
            Debug.Log($"Controller pose bindings applied: {controllerProfiles}. Save the scene or prefab.", this);
        }

        private static (string LeftPosition, string LeftRotation, string LeftTrackingState,
            string RightPosition, string RightRotation, string RightTrackingState) GetPaths(XRControllerBindingProfiles profile)
        {
            // Each profile explicitly owns all six paths. No device detection or substitute bindings.
            return profile switch
            {
                XRControllerBindingProfiles.MetaQuestTouchPlus => (
                    "<QuestTouchPlusController>{LeftHand}/pointerPosition",
                    "<QuestTouchPlusController>{LeftHand}/pointerRotation",
                    "<QuestTouchPlusController>{LeftHand}/trackingState",
                    "<QuestTouchPlusController>{RightHand}/pointerPosition",
                    "<QuestTouchPlusController>{RightHand}/pointerRotation",
                    "<QuestTouchPlusController>{RightHand}/trackingState"),
                XRControllerBindingProfiles.MetaQuestTouch => (
                    "<OculusTouchController>{LeftHand}/pointerPosition",
                    "<OculusTouchController>{LeftHand}/pointerRotation",
                    "<OculusTouchController>{LeftHand}/trackingState",
                    "<OculusTouchController>{RightHand}/pointerPosition",
                    "<OculusTouchController>{RightHand}/pointerRotation",
                    "<OculusTouchController>{RightHand}/trackingState"),
                XRControllerBindingProfiles.MetaQuestTouchPro => (
                    "<QuestProTouchController>{LeftHand}/pointerPosition",
                    "<QuestProTouchController>{LeftHand}/pointerRotation",
                    "<QuestProTouchController>{LeftHand}/trackingState",
                    "<QuestProTouchController>{RightHand}/pointerPosition",
                    "<QuestProTouchController>{RightHand}/pointerRotation",
                    "<QuestProTouchController>{RightHand}/trackingState"),
                XRControllerBindingProfiles.Pico4 => (
                    "<PICO4TouchController>{LeftHand}/pointerPosition",
                    "<PICO4TouchController>{LeftHand}/pointerRotation",
                    "<PICO4TouchController>{LeftHand}/trackingState",
                    "<PICO4TouchController>{RightHand}/pointerPosition",
                    "<PICO4TouchController>{RightHand}/pointerRotation",
                    "<PICO4TouchController>{RightHand}/trackingState"),
                XRControllerBindingProfiles.Pico4Ultra => (
                    "<PICO4UltraController>{LeftHand}/pointerPosition",
                    "<PICO4UltraController>{LeftHand}/pointerRotation",
                    "<PICO4UltraController>{LeftHand}/trackingState",
                    "<PICO4UltraController>{RightHand}/pointerPosition",
                    "<PICO4UltraController>{RightHand}/pointerRotation",
                    "<PICO4UltraController>{RightHand}/trackingState"),
                XRControllerBindingProfiles.HTCVive => (
                    "<ViveController>{LeftHand}/pointerPosition",
                    "<ViveController>{LeftHand}/pointerRotation",
                    "<ViveController>{LeftHand}/trackingState",
                    "<ViveController>{RightHand}/pointerPosition",
                    "<ViveController>{RightHand}/pointerRotation",
                    "<ViveController>{RightHand}/trackingState"),
                _ => throw new System.ArgumentOutOfRangeException(nameof(profile), profile, "Unknown controller binding profile.")
            };
        }

        private static void ApplyTo(TrackedPoseDriver target, IEnumerable<string> position, IEnumerable<string> rotation, IEnumerable<string> trackingState)
        {
            target.positionInput = CreateAction("Position", "Vector3", position);
            target.rotationInput = CreateAction("Rotation", "Quaternion", rotation);
            target.trackingStateInput = CreateAction("Tracking State", "Integer", trackingState);
            target.ignoreTrackingState = false;
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            UnityEditor.EditorUtility.SetDirty(target);
        }

        private static InputActionProperty CreateAction(string name, string controlType, IEnumerable<string> paths)
        {
            var action = new InputAction(name, UnityEngine.InputSystem.InputActionType.Value, expectedControlType: controlType);
            foreach (string path in paths) action.AddBinding(path);
            return new InputActionProperty(action);
        }
#endif
    }
}
