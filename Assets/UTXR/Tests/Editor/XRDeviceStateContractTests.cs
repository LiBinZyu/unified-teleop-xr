using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.ProviderImplementation;
using UTXR.Devices;

namespace UTXR.Tests.Editor
{
    /// <summary>Hardware-independent contract tests. Also runnable in a project without NUnit/Test Framework.</summary>
    public static class XRDeviceStateContractTests
    {
        private static int _assertions;
        [MenuItem("UTXR/Tests/Run XR Device State Contract Tests")]
        public static void Run()
        {
            _assertions = 0;
            SwitchingCommitsSnapshotsAndReleasesExactlyOnce();
            UnavailableModeNeverFallsBackOrRetries();
            ProviderFaultClearsDataAndRequiresExplicitRefresh();
            FactoryContractViolationsAreReported();
            TrackingLossClearsFilterAndGestureHistory();
            PhysicalControllerSelectionExcludesHands();
            RawProviderFactoryAndLifetime();
            HandSubsystemOwnershipAndTrackingValidity();
            SharedProcessingAndGestures();
            LegacyTrackingAndWarningParity();
            Debug.Log($"[XRDeviceStateTests] PASS: 10 scenarios, {_assertions} assertions.");
        }
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void SwitchingCommitsSnapshotsAndReleasesExactlyOnce()
        {
            var created = new List<TestProvider>();
            var session = new XRDeviceStateSession(mode =>
            {
                var provider = new TestProvider(mode);
                created.Add(provider);
                return provider;
            }, XRInputMode.Controller);
            session.RefreshProvider();
            Check(session.GetState(XRSide.Left).LinearVelocity.HasValue, "Controller runtime velocity is available.");
            int modes = 0, sides = 0;
            session.ModeChanged += mode =>
            {
                modes++;
                Check(mode == XRInputMode.Hand && session.GetState(XRSide.Left).IsTracked &&
                    session.GetState(XRSide.Right).IsTracked, "Mode event sees a complete new snapshot.");
            };
            Action<XRSide, XRDeviceState> observe = (_, state) => { sides++; Check(state.IsTracked, "Both new sides are committed."); };
            session.TrackingChanged += observe;
            session.SetMode(XRInputMode.Hand);
            Check(modes == 1 && sides == 2, "Source change is published even when tracking booleans match.");
            Check(created[0].DisposeCount == 1, "Old provider released once.");
            Check(!session.GetState(XRSide.Left).LinearVelocity.HasValue, "Hand mode cannot inherit controller velocity.");
            Check(session.GetState(XRSide.Left).Pose.position == created[1].Position, "New source supplies the pose.");
            Check(session.GetState(XRSide.Left).Gestures == XRGesture.None, "Switching cannot turn a source offset into a swing.");
            session.SetMode(XRInputMode.Hand);
            Check(created.Count == 2 && modes == 1, "Selecting the same mode does not rebind.");
            session.TrackingChanged -= observe;
            // Dispose must publish invalidated snapshots, so use a separate observer for teardown.
            bool disabled = false;
            var teardown = new XRDeviceStateSession(mode => new TestProvider(mode), XRInputMode.Controller);
            teardown.RefreshProvider();
            teardown.TrackingChanged += (_, state) => disabled |= !state.IsTracked && state.Status.Code == XRProviderStatusCode.Disabled;
            teardown.Dispose();
            teardown.Dispose();
            Check(disabled && !teardown.GetState(XRSide.Left).IsTracked, "Dispose invalidates both sides and is idempotent.");
            Check(!teardown.GetState(XRSide.Left).LinearVelocity.HasValue, "No motion survives disposal.");
            Expect<ObjectDisposedException>(() => teardown.SetMode(XRInputMode.Hand));
            session.Dispose();
            session.Dispose();
            Check(created[1].DisposeCount == 1, "Active provider is released once.");
        }

