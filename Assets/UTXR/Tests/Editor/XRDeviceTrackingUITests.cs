using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UTXR.Devices;
using UTXR.UI;

namespace UTXR.Tests.Editor
{
    public static class XRDeviceTrackingUITests
    {
        private static int assertions;
        private static readonly string[] PanelFields =
        {
            "leftControllerTrackedUI", "leftControllerUntrackedUI", "leftControllerWarningUI",
            "rightControllerTrackedUI", "rightControllerUntrackedUI", "rightControllerWarningUI",
            "leftHandTrackedUI", "leftHandUntrackedUI", "leftHandWarningUI",
            "rightHandTrackedUI", "rightHandUntrackedUI", "rightHandWarningUI"
        };

        [MenuItem("UTXR/Tests/Run XR Tracking UI Tests")]
        public static void Run()
        {
            assertions = 0;
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Tracking UI test fixture");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            XRDeviceStateSession session = null;
            XRDeviceTrackingUIController ui = null;
            bool subscribed = false;
            try
            {
                var monitor = Child(root, "Monitor").AddComponent<XRGeneralDeviceStateMonitor>();
                ui = Child(root, "UI").AddComponent<XRDeviceTrackingUIController>();
                var toggleObject = new GameObject("Mode Toggle", typeof(RectTransform), typeof(Toggle));
                toggleObject.transform.SetParent(root.transform);
                var toggle = toggleObject.GetComponent<Toggle>();
                toggle.SetIsOnWithoutNotify(true);
                Set(ui, "stateMonitor", monitor);
                Set(ui, "modeSwitchToggle", toggle);
                var controllerGroup = Child(root, "Controller UI");
                var handGroup = Child(root, "Hand UI");
                handGroup.SetActive(false);
                var panels = new GameObject[PanelFields.Length];
                for (int i = 0; i < panels.Length; i++)
                {
                    panels[i] = Child(i < 6 ? controllerGroup : handGroup, PanelFields[i]);
                    Set(ui, PanelFields[i], panels[i]);
                }

                int bindings = 0;
                bool handAvailable = true;
                TestProvider current = null;
                session = new XRDeviceStateSession(mode =>
                {
                    bindings++;
                    current = new TestProvider(mode, mode != XRInputMode.Hand || handAvailable);
                    return current;
                }, XRInputMode.Controller, new XRDeviceStateSettings { WarningHoldSeconds = 0f });
                Set(monitor, "_session", session);
                // Relay committed session events through the actual monitor's public interface.
                session.ModeChanged += mode => (Get(monitor, "ModeChanged") as Action<XRInputMode>)?.Invoke(mode);
                session.TrackingChanged += (side, state) =>
                    (Get(monitor, "TrackingChanged") as Action<XRSide, XRDeviceState>)?.Invoke(side, state);
                session.RefreshProvider();
                int startupToggleEvents = 0;
                toggle.onValueChanged.AddListener(_ => startupToggleEvents++);
                Invoke(ui, "OnEnable");
                subscribed = true;
                Check(!toggle.isOn && bindings == 1 && startupToggleEvents == 0, "Startup follows the monitor without changing mode or notifying toggle listeners.");
                AssertPanels(panels, 0, 3);

                toggle.isOn = true;
                Check(session.Mode == XRInputMode.Hand && bindings == 2, "Toggle on selects both hands.");
                AssertPanels(panels, 6, 9);
                ui.UpdateLeftUI(false, true);
                AssertPanels(panels, 8, 9);
                toggle.isOn = false;
                Check(session.Mode == XRInputMode.Controller, "Toggle off selects both controllers.");
                AssertPanels(panels, 0, 3);
                Check((float)Get(ui, "_leftWarningTimer") == 0f, "Mode switch clears old warning timers.");

                int before = bindings;
                session.SetMode(XRInputMode.Hand);
                Check(toggle.isOn && bindings == before + 1, "Code mode changes synchronize the toggle without recursive switching.");
                AssertPanels(panels, 6, 9);
                current.LeftTracked = false;
                session.Sample(.01f);
                Check((bool)Get(ui, "_leftPendingUntracked"), "Ordinary tracking loss still uses the display buffer.");
                toggle.isOn = false;
                Check(!(bool)Get(ui, "_leftPendingUntracked"), "Switching clears pending transitions from the old mode.");
                AssertPanels(panels, 0, 3);

                handAvailable = false;
                toggle.isOn = true;
                Check(toggle.isOn && session.Mode == XRInputMode.Hand && !session.ProviderStatus.IsReady,
                    "Unavailable hands remain selected; controllers are not substituted.");
                AssertPanels(panels, 7, 10);
                Check(controllerGroup.activeSelf && !handGroup.activeSelf, "Mode changes leave group visibility to Inspector bindings.");

                Set(ui, "leftHandTrackedUI", panels[0]);
                bool rejected = false;
                try { Invoke(ui, "ValidateUIReferences"); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Shared state indicator objects are rejected.");
                Set(ui, "leftHandTrackedUI", panels[6]);

                Invoke(ui, "OnDisable");
                subscribed = false;
                AssertPanels(panels);
                toggle.isOn = false;
                Check(session.Mode == XRInputMode.Hand, "Disabled UI removes its toggle listener.");
                session.SetMode(XRInputMode.Controller);
                AssertPanels(panels);
                Check(Get(monitor, "ModeChanged") == null && Get(monitor, "TrackingChanged") == null,
                    "Disabled UI removes source subscriptions.");

                session.SetMode(XRInputMode.Hand);
                Invoke(ui, "OnEnable");
                subscribed = true;
                Check(toggle.isOn, "Re-enabled UI synchronizes to the current mode.");
                AssertPanels(panels, 7, 10);
                Check(controllerGroup.activeSelf && !handGroup.activeSelf, "Re-enabling leaves group visibility unchanged.");
                Debug.Log($"[XRTrackingUITests] PASS: {assertions} assertions.");
            }
            finally
            {
                if (subscribed && ui != null) Invoke(ui, "OnDisable");
                session?.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void AssertPanels(GameObject[] panels, params int[] active)
        {
            for (int i = 0; i < panels.Length; i++)
                Check(panels[i].activeSelf == Array.Exists(active, index => index == i), "Unexpected state indicator: " + PanelFields[i]);
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform);
            return child;
        }

        private static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        private static object Get(object target, string name) => Field(target.GetType(), name).GetValue(target);
        private static void Set(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
        private static void Invoke(object target, string method)
        {
            try { target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null); }
            catch (TargetInvocationException exception) { throw exception.InnerException; }
        }
        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class TestProvider : IXRDeviceStateProvider
        {
            public XRInputMode Mode { get; }
            public XRProviderStatus Status { get; }
            public bool LeftTracked = true;
            public TestProvider(XRInputMode mode, bool available)
            {
                Mode = mode;
                Status = available ? XRProviderStatus.Ready : new XRProviderStatus(XRProviderStatusCode.ProviderMissing, "Test hand provider unavailable.");
            }
            public void Sample() { }
            public XRDeviceSample GetSample(XRSide side) => new XRDeviceSample(Status,
                side == XRSide.Left && !LeftTracked ? InputTrackingState.None : InputTrackingState.Position | InputTrackingState.Rotation,
                Status.IsReady && (side == XRSide.Right || LeftTracked), Pose.identity);
            public void Dispose() { }
        }
    }
}
