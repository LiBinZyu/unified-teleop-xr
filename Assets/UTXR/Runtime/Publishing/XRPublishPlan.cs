using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace UTXR.Publishing
{
    /// <summary>One capture factory, shared by all routes reading that source.</summary>
    public sealed class XRPublishSource
    {
        public string Name { get; }
        private readonly Func<IXRDataCapture> _open;
        public XRPublishSource(string name, Func<IXRDataCapture> open)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A source name is required.", nameof(name));
            Name = name;
            _open = open ?? throw new ArgumentNullException(nameof(open));
        }
        internal IXRDataCapture Open() => _open() ?? throw new InvalidOperationException("Capture factory returned null.");
    }

    public sealed class XRPublishRoute
    {
        public XRPublishSource Source { get; }
        public XRPublishChannel Channel { get; }
        public string Inputs { get; }
        private readonly Func<IXRDataCapture, XRPublishSample?> _read;

        private XRPublishRoute(XRPublishSource source, XRPublishChannel channel, string inputs, Func<IXRDataCapture, XRPublishSample?> read)
        { Source = source ?? throw new ArgumentNullException(nameof(source)); Channel = channel; Inputs = inputs; _read = read; }

        public static XRPublishRoute Create<T>(XRPublishSource source, XRPublishChannel channel, string inputs, Func<T, XRPublishSample?> read) where T : IXRDataCapture
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            return new XRPublishRoute(source, channel, inputs, capture => read((T)capture));
        }
        internal XRPublishSample? Read(IXRDataCapture capture) => _read(capture);
    }

    /// <summary>Frozen route membership. Sources are acquired once by identity; no mode logic lives here.</summary>
    public sealed class XRPublishPlan
    {
        public ReadOnlyCollection<XRPublishRoute> Routes { get; }
        public XRPublishPlan(IEnumerable<XRPublishRoute> routes)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            var copy = new List<XRPublishRoute>();
            var topics = new HashSet<string>(StringComparer.Ordinal);
            foreach (var route in routes)
            {
                if (route == null || string.IsNullOrWhiteSpace(route.Channel.Topic)) throw new ArgumentException("Invalid route.");
                if (!topics.Add(route.Channel.Topic)) throw new InvalidOperationException($"Duplicate publishing topic '{route.Channel.Topic}'.");
                copy.Add(route);
            }
            Routes = copy.AsReadOnly();
        }
    }
}