        private static void UnavailableModeNeverFallsBackOrRetries()
        {
            int binds = 0;
            using (var session = new XRDeviceStateSession(mode =>
            {
                binds++;
                return new TestProvider(mode, mode == XRInputMode.Hand ?
                    new XRProviderStatus(XRProviderStatusCode.ProviderMissing, "Hand provider absent") : XRProviderStatus.Ready);
            }, XRInputMode.Controller))
            {
                session.RefreshProvider();
                var status = session.SetMode(XRInputMode.Hand);
                Check(session.Mode == XRInputMode.Hand && status.Code == XRProviderStatusCode.ProviderMissing, "Selection and availability are separate.");
                Check(!session.GetState(XRSide.Left).IsTracked, "Unavailable hand never publishes a controller sample.");
                for (int i = 0; i < 5; i++) session.Sample(.01f);
                session.SetMode(XRInputMode.Hand);
                Check(binds == 2, "No automatic discovery, retry, or same-mode rebind.");
                session.RefreshProvider();
                Check(binds == 3 && session.Mode == XRInputMode.Hand, "Only explicit refresh rebinds.");
                Expect<ArgumentOutOfRangeException>(() => session.SetMode((XRInputMode)42));
                Check(session.Mode == XRInputMode.Hand, "Invalid requests do not change selection.");
            }
        }

        private static void ProviderFaultClearsDataAndRequiresExplicitRefresh()
        {
            int binds = 0;
            TestProvider active = null;
            using (var session = new XRDeviceStateSession(mode =>
            {
                binds++;
                active = new TestProvider(mode);
                return active;
            }, XRInputMode.Controller))
            {
                session.RefreshProvider();
                active.ThrowOnSample = true;
                session.Sample(.01f);
                Check(session.ProviderStatus.Code == XRProviderStatusCode.ProviderFaulted, "Unexpected provider errors are explicit.");
                Check(session.ProviderStatus.Message.Contains("test sampling failure"), "Original error is preserved.");
                Check(active.DisposeCount == 1 && !session.GetState(XRSide.Left).IsTracked, "Fault releases provider and clears tracking.");
                Check(!session.GetState(XRSide.Right).LinearVelocity.HasValue, "Fault clears motion.");
                session.Sample(.01f);
                Check(binds == 1, "Fault does not trigger an automatic retry.");
                session.RefreshProvider();
                Check(binds == 2 && session.ProviderStatus.IsReady, "Explicit refresh recovers.");
            }
        }

        private static void TrackingLossClearsFilterAndGestureHistory()
        {
            var settings = new XRDeviceStateSettings { Filter = FilterMethod.BitwiseDebounce, FrameTolerance = 2 };
            var core = new XRDeviceStateCore(settings);
            var raw = RawPose(Vector3.zero);
            Check(core.Process(raw, .1f).IsTracked, "Legacy debounce accepts the first valid hardware sample.");
            Check(core.Process(raw, .1f).IsTracked, "Stable samples acquire tracking.");
            core.Process(RawPose(Vector3.one), .2f);
            core.Process(RawPose(Vector3.one), .2f);
            core.Process(RawPose(Vector3.zero), .2f);
            var lost = new XRDeviceSample(XRProviderStatus.Ready, InputTrackingState.None, false);
            var lostState = core.Process(lost, .1f);
            Check(!lostState.IsTracked, "Hardware loss immediately invalidates tracking.");
            Check(lostState.IsWarning, "Incomplete hardware pose uses the shared warning hold algorithm.");
            Check(core.Process(RawPose(Vector3.one * 100f), .1f).IsTracked, "Valid hardware tracking reacquires immediately after loss.");
            Check(core.Process(RawPose(Vector3.one * 100f), .1f).Gestures == XRGesture.None, "Reacquisition cannot inherit a combo or previous position.");
            settings.FrameTolerance = 0;
            Expect<ArgumentOutOfRangeException>(() => new XRDeviceStateCore(settings));
        }

        private static void FactoryContractViolationsAreReported()
        {
            var wrong = new TestProvider(XRInputMode.Controller);
            using (var session = new XRDeviceStateSession(_ => wrong, XRInputMode.Hand))
            {
                session.RefreshProvider();
                Check(session.Mode == XRInputMode.Hand && session.ProviderStatus.Code == XRProviderStatusCode.ProviderFaulted,
                    "A provider of the wrong mode is rejected without changing the requested mode.");
                Check(wrong.DisposeCount == 1 && !session.GetState(XRSide.Left).IsTracked,
                    "Invalid factory output is released and cannot leak a sample.");
            }
            using (var session = new XRDeviceStateSession(_ => throw new ArgumentException("invalid explicit binding"), XRInputMode.Hand))
            {
                session.RefreshProvider();
                Check(session.ProviderStatus.Code == XRProviderStatusCode.InvalidConfiguration &&
                    session.ProviderStatus.Message.Contains("invalid explicit binding"), "Configuration errors retain their cause.");
            }
        }

