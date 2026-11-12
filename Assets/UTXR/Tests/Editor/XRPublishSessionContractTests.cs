using System;
using System.Collections.Generic;
using CycloneDDS;
using UTXR.Publishing;

namespace UTXR.Tests.Editor
{
    /// <summary>Session contracts, executable without an XR runtime or native DDS.</summary>
    public static class XRPublishSessionContractTests
    {
        private static int _assertions;
        public static int Run()
        {
            _assertions = 0;
            using (var empty = new XRPublishSession())
            {
                var transport = new Transport();
                empty.Start(new XRPublishPlan(Array.Empty<XRPublishRoute>()), transport);
                empty.Publish(1);
                Check(empty.IsPublishing && transport.Writes == 0, "All-off selection remains idle without sampling sources.");
            }
            FixedPlanAndSharedSnapshots();
            AcquisitionAndWriteFailures();
            CleanupAndReentrancy();
            return _assertions;
        }

        private static void FixedPlanAndSharedSnapshots()
        {
            var source = new Capture();
            int opens = 0;
            var binding = new XRPublishSource("actions", () => { opens++; return source; });
            var routes = new[] { Route(binding, "head"), Route(binding, "controller") };
            var plan = new XRPublishPlan(routes);
            routes[0] = Route(binding, "changed");
            Check(plan.Routes[0].Channel.Topic == "head", "Caller mutation cannot change plan membership.");
            using var session = new XRPublishSession();
            var transport = new Transport();
            session.Start(plan, transport);
            session.Publish(123);
            Check(opens == 1 && source.Refreshes == 1 && transport.Writes == 2, "Shared source acquired/refreshed once for multiple routes.");
            Check(transport.Values[0] == 1 && transport.Values[1] == 1 && transport.Timestamp == 123, "Routes read the same tick and timestamp.");
            var rejected = new Transport();
            Expect<InvalidOperationException>(() => session.Start(plan, rejected));
            Check(session.IsPublishing && rejected.Disposes == 0, "Rejected Start does not take ownership of caller resources.");
            source.Ready = false;
            session.Publish(124);
            Check(source.Refreshes == 2 && transport.Writes == 2, "Unavailable data is skipped without substitution or replay.");
            session.Stop(); session.Stop();
            Check(source.Disposes == 1 && transport.Disposes == 1, "Stop releases each resource exactly once.");
            Expect<InvalidOperationException>(() => session.Publish(125));
            Expect<InvalidOperationException>(() => new XRPublishPlan(new[] { Route(binding, "same"), Route(binding, "same") }));
        }

        private static void AcquisitionAndWriteFailures()
        {
            using var session = new XRPublishSession();
            var source = new Capture();
            var good = new XRPublishSource("good", () => source);
            var bad = new XRPublishSource("bad", () => throw new InvalidOperationException("acquire"));
            var transport = new Transport();
            var error = Expect<InvalidOperationException>(() => session.Start(new XRPublishPlan(new[] { Route(good, "a"), Route(bad, "b") }), transport));
            Check(error.Message.Contains("bad") && source.Disposes == 1 && transport.Opens == 0 && transport.Disposes == 1,
                "Acquisition failure names its source and releases only successful acquisitions.");

            source = new Capture();
            transport = new Transport { FailOpen = true };
            Expect<InvalidOperationException>(() => session.Start(new XRPublishPlan(new[] { Route(good, "a") }), transport));
            Check(source.Disposes == 1 && transport.Disposes == 1 && !session.IsPublishing, "Partial transport Open rolls back captures.");

            source = new Capture { FailRefresh = true };
            transport = new Transport();
            session.Start(new XRPublishPlan(new[] { Route(good, "a") }), transport);
            error = Expect<InvalidOperationException>(() => session.Publish(1));
            Check(error.Message.Contains("good") && transport.Writes == 0 && source.Disposes == 1 && !session.IsPublishing,
                "Refresh failure stops before any route writes.");

            source = new Capture();
            transport = new Transport { FailWrite = true };
            session.Start(new XRPublishPlan(new[] { Route(good, "problem_topic") }), transport);
            error = Expect<InvalidOperationException>(() => session.Publish(2));
            Check(error.Message.Contains("problem_topic") && source.Disposes == 1 && transport.Disposes == 1 && !session.IsPublishing,
                "Write failure preserves topic context and releases resources.");
        }

        private static void CleanupAndReentrancy()
        {
            using var session = new XRPublishSession();
            var order = new List<string>();
            var a = new Capture { OnDispose = () => order.Add("a") };
            var b = new Capture { OnDispose = () => { order.Add("b"); throw new InvalidOperationException("cleanup"); } };
            var first = new XRPublishSource("a", () => a);
            var second = new XRPublishSource("b", () => b);
            var plan = new XRPublishPlan(new[] { Route(first, "a"), Route(second, "b") });
            session.Start(plan, new Transport { OnDispose = () => order.Add("transport") });
            Expect<AggregateException>(session.Stop);
            Check(string.Join(",", order) == "transport,b,a", "All resources release in reverse order even when one Dispose fails.");
            Check(!session.IsPublishing && a.Disposes == 1 && b.Disposes == 1, "Cleanup failures leave the session stopped.");

            a = new Capture { OnRefresh = () => session.Publish(5) };
            var transport = new Transport();
            session.Start(new XRPublishPlan(new[] { Route(first, "a") }), transport);
            Expect<InvalidOperationException>(() => session.Publish(4));
            Check(!session.IsPublishing && transport.Writes == 0 && a.Disposes == 1, "Reentrant Publish stops without corrupting ownership.");

            a = new Capture();
            var next = new Transport();
            transport = new Transport { OnDispose = () => Expect<InvalidOperationException>(() => session.Start(plan, next)) };
            session.Start(new XRPublishPlan(new[] { Route(first, "a") }), transport);
            session.Stop();
            Check(next.Disposes == 0 && a.Disposes == 1, "Teardown rejects reentrant Start before accepting resources.");
        }

        private static XRPublishRoute Route(XRPublishSource source, string topic) =>
            XRPublishRoute.Create<Capture>(source, new XRPublishChannel(topic, DdsMessageType.StdFloat32), "test raw frame",
                capture => capture.Ready ? new XRPublishSample(scalar: capture.Refreshes) : null);

        private sealed class Capture : IXRDataCapture
        {
            public int Refreshes, Disposes;
            public bool Ready = true, FailRefresh;
            public Action OnRefresh, OnDispose;
            public void Refresh()
            {
                Refreshes++;
                OnRefresh?.Invoke();
                if (FailRefresh) throw new InvalidOperationException("refresh");
            }
            public void Dispose() { Disposes++; OnDispose?.Invoke(); }
        }
        private sealed class Transport : IXRPublishTransport
        {
            public int Opens, Writes, Disposes;
            public long Timestamp;
            public readonly List<float> Values = new();
            public bool FailOpen, FailWrite;
            public Action OnDispose;
            public void Open(IReadOnlyList<XRPublishChannel> channels)
            { Opens++; if (FailOpen) throw new InvalidOperationException("open"); }
            public void Write(int index, XRPublishSample sample, long timestampNs)
            {
                if (FailWrite) throw new InvalidOperationException("write");
                Writes++; Timestamp = timestampNs; Values.Add(sample.Scalar);
            }
            public void Dispose() { Disposes++; OnDispose?.Invoke(); }
        }
        private static void Check(bool condition, string message)
        { _assertions++; if (!condition) throw new InvalidOperationException(message); }
        private static T Expect<T>(Action action) where T : Exception
        {
            _assertions++;
            try { action(); } catch (T error) { return error; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
    }
}
