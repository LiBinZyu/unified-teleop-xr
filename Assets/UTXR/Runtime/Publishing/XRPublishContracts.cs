using System;
using System.Collections.Generic;
using CycloneDDS;
using UnityEngine;

namespace UTXR.Publishing
{
    /// <summary>Immutable channel configuration, captured once when a publishing session starts.</summary>
    public readonly struct XRPublishChannel
    {
        public string Topic { get; }
        public DdsMessageType Type { get; }
        public DdsQosPreset Qos { get; }
        public string FrameId { get; }
        public bool ConvertCoordinates { get; }

        public XRPublishChannel(string topic, DdsMessageType type, DdsQosPreset qos = DdsQosPreset.SensorData,
            string frameId = "map", bool convertCoordinates = false)
        {
            if (string.IsNullOrWhiteSpace(topic)) throw new ArgumentException("A DDS topic is required.", nameof(topic));
            Topic = topic.Trim();
            Type = type;
            Qos = qos;
            FrameId = frameId;
            ConvertCoordinates = convertCoordinates;
        }
    }

    /// <summary>Values use their channel's declared type. Array storage is borrowed until the next publishing tick or session end.</summary>
    public readonly struct XRPublishSample
    {
        public Vector3 Vector { get; }
        public Vector3 Angular { get; }
        public Quaternion Rotation { get; }
        public float Scalar { get; }
        public int Integer { get; }
        internal float[] Array { get; }
        internal int[] Buttons { get; }
        public ReadOnlyMemory<float> Float32Values => Array;

        public XRPublishSample(Vector3 vector = default, Quaternion rotation = default,
            float scalar = 0f, int integer = 0, float[] array = null, Vector3 angular = default, int[] buttons = null)
        {
            Vector = vector;
            Angular = angular;
            Rotation = rotation;
            Scalar = scalar;
            Integer = integer;
            Array = array;
            Buttons = buttons;
        }
    }

    /// <summary>One owned transport per session. Dispose releases only resources acquired by Open.</summary>
    public interface IXRPublishTransport : IDisposable
    {
        void Open(IReadOnlyList<XRPublishChannel> channels);
        void Write(int channelIndex, XRPublishSample sample, long timestampNs);
    }
}