        private static void PhysicalControllerSelectionExcludesHands()
        {
            var select = typeof(XRControllerStateProvider).GetMethod("MatchesCharacteristics", BindingFlags.Static | BindingFlags.NonPublic);
            bool Matches(InputDeviceCharacteristics flags, XRSide side) => (bool)select.Invoke(null, new object[] { flags, side });
            var left = InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.TrackedDevice | InputDeviceCharacteristics.Left;
            var right = InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.TrackedDevice | InputDeviceCharacteristics.Right;
            Check(Matches(left, XRSide.Left) && Matches(right, XRSide.Right), "Physical controllers match their own side.");
            Check(!Matches(left, XRSide.Right) && !Matches(right, XRSide.Left), "Controllers never cross sides.");
            Check(!Matches(left | InputDeviceCharacteristics.HandTracking, XRSide.Left), "Hand tracking cannot impersonate a controller, even when Controller is also set.");
            Check(!Matches(left & ~InputDeviceCharacteristics.Controller, XRSide.Left), "A tracked hand is not a physical controller.");
            Check(!Matches(left | InputDeviceCharacteristics.HeadMounted, XRSide.Left), "HMD tracking cannot supply controller state.");
            Check(!Matches(left | InputDeviceCharacteristics.Right, XRSide.Left), "Ambiguous side flags are rejected.");
            var nameFilter = typeof(XRControllerStateProvider).GetMethod("IsHandTrackingDeviceName", BindingFlags.Static | BindingFlags.NonPublic);
            bool IsHandName(string name) => (bool)nameFilter.Invoke(null, new object[] { name });
            Check(IsHandName("Left CUSTOMHAND device") && IsHandName("PicoHand"), "Known hand names are rejected even if vendor metadata incorrectly marks them as controllers.");
            Check(IsHandName("MetaAimHand") && IsHandName("XRHandDevice"), "Additional Unity hand device names are excluded.");
            Check(!IsHandName("QuestTouchPlusController") && !IsHandName("PICO4TouchController"), "Physical controller names remain eligible.");
            Check(!IsHandName(null) && !IsHandName(string.Empty), "Missing device names leave the characteristics gate in control.");
        }

