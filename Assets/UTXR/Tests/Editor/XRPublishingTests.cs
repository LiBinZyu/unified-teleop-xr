using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using CycloneDDS;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using UTXR.UI;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR.Hands;
using UTXR.Devices;
using UTXR.Publishing;

namespace UTXR.Tests.Editor
{
    public static class XRPublishingTests
    {
        private static int _assertions;
        [MenuItem("UTXR/Tests/Run XR Publishing Tests")]
        public static void Run()
        {
            _assertions = 0;
            _assertions += XRPublishSessionContractTests.Run();
            _assertions += XRPublishEncodingContractTests.Run();
            _assertions += XRPublishConnectionTests.Run();
            ActionsAreBorrowedConfiguration();
            MessageSwitchesAndUI();
            JointFreshnessAndInvalidation();
            LoopbackTransport();
            UnityEngine.Debug.Log($"[XRPublishingTests] PASS: session contracts + 4 integration scenarios, {_assertions} assertions.");
        }

        public static void RunBatch()
        {
            try { XRDeviceStateContractTests.Run(); Run(); EditorApplication.Exit(0); }
            catch (Exception error) { UnityEngine.Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void ActionsAreBorrowedConfiguration()
        {
            var fixture = new GameObject("Publishing Action Test");
            var source = fixture.AddComponent<XRInputActionDataSource>();
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Test"); asset.AddActionMap(map);
            var button = map.AddAction("Button", UnityEngine.InputSystem.InputActionType.Button, "<Gamepad>/buttonSouth");
            var reference = InputActionReference.Create(button);
            var gamepad = InputSystem.AddDevice<Gamepad>();
            var manager = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var editUpdates = manager.GetType().GetProperty("runPlayerUpdatesInEditMode");
            bool previous = (bool)editUpdates.GetValue(manager); editUpdates.SetValue(manager, true);
            try
            {

                var input = new XRActionInput(reference, XRActionValueKind.Button);
                using var capture = source.OpenCapture(new[] { input, input });
                Check(!button.enabled, "Capture does not enable borrowed actions.");
                Expect<InvalidOperationException>(() => capture.Read(input));
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South)); InputSystem.Update();
                capture.Refresh();
                Check(capture.Read(input).Available && capture.Read(input).Integer == 1, "Owned clone reads held state.");
                InputSystem.QueueStateEvent(gamepad, new GamepadState()); InputSystem.Update();
                Check(capture.Read(input).Integer == 1, "Input updates do not mutate a captured tick.");
                capture.Refresh();
                Check(capture.Read(input).Integer == 0, "Next tick reads release as false.");
                capture.Dispose();
                Check(!button.enabled, "Capture disposal never changes borrowed action state.");
                Expect<ObjectDisposedException>(() => capture.Refresh());
                Expect<InvalidOperationException>(() => source.OpenCapture(new[] { input, new XRActionInput(reference, XRActionValueKind.Float) }));
                var configuration = fixture.AddComponent<XRPublishConfiguration>();
                Expect<ArgumentException>(() => configuration.ValidateReferences());
                foreach (var field in typeof(XRPublishConfiguration).GetFields())
                    if (field.FieldType == typeof(InputActionReference)) field.SetValue(configuration, reference);
                configuration.ValidateReferences();

                configuration.actionSource = source;
                configuration.handSource = fixture.AddComponent<XRHandJointDataSource>();
                var controllerPlan = configuration.CreatePlan(XRInputMode.Controller);
                var handPlan = configuration.CreatePlan(XRInputMode.Hand);
                Check(controllerPlan.Routes.Count == 12 && handPlan.Routes.Count == 11, "Mode selection composes explicit message sets.");
                Check(controllerPlan.Routes[11].Channel.Type == DdsMessageType.SensorJoy, "Controller input uses standard Joy.");
                foreach (var route in controllerPlan.Routes)
                    Check(ReferenceEquals(route.Source, controllerPlan.Routes[0].Source), "All Action routes share one raw capture.");
                foreach (var route in handPlan.Routes)
                    Check(!route.Channel.Topic.Contains("controller"), "Hand plan never includes controller messages.");

                ControllerInputWithMissingDevices(configuration, map, gamepad);
            }
            finally
            {
                map.Dispose();
                if (gamepad.added) InputSystem.RemoveDevice(gamepad);
                editUpdates.SetValue(manager, previous);
                UnityEngine.Object.DestroyImmediate(reference); UnityEngine.Object.DestroyImmediate(asset); UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        private static void ControllerInputWithMissingDevices(XRPublishConfiguration configuration, InputActionMap map, Gamepad left)
        {
            var references = new List<InputActionReference>();
            InputActionReference Bind(string name, string side, string control)
            {
                var reference = InputActionReference.Create(map.AddAction(name, UnityEngine.InputSystem.InputActionType.Value,
                    $"<Gamepad>{{UTXRJoyTest{side}}}/{control}"));
                references.Add(reference);
                return reference;
            }

            Gamepad right = null;
            try
            {
                InputSystem.SetDeviceUsage(left, "UTXRJoyTestLeft");
                foreach (var message in configuration.Messages) message.Option.enabled = false;
                configuration.controllerInputMessage.enabled = true;
                configuration.leftThumbstick = Bind("LeftStick", "Absent", "leftStick");
                configuration.leftTrigger = Bind("LeftTrigger", "Absent", "leftTrigger");
                configuration.leftGrip = Bind("LeftGrip", "Absent", "rightTrigger");
                configuration.rightThumbstick = Bind("RightStick", "Right", "rightStick");
                configuration.rightTrigger = Bind("RightTrigger", "Right", "rightTrigger");
                configuration.rightGrip = Bind("RightGrip", "Right", "leftTrigger");
                configuration.buttonX = Bind("X", "Left", "buttonWest");
                configuration.buttonY = Bind("Y", "Absent", "buttonNorth");
                configuration.buttonA = Bind("A", "Right", "buttonSouth");
                configuration.buttonB = Bind("B", "Right", "buttonEast");
                configuration.leftThumbstickClick = Bind("LeftClick", "Absent", "leftStickPress");
                configuration.rightThumbstickClick = Bind("RightClick", "Right", "rightStickPress");

                var transport = new JoyRecordingTransport();
                using var session = new XRPublishSession();
                session.Start(configuration.CreatePlan(XRInputMode.Controller), transport);
                InputSystem.QueueStateEvent(left, new GamepadState().WithButton(GamepadButton.West));
                InputSystem.Update();
                session.Publish(1);
                Check(transport.Writes == 1 && transport.Axes.Length == 8 && transport.Buttons.Length == 6,
                    "One available Action still publishes the complete Joy layout.");
                Check(transport.Axes[0] == 0 && transport.Axes[4] == 0 && transport.Buttons[0] == 1 && transport.Buttons[2] == 0,
                    "The available button retains its value at the same index.");

                InputSystem.QueueStateEvent(left, new GamepadState());
                InputSystem.Update();
                session.Publish(2);
                Check(transport.Writes == 2 && transport.Buttons[0] == 0, "A released button is still an available input.");

                InputSystem.RemoveDevice(left);
                InputSystem.Update();
                session.Publish(3);
                Check(transport.Writes == 2 && session.IsPublishing, "No available inputs skips the Joy sample and keeps the session open.");

                right = InputSystem.AddDevice<Gamepad>();
                InputSystem.SetDeviceUsage(right, "UTXRJoyTestRight");
                InputSystem.QueueStateEvent(right, new GamepadState { rightStick = new Vector2(.8f, 0f) }.WithButton(GamepadButton.South));
                InputSystem.Update();
                session.Publish(4);
                Check(transport.Writes == 3 && transport.Opens == 1 && session.IsPublishing && transport.Axes[0] == 0 &&
                    transport.Axes[4] > .5f && transport.Buttons[0] == 0 && transport.Buttons[2] == 1,
                    "Right-only input resumes on the same publishing session.");
            }
            finally
            {
                if (right != null && right.added) InputSystem.RemoveDevice(right);
                foreach (var reference in references) UnityEngine.Object.DestroyImmediate(reference);
            }
        }

        private sealed class JoyRecordingTransport : IXRPublishTransport
        {
            public int Opens, Writes;
            public float[] Axes;
            public int[] Buttons;

            public void Open(IReadOnlyList<XRPublishChannel> channels) => Opens++;
            public void Write(int channelIndex, XRPublishSample sample, long timestampNs)
            {
                Writes++;
                Axes = sample.Float32Values.ToArray();
                Buttons = (int[])typeof(XRPublishSample).GetProperty("Buttons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sample);
            }
            public void Dispose() { }
        }

        private static void MessageSwitchesAndUI()
        {
            var fixture = new GameObject("Message switch test");
            var configuration = fixture.AddComponent<XRPublishConfiguration>();
            try
            {
                foreach (var field in typeof(XRPublishConfiguration).GetFields())
                    if (field.FieldType == typeof(XRPublishConfiguration.MessageOption))
                        ((XRPublishConfiguration.MessageOption)field.GetValue(configuration)).enabled = false;
                Check(configuration.CreatePlan(XRInputMode.Controller).Routes.Count == 0 &&
                    configuration.CreatePlan(XRInputMode.Hand).Routes.Count == 0,
                    "All messages disabled requires no Action references, hand subsystem or body source.");
                var container = new GameObject("Static message rows", typeof(RectTransform));
                container.transform.SetParent(fixture.transform);
                var template = new GameObject("Template", typeof(RectTransform), typeof(Toggle));
                template.transform.SetParent(container.transform);
                var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(template.transform);
                var listUI = fixture.AddComponent<XRPublishMessageListUI>();
                listUI.configuration = configuration;
                listUI.itemTemplate = template;
                listUI.GenerateItems();
                Check(container.transform.childCount == configuration.Messages.Count + 1 && !template.activeSelf,
                    "Editor generates one sibling per fixed message and hides the template.");
                listUI.GenerateItems();
                Check(container.transform.childCount == configuration.Messages.Count + 1,
                    "Regeneration replaces owned items instead of duplicating them.");
                Toggle toggle = null;
                for (int i = 0; i < container.transform.childCount; i++)
                {
                    var item = container.transform.GetChild(i);
                    if (item.GetComponentInChildren<TMP_Text>(true).text == "/xr/left/hand_tracking_state")
                        toggle = item.GetComponentInChildren<Toggle>(true);
                }
                Check(toggle != null, "Generated TMP labels identify their corresponding topics.");
                int changes = 0;
                configuration.MessagesChanged += () => changes++;
                void Sync() => typeof(XRPublishConfiguration).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(configuration, null);
                typeof(XRPublishConfiguration).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(configuration, null);
                Sync();
                Check(!toggle.isOn, "Inspector value initializes the UI without sending a user event.");
                toggle.isOn = true; Sync();
                Check(configuration.leftHandStateMessage.enabled && changes > 0, "UI toggle updates message selection.");
                configuration.handSource = fixture.AddComponent<XRHandJointDataSource>();
                var plan = configuration.CreatePlan(XRInputMode.Hand);
                Check(plan.Routes.Count == 1 && plan.Routes[0].Channel.Topic == "rt/xr/left/hand_tracking_state",
                    "Hand state is independent of joint arrays, velocity and Input Actions.");
                Check(configuration.CreatePlan(XRInputMode.Controller).Routes.Count == 0,
                    "Enabling a Hand message cannot enable it in Controller mode.");
                configuration.leftHandStateMessage.enabled = false;
                typeof(XRPublishConfiguration).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(configuration, null);
                Sync();
                Check(!toggle.isOn && configuration.CreatePlan(XRInputMode.Hand).Routes.Count == 0,
                    "Inspector edits update the UI and remove the route.");
                Check(!configuration.bodyPosesMessage.enabled, "Body remains opt-in.");
                int itemCount = container.transform.childCount;
                typeof(XRPublishMessageListUI).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(listUI, null);
                typeof(XRPublishMessageListUI).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(listUI, null);
                Check(container.transform.childCount == itemCount, "Binding an existing UI never spawns rows.");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        private static void JointFreshnessAndInvalidation()
        {
            const string id = "UTXR.Publishing.ContractTest.Hands";
            XRHandSubsystemDescriptor.Register(new XRHandSubsystemDescriptor.Cinfo
            { id = id, providerType = typeof(TestHandProvider), subsystemTypeOverride = typeof(TestHandSubsystem) });
            var descriptors = new List<XRHandSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(descriptors);
            XRHandSubsystem subsystem = null;
            foreach (var descriptor in descriptors) if (descriptor.id == id) subsystem = descriptor.Create();
            var fixture = new GameObject("Publishing Hand Test");
            var source = fixture.AddComponent<XRHandJointDataSource>();

            XRHandJointDataSource.Capture capture = null;
            try
            {
                Check(subsystem != null, "Test hand subsystem is available.");
                Expect<InvalidOperationException>(() => source.OpenCapture(subsystem));
                subsystem.Start(); capture = source.OpenCapture(subsystem);
                capture.Refresh();
                Check(!capture.HasFrame, "No pose is invented before the first joint update.");
                TestHandProvider.Flags = XRHandSubsystem.UpdateSuccessFlags.All;
                subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.BeforeRender);
                capture.Refresh(); Check(!capture.HasFrame, "BeforeRender is not a network tick.");
                subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
                capture.Refresh();
                Check(capture.HasFrame && capture.Left.TrackingState == 7, "Fresh raw hand frame contains available root velocity bits.");
                int wrist = XRHandJointID.Wrist.ToIndex();
                Check(capture.Left.JointTrackingStates.Span[wrist] == (int)(XRHandJointTrackingState.Pose | XRHandJointTrackingState.LinearVelocity),
                    "Raw joint tracking flags are preserved.");
                Check(capture.Left.JointPoses.Span[wrist].rotation == Quaternion.identity, "Source returns unconverted Unity poses.");
                Check((capture.Left.TrackingState & 12) != 12, "Unavailable angular velocity is not invented.");
                Check(capture.HasFrame && capture.HasFrame, "Reading a frame does not consume another route's data.");
                capture.Refresh(); Check(!capture.HasFrame, "No fresh Dynamic update means no repeated frame.");
                TestHandProvider.Flags = XRHandSubsystem.UpdateSuccessFlags.None;
                subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic); capture.Refresh();
                Check(capture.HasFrame && capture.Left.TrackingState == 0, "Tracking loss produces explicit invalid raw data.");
                for (int i = 0; i < XRHandFrame.JointCount; ++i)
                    Check(capture.Left.JointTrackingStates.Span[i] == 0, "Invalid frame does not retain joint states.");
                capture.Dispose(); Check(subsystem.running, "Capture never stops a borrowed subsystem.");
                using (var stateOnly = source.OpenCapture(subsystem, XRHandCaptureFeatures.TrackingState, XRHandCaptureFeatures.None))
                {
                    TestHandProvider.Flags = XRHandSubsystem.UpdateSuccessFlags.All;
                    subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic); stateOnly.Refresh();
                    Check(stateOnly.Left.JointPoses.Length == 0 && stateOnly.Right.JointPoses.Length == 0,
                        "State-only and disabled hands allocate no joint buffers.");
                    Check(stateOnly.Left.TrackingState == 3 && stateOnly.Left.LinearVelocity == Vector3.zero && stateOnly.Right.TrackingState == 0,
                        "State-only capture skips velocity and the disabled hand.");
                }
                capture = source.OpenCapture(subsystem); capture.Refresh();
                Check(!capture.HasFrame, "A new capture has no previous pending frame.");
                subsystem.Stop(); Expect<InvalidOperationException>(() => capture.Refresh());
            }
            finally
            {
                capture?.Dispose(); subsystem?.Stop(); subsystem?.Destroy();
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        private static void LoopbackTransport()
        {
            const string xml = "<CycloneDDS><Domain Id=\"any\"><General><Interfaces><NetworkInterface address=\"127.0.0.1\"/></Interfaces><AllowMulticast>false</AllowMulticast></General><Discovery><Peers><Peer Address=\"127.0.0.1\"/></Peers></Discovery></Domain></CycloneDDS>";
            using var transport = new XRCycloneDdsTransport(187, xml);
            var channels = new[]
            {
                new XRPublishChannel("utxr_publish_test_array", DdsMessageType.StdFloat32MultiArray),
                new XRPublishChannel("utxr_publish_test_point", DdsMessageType.GeometryPointStamped, convertCoordinates: true),
                new XRPublishChannel("utxr_publish_test_joy", DdsMessageType.SensorJoy, DdsQosPreset.Default, "xr_tracking"),
                new XRPublishChannel("utxr_publish_test_twist", DdsMessageType.GeometryTwistStamped, convertCoordinates: true)
            };
            transport.Open(channels);
            using (var contender = new XRCycloneDdsTransport(188, xml))
                Expect<InvalidOperationException>(() => contender.Open(channels));
            Check(CddsNative.cdds_is_initialized(), "A rejected second transport never shuts down the active owner.");
            int subscriber = CddsNative.cdds_create_subscriber(channels[0].Topic, channels[0].Type, DdsQosPreset.SensorData);
            int pointSubscriber = CddsNative.cdds_create_subscriber(channels[1].Topic, channels[1].Type, DdsQosPreset.SensorData);
            int joySubscriber = CddsNative.cdds_create_subscriber(channels[2].Topic, channels[2].Type, DdsQosPreset.Default);
            int twistSubscriber = CddsNative.cdds_create_subscriber(channels[3].Topic, channels[3].Type, DdsQosPreset.SensorData);
            try
            {
                Check(subscriber > 0 && pointSubscriber > 0, "Loopback subscribers created.");
                using var received = new DdsRawSample(DdsMessageType.StdFloat32MultiArray);
                using var point = new DdsRawSample(DdsMessageType.GeometryPointStamped);
                using var joy = new DdsRawSample(DdsMessageType.SensorJoy);
                using var twist = new DdsRawSample(DdsMessageType.GeometryTwistStamped);
                WaitUntil(() => CddsNative.cdds_get_matched_publications(subscriber) > 0 &&
                    CddsNative.cdds_get_matched_publications(pointSubscriber) > 0 &&
                    CddsNative.cdds_get_matched_publications(joySubscriber) > 0 && CddsNative.cdds_get_matched_publications(twistSubscriber) > 0);
                var data = new float[186]; data[0] = 1f; data[1] = 1f; data[2] = 65535f; data[3] = 1023f;
                transport.Write(0, new XRPublishSample(array: data), 1234567890);
                transport.Write(1, new XRPublishSample(vector: new Vector3(1, 2, 3)), 1234567890);
                transport.Write(2, new XRPublishSample(array: new float[] { .1f, .2f, .3f, .4f, .5f, .6f, .7f, .8f }, buttons: new[] { 1, 0, 1, 0, 1, 0, 1, 0 }), 1234567890);
                transport.Write(3, new XRPublishSample(vector: new Vector3(1, 2, 3), angular: new Vector3(4, 5, 6)), 1234567890);
                WaitUntil(() => received.Take(subscriber));
                var view = Marshal.PtrToStructure<StdFloat32MultiArray>(received.Pointer);
                var actual = new float[186]; Marshal.Copy(view.data.buffer, actual, 0, actual.Length);
                Check(view.data.length == 186 && actual[2] == 65535f && actual[3] == 1023f, "Array payload and both validity-mask halves survive native DDS.");
                WaitUntil(() => point.Take(pointSubscriber));
                var pointView = Marshal.PtrToStructure<GeometryPointStamped>(point.Pointer);
                Check(pointView.point.x == 3 && pointView.point.y == -1 && pointView.point.z == 2,
                    "The transport applies the configured coordinate conversion exactly once.");
                WaitUntil(() => joy.Take(joySubscriber));
                var joyView = Marshal.PtrToStructure<JoyView>(joy.Pointer);
                var axes = new float[8]; var buttons = new int[8];
                Marshal.Copy(joyView.axes.buffer, axes, 0, 8); Marshal.Copy(joyView.buttons.buffer, buttons, 0, 8);
                Check(joyView.axes.length == 8 && joyView.buttons.length == 8 && axes[7] == .8f && buttons[6] == 1 && buttons[7] == 0, "Standard Joy axes and button arrays survive DDS.");
                Check(Marshal.PtrToStringUTF8(joyView.frame) == "xr_tracking" && joyView.stamp.sec == 1, "Joy carries its configured header.");
                WaitUntil(() => twist.Take(twistSubscriber));
                var velocity = Marshal.PtrToStructure<GeometryTwistStamped>(twist.Pointer);
                Check(velocity.twist.linear.x == 3 && velocity.twist.angular.x == 6 && velocity.twist.angular.y == -4, "Linear and angular velocity are converted as vectors.");
            }
            finally
            {
                CddsNative.cdds_destroy_entity(subscriber);
                CddsNative.cdds_destroy_entity(pointSubscriber);
                CddsNative.cdds_destroy_entity(joySubscriber); CddsNative.cdds_destroy_entity(twistSubscriber);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JoyView { public DdsTime stamp; public IntPtr frame; public DdsSequence axes, buttons; }

        private static void WaitUntil(Func<bool> predicate)
        {
            var timeout = Stopwatch.StartNew();
            while (!predicate())
            {
                if (timeout.ElapsedMilliseconds > 5000) throw new TimeoutException("DDS loopback test timed out.");
                Thread.Sleep(10);
            }
        }

        private static void Check(bool condition, string message)
        { _assertions++; if (!condition) throw new InvalidOperationException("Assertion failed: " + message); }
        private static void Expect<T>(Action action) where T : Exception
        { _assertions++; try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    }
}
