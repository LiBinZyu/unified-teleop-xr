using System;
using System.Collections.Generic;

namespace UTXR.Publishing
{
    /// <summary>Owns one transport and the captures acquired for a fixed plan. Main thread only.</summary>
    public sealed class XRPublishSession : IDisposable
    {
        private readonly List<(string Name, IXRDataCapture Capture)> _captures = new();
        private IXRDataCapture[] _routeCaptures;
        private XRPublishPlan _plan;
        private IXRPublishTransport _transport;
        private bool _executing;
        public bool IsPublishing { get; private set; }

        public void Start(XRPublishPlan plan, IXRPublishTransport transport)
        {
            if (IsPublishing || _transport != null || _executing) throw new InvalidOperationException("Stop before starting another session.");
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _plan = plan;
            _executing = true;
            try
            {
                var acquired = new Dictionary<XRPublishSource, IXRDataCapture>();
                var channels = new XRPublishChannel[plan.Routes.Count];
                _routeCaptures = new IXRDataCapture[channels.Length];
                for (int i = 0; i < channels.Length; ++i)
                {
                    var route = plan.Routes[i];
                    if (!acquired.TryGetValue(route.Source, out var capture))
                    {
                        try { capture = route.Source.Open(); }
                        catch (Exception error) { throw new InvalidOperationException($"Acquire source '{route.Source.Name}' failed.", error); }
                        _captures.Add((route.Source.Name, capture));
                        acquired.Add(route.Source, capture);
                    }
                    channels[i] = route.Channel;
                    _routeCaptures[i] = capture;
                }
                _transport.Open(Array.AsReadOnly(channels));
                IsPublishing = true;
            }
            catch (Exception error) { _executing = false; StopAfterFailure(error); throw; }
            finally { _executing = false; }
        }

        public void Publish(long timestampNs)
        {
            if (!IsPublishing) throw new InvalidOperationException("Start before publishing.");
            if (_executing) throw new InvalidOperationException("Reentrant publishing is not supported.");
            _executing = true;
            try
            {
                foreach (var source in _captures)
                {
                    try { source.Capture.Refresh(); }
                    catch (Exception error) { throw new InvalidOperationException($"Capture source '{source.Name}' failed.", error); }
                }
                for (int i = 0; i < _plan.Routes.Count; ++i)
                {
                    var route = _plan.Routes[i];
                    try
                    {
                        var sample = route.Read(_routeCaptures[i]);
                        if (sample.HasValue) _transport.Write(i, sample.Value, timestampNs);
                    }
                    catch (Exception error) { throw new InvalidOperationException($"Publish '{route.Channel.Topic}' from '{route.Source.Name}' failed. Inputs: {route.Inputs}", error); }
                }
            }
            catch (Exception error) { _executing = false; StopAfterFailure(error); throw; }
            finally { _executing = false; }
        }

        private void StopAfterFailure(Exception cause)
        {
            try { Stop(); }
            catch (Exception cleanup) { throw new AggregateException(cause, cleanup); }
        }

        public void Stop()
        {
            if (_executing) throw new InvalidOperationException("Cannot stop inside a capture or write callback.");
            _executing = true;
            try
            {
                IsPublishing = false;
                var transport = _transport;
                _transport = null;
                _plan = null;
                _routeCaptures = null;
                List<Exception> errors = null;
                // Transport is acquired last: release it before the captures, in reverse acquisition order.
                try { transport?.Dispose(); }
                catch (Exception error) { (errors ??= new()).Add(error); }
                for (int i = _captures.Count - 1; i >= 0; --i)
                {
                    try { _captures[i].Capture.Dispose(); }
                    catch (Exception error) { (errors ??= new()).Add(new InvalidOperationException($"Release source '{_captures[i].Name}' failed.", error)); }
                }
                _captures.Clear();
                if (errors != null) throw new AggregateException("Publishing teardown failed.", errors);
            }
            finally { _executing = false; }
        }
        public void Dispose() => Stop();
    }
}