        private static void RawProviderFactoryAndLifetime()
        {
            var fixture = new GameObject("Raw provider factory test");
            fixture.SetActive(false);
            try
            {
                var monitor = fixture.AddComponent<XRGeneralDeviceStateMonitor>();
                var factory = typeof(XRGeneralDeviceStateMonitor).GetMethod("CreateProvider", BindingFlags.Instance | BindingFlags.NonPublic);
                using (var controller = (IXRDeviceStateProvider)factory.Invoke(monitor, new object[] { XRInputMode.Controller }))
                {
                    Check(controller is XRControllerStateProvider, "Controller mode constructs a raw Unity XR reader without Action configuration.");
                    controller.Sample();
                    Check(controller.Mode == XRInputMode.Controller, "Availability never changes the selected mode.");
                    Expect<ArgumentOutOfRangeException>(() => controller.GetSample((XRSide)42));
                    controller.Dispose(); controller.Dispose();
                    Expect<ObjectDisposedException>(() => controller.Sample());
                }
                using (var hand = (IXRDeviceStateProvider)factory.Invoke(monitor, new object[] { XRInputMode.Hand }))
                    Check(hand is XRHandStateProvider, "Hand mode constructs the XR Hands reader without Action configuration.");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        private static void HandSubsystemOwnershipAndTrackingValidity()
        {
            using (var missing = new XRHandStateProvider(null))
            {
                missing.Sample();
                Check(missing.Status.Code == XRProviderStatusCode.ProviderMissing, "Missing hand subsystem is reported without selecting another source.");
            }
            const string id = "UTXR.DeviceState.ContractTest.Hands";
            var descriptors = new List<XRHandSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(descriptors);
            bool registered = false;
            foreach (var descriptor in descriptors) if (descriptor.id == id) registered = true;
            if (!registered)
            {
                XRHandSubsystemDescriptor.Register(new XRHandSubsystemDescriptor.Cinfo
                {
                    id = id, providerType = typeof(TestHandProvider), subsystemTypeOverride = typeof(TestHandSubsystem)
                });
                SubsystemManager.GetSubsystemDescriptors(descriptors);
            }
            XRHandSubsystem subsystem = null;
            foreach (var descriptor in descriptors) if (descriptor.id == id) subsystem = descriptor.Create();
            Check(subsystem != null, "Test hand subsystem created.");
            try
            {
                using (var provider = new XRHandStateProvider(subsystem))
                {
                    provider.Sample();
                    Check(provider.Status.Code == XRProviderStatusCode.ProviderNotRunning && !subsystem.running, "Reader does not start the subsystem.");
                    subsystem.Start();
                    TestHandProvider.Flags = XRHandSubsystem.UpdateSuccessFlags.All;
                    subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
                    provider.Sample();
                    Check(provider.GetSample(XRSide.Left).IsTracked && provider.GetSample(XRSide.Right).IsTracked, "Current hand root and joint data are tracked.");
                    Check(provider.GetSample(XRSide.Left).LinearVelocity == Vector3.one, "Hand linear velocity comes directly from the wrist joint.");
                    Check((provider.GetSample(XRSide.Left).TrackingState & InputTrackingState.Velocity) != 0, "Velocity validity is preserved.");
                    Check(!provider.GetSample(XRSide.Left).AngularVelocity.HasValue, "Missing angular velocity is unavailable, not synthesized.");
                    TestHandProvider.Flags = XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose | XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose;
                    subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
                    provider.Sample();
                    Check(!provider.GetSample(XRSide.Left).LinearVelocity.HasValue, "No stale wrist velocity when joints were not updated.");
                    TestHandProvider.Flags = XRHandSubsystem.UpdateSuccessFlags.None;
                    subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
                    provider.Sample();
                    Check(provider.Status.IsReady && !provider.GetSample(XRSide.Left).IsTracked, "No fresh hand data is ready but untracked.");
                    Check(!provider.GetSample(XRSide.Left).LinearVelocity.HasValue, "Tracking loss clears velocity.");
                    Check(provider.GetSample(XRSide.Left).TrackingState == InputTrackingState.None, "No stale pose or velocity flags survive tracking loss.");
                    provider.Dispose(); provider.Dispose();
                    Check(subsystem.running, "Disposal never stops the borrowed subsystem.");
                    Expect<ObjectDisposedException>(() => provider.Sample());
                }
            }
            finally { subsystem.Stop(); subsystem.Destroy(); }
        }

        private static XRDeviceSample RawPose(Vector3 position) => new XRDeviceSample(XRProviderStatus.Ready,
            InputTrackingState.Position | InputTrackingState.Rotation, true, new Pose(position, Quaternion.identity));

        private static void SharedProcessingAndGestures()
        {
            foreach (XRInputMode mode in new[] { XRInputMode.Controller, XRInputMode.Hand })
            {
                var provider = new TestProvider(mode) { Position = Vector3.zero, LinearVelocity = null };
                var settings = new XRDeviceStateSettings { SwingSpeedThreshold = 1f, SwingCooldownSeconds = .15f };
                using (var session = new XRDeviceStateSession(_ => provider, mode, settings))
                {
                    session.RefreshProvider();
                    int trackingEvents = 0;
                    session.TrackingChanged += (_, __) => trackingEvents++;
                    // No runtime velocity is needed: both modes use the same position-speed crossings.
                    for (int swing = 1; swing <= 6; ++swing)
                    {
                        provider.Position += Vector3.right * .4f;
                        session.Sample(.2f);
                        var state = session.GetState(XRSide.Left);
                        Check((state.Gestures & XRGesture.Swing) != 0, mode + " detects pose-based swing.");
                        Check(((state.Gestures & XRGesture.TripleSwing) != 0) == (swing % 3 == 0), mode + " counts non-overlapping triples.");
                        Check(state.Pose.position == provider.Position && state.Pose.rotation == Quaternion.identity, "Pose uses the selected raw source.");
                        Check(!state.LinearVelocity.HasValue, "Gesture calculation does not invent runtime velocity.");
                        session.Sample(.05f); // Below speed threshold: re-arm for a new crossing.
                        Check(session.GetState(XRSide.Left).Gestures == XRGesture.None, "Gestures are one-sample pulses.");
                    }
                    Check(trackingEvents == 0, "Pose/gesture changes do not spam tracking notifications.");
                    // Continuous high speed is one swing, not a stream of repeated triggers.
                    provider.Position += Vector3.one;
                    session.Sample(.2f);
                    provider.Position += Vector3.one;
                    session.Sample(.2f);
                    Check(session.GetState(XRSide.Left).Gestures == XRGesture.None, "A sustained threshold crossing cannot retrigger.");
                }

                provider = new TestProvider(mode) { LinearVelocity = Vector3.one * 20f };
                settings = new XRDeviceStateSettings { Filter = FilterMethod.KinematicDrift };
                using (var session = new XRDeviceStateSession(_ => provider, mode, settings))
                {
                    session.RefreshProvider();
                    var state = session.GetState(XRSide.Left);
                    Check(state.Raw.IsTracked && !state.IsTracked && state.IsWarning, mode + " raw tracking is processed by shared kinematic/warning algorithms.");
                    Check(!state.LinearVelocity.HasValue && state.Gestures == XRGesture.None, "Rejected samples do not publish motion or gestures.");
                }
                provider = new TestProvider(mode) { LinearVelocity = null };
                using (var session = new XRDeviceStateSession(_ => provider, mode, settings))
                {
                    session.RefreshProvider();
                    Check(session.ProviderStatus.Code == XRProviderStatusCode.InvalidConfiguration, "Missing required velocity is an explicit error in either mode.");
                }
            }
            var core = new XRDeviceStateCore(new XRDeviceStateSettings());
            var invalid = core.Process(RawPose(new Vector3(float.NaN, 0, 0)), .1f);
            Check(invalid.Status.Code == XRProviderStatusCode.InvalidData, "Shared validation rejects non-finite pose.");
            Check(float.IsNaN(invalid.Raw.Pose.position.x), "Validation preserves the original raw measurement for diagnosis.");
            foreach (XRInputMode mode in new[] { XRInputMode.Controller, XRInputMode.Hand })
            {
                var sentinel = new Vector3(-4.3e8f, 0f, 0f);
                var provider = new TestProvider(mode) { LinearVelocity = sentinel };
                using (var session = new XRDeviceStateSession(_ => provider, mode, new XRDeviceStateSettings { Filter = FilterMethod.None }))
                {
                    session.RefreshProvider();
                    var state = session.GetState(XRSide.Left);
                    Check(state.Status.Code == XRProviderStatusCode.InvalidData && !state.IsTracked,
                        mode + " rejects finite runtime velocity sentinels even with motion filtering disabled.");
                    Check(state.Raw.LinearVelocity == sentinel, "Sentinel rejection preserves raw velocity instead of replacing it with zero.");
                }
            }
            var invalidAngular = core.Process(new XRDeviceSample(XRProviderStatus.Ready,
                InputTrackingState.Position | InputTrackingState.Rotation, true, Pose.identity,
                angularVelocity: new Vector3(0f, 4.3e8f, 0f)), .1f);
            Check(invalidAngular.Status.Code == XRProviderStatusCode.InvalidData && !invalidAngular.IsTracked,
                "Angular velocity sentinels are rejected independently of the optional human-motion filters.");
            var rotationOnly = new XRDeviceSample(XRProviderStatus.Ready, InputTrackingState.Rotation, true, Pose.identity);
            Check(!core.Process(rotationOnly, .1f).IsTracked, "Require6DoF is decided after raw sampling.");
            core = new XRDeviceStateCore(new XRDeviceStateSettings { Require6DoF = false, WarningHoldSeconds = .3f });
            Check(core.Process(rotationOnly, .1f).IsWarning, "Partial tracking starts warning hold.");
            Check(core.Process(RawPose(Vector3.zero), .1f).IsWarning, "Warning hold survives a good sample.");
            Check(core.Process(RawPose(Vector3.zero), .3f).IsWarning, "Legacy warning remains visible on the frame that consumes its final hold time.");
            Check(!core.Process(RawPose(Vector3.zero), 0f).IsWarning, "Warning clears on the next sample after its hold expires.");
            Expect<ArgumentOutOfRangeException>(() => core.Process(RawPose(Vector3.zero), -1f));
        }

        private static void LegacyTrackingAndWarningParity()
        {
            foreach (XRInputMode mode in new[] { XRInputMode.Controller, XRInputMode.Hand })
            foreach (FilterMethod filter in new[] { FilterMethod.KinematicDrift, FilterMethod.Combined })
            {
                var provider = new TestProvider(mode) { Position = Vector3.zero, LinearVelocity = Vector3.zero };
                using (var session = new XRDeviceStateSession(_ => provider, mode, new XRDeviceStateSettings { Filter = filter }))
                {
                    session.RefreshProvider();
                    var state = session.GetState(XRSide.Left);
                    Check(state.IsTracked && !state.IsWarning, "Valid hardware tracking is accepted immediately, including Combined.");
                    session.Sample(.05f);
                    session.Sample(.05f);
                    Check(session.GetState(XRSide.Left).IsTracked, "Stationary tracking is accepted through the 0.1 second tolerance.");
                    session.Sample(.01f);
                    state = session.GetState(XRSide.Left);
                    Check(state.Raw.IsTracked && !state.IsTracked && state.IsWarning,
                        mode + " " + filter + " warns for stationary runtime velocity even while hardware tracking stays valid.");
                    Check(state.Raw.LinearVelocity == Vector3.zero && !state.LinearVelocity.HasValue,
                        "Freeze rejection preserves the raw zero velocity and blocks processed motion.");
                    session.Sample(3f);
                    Check(session.GetState(XRSide.Left).IsWarning, "Ongoing freeze refreshes warning even across a long frame.");
                    session.Sample(.01f); // A subsequent rejected frame refreshes the legacy hold timer.
                    provider.LinearVelocity = Vector3.right * .001f;
                    session.Sample(.01f);
                    state = session.GetState(XRSide.Left);
                    Check(state.IsTracked && state.IsWarning, "Speed at the minimum threshold recovers tracking while warning is held.");
                    session.Sample(2f);
                    Check(session.GetState(XRSide.Left).IsWarning, "The last warning frame matches the legacy timer.");
                    session.Sample(0f);
                    Check(!session.GetState(XRSide.Left).IsWarning, "Warning clears after recovery and the two-second hold.");
                    provider.LinearVelocity = Vector3.zero;
                    session.Sample(.05f);
                    Check(session.GetState(XRSide.Left).IsTracked, "Recovery resets the freeze timer.");
                    session.Sample(.06f);
                    Check(session.GetState(XRSide.Left).IsWarning && !session.GetState(XRSide.Left).IsTracked,
                        "A new freeze is detected after its own tolerance, independently of the previous freeze.");
                }
            }

            foreach (FilterMethod filter in new[] { FilterMethod.None, FilterMethod.BitwiseDebounce })
            {
                var core = new XRDeviceStateCore(new XRDeviceStateSettings { Filter = filter });
                Check(core.Process(RawPose(Vector3.zero), 1f).IsTracked,
                    filter + " does not require velocity or apply a stationary filter, matching the legacy selection.");
                var invalidHardware = new XRDeviceSample(XRProviderStatus.Ready,
                    InputTrackingState.Position | InputTrackingState.Rotation, false, Pose.identity);
                var state = core.Process(invalidHardware, .01f);
                Check(!state.IsTracked && state.IsWarning, "Hardware IsTracked=false must warn even when pose bits remain valid.");
            }

            var kinematic = new XRDeviceStateCore(new XRDeviceStateSettings { Filter = FilterMethod.KinematicDrift });
            XRDeviceSample Moving(float speed, Vector3 position) => new XRDeviceSample(XRProviderStatus.Ready,
                InputTrackingState.Position | InputTrackingState.Rotation, true,
                new Pose(position, Quaternion.identity), Vector3.right * speed);
            Check(kinematic.Process(Moving(8f, Vector3.zero), 0f).IsTracked, "Speed at 8 m/s is accepted.");
            var rejected = kinematic.Process(Moving(8.01f, Vector3.zero), .01f);
            Check(!rejected.IsTracked && rejected.IsWarning, "Speed above 8 m/s rejects tracking and warns.");
            Check(kinematic.Process(Moving(1f, Vector3.right * .04f), .01f).IsTracked,
                "A position change at the 4 m/s limit is accepted.");
            rejected = kinematic.Process(Moving(1f, Vector3.right * .081f), .01f);
            Check(!rejected.IsTracked && rejected.IsWarning, "A position jump above 4 m/s and 1.5 cm warns.");
            Check(kinematic.Process(Moving(1f, Vector3.right * 100f), 1f).IsTracked,
                "Position jump rejection is skipped after a sample gap of at least one second, as in the legacy algorithm.");
            var rotatingInPlace = new XRDeviceSample(XRProviderStatus.Ready,
                InputTrackingState.Position | InputTrackingState.Rotation, true,
                new Pose(Vector3.right * 100f, Quaternion.Euler(0, 30, 0)), Vector3.zero, Vector3.up * 10f);
            rejected = kinematic.Process(rotatingInPlace, .11f);
            Check(!rejected.IsTracked && rejected.IsWarning, "Angular motion does not bypass the legacy linear freeze check.");
        }

        private sealed class TestProvider : IXRDeviceStateProvider
        {
            public XRInputMode Mode { get; }
            public XRProviderStatus Status { get; }
            public int DisposeCount;
            public bool ThrowOnSample;
            public Vector3 Position;
            public Vector3? LinearVelocity;
            public TestProvider(XRInputMode mode) : this(mode, XRProviderStatus.Ready) { }
            public TestProvider(XRInputMode mode, XRProviderStatus status)
            {
                Mode = mode;
                Status = status;
                Position = mode == XRInputMode.Controller ? Vector3.left : Vector3.right;
                LinearVelocity = mode == XRInputMode.Controller ? Vector3.one : (Vector3?)null;
            }
            public void Sample() { if (ThrowOnSample) throw new InvalidOperationException("test sampling failure"); }
            public XRDeviceSample GetSample(XRSide side) => new XRDeviceSample(Status,
                InputTrackingState.Position | InputTrackingState.Rotation, Status.IsReady,
                new Pose(Position, Quaternion.identity), LinearVelocity);
            public void Dispose() { DisposeCount++; }
        }
        private static void Check(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }
        private static void Expect<T>(Action action) where T : Exception
        {
            _assertions++;
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
    }

    public sealed class TestHandSubsystem : XRHandSubsystem { }

    public sealed class TestHandProvider : XRHandSubsystemProvider
    {
        public static XRHandSubsystem.UpdateSuccessFlags Flags;
        public override void Start() { }
        public override void Stop() { }
        public override void Destroy() { }
        public override void GetHandLayout(NativeArray<bool> joints)
        {
            for (int i = 0; i < joints.Length; i++) joints[i] = true;
        }
        public override XRHandSubsystem.UpdateSuccessFlags TryUpdateHands(XRHandSubsystem.UpdateType updateType,
            ref Pose leftRoot, NativeArray<XRHandJoint> leftJoints, ref Pose rightRoot, NativeArray<XRHandJoint> rightJoints)
        {
            leftRoot = rightRoot = Pose.identity;
            leftJoints[XRHandJointID.Wrist.ToIndex()] = XRHandProviderUtility.CreateJoint(Handedness.Left,
                XRHandJointTrackingState.Pose | XRHandJointTrackingState.LinearVelocity,
                XRHandJointID.Wrist, Pose.identity, linearVelocity: Vector3.one);
            rightJoints[XRHandJointID.Wrist.ToIndex()] = XRHandProviderUtility.CreateJoint(Handedness.Right,
                XRHandJointTrackingState.Pose | XRHandJointTrackingState.LinearVelocity,
                XRHandJointID.Wrist, Pose.identity, linearVelocity: Vector3.one);
            return Flags;
        }
    }
}
