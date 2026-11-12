using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CycloneDDS;
using UTXR.Devices;
using UnityEngine;

namespace UTXR.Publishing
{
    /// <summary>Owned DDS runtime and writers. Does not sample inputs or select XR modes.</summary>
    public sealed class XRCycloneDdsTransport : IXRPublishTransport
    {
        private readonly int _domainId;
        private readonly string _configurationXml;
        private readonly List<(XRPublishChannel Channel, int Handle)> _writers = new();
        private bool _ownsRuntime, _disposed;
        private readonly Dictionary<int, JoyBuffer> _joy = new();

        public XRCycloneDdsTransport(int domainId, string configurationXml)
        {
            if (domainId < 0) throw new ArgumentOutOfRangeException(nameof(domainId));
            if (string.IsNullOrWhiteSpace(configurationXml)) throw new ArgumentException("Provide explicit CycloneDDS XML configuration.", nameof(configurationXml));
            _domainId = domainId;
            _configurationXml = configurationXml;
        }

        public void Open(IReadOnlyList<XRPublishChannel> channels)
        {
            if (_disposed || _ownsRuntime) throw new InvalidOperationException("This DDS transport cannot be opened again.");
            if (channels.Count == 0) return; // All message switches off: acquire no DDS runtime or writers.
            if (CddsNative.cdds_is_initialized())
                throw new InvalidOperationException("DDS is already owned by another publisher. Stop the legacy DdsPublisher before starting this router.");
            Check(CddsNative.cdds_init(_domainId, _configurationXml), "Initialize DDS");
            _ownsRuntime = true;
            foreach (var channel in channels)
            {
                int handle = CddsNative.cdds_create_publisher(channel.Topic, channel.Type, channel.Qos);
                if (handle <= 0) throw new InvalidOperationException($"Create writer '{channel.Topic}' failed: {handle}.");
                _writers.Add((channel, handle));
                if (channel.Type == DdsMessageType.SensorJoy) _joy.Add(_writers.Count - 1, new JoyBuffer(channel.FrameId));
            }
        }

        public void Write(int channelIndex, XRPublishSample sample, long timestampNs)
        {
            if (!_ownsRuntime || _disposed) throw new InvalidOperationException("DDS transport is not open.");
            var (channel, handle) = _writers[channelIndex];
            if (channel.Type == DdsMessageType.SensorJoy)
            {
                int joyResult = _joy[channelIndex].Write(handle, sample, timestampNs);
                Check(joyResult, "Publish Joy"); return;
            }
            var vector = sample.Vector;
            var rotation = sample.Rotation;
            if (!XRDeviceStateCore.IsFinite(vector) || !XRDeviceStateCore.IsFinite(sample.Angular) || !XRDeviceStateCore.IsFinite(rotation) || !XRDeviceStateCore.IsFinite(sample.Scalar))
                throw new InvalidOperationException($"Channel '{channel.Topic}' supplied a non-finite measurement.");
            double x = vector.x, y = vector.y, z = vector.z;
            double qx = rotation.x, qy = rotation.y, qz = rotation.z, qw = rotation.w;
            if (channel.ConvertCoordinates)
                DdsConfig.UnityToRos(vector, rotation, out x, out y, out z, out qx, out qy, out qz, out qw);
            double ax = sample.Angular.x, ay = sample.Angular.y, az = sample.Angular.z;
            if (channel.ConvertCoordinates) DdsConfig.UnityToRos(sample.Angular, out ax, out ay, out az);
            int result = channel.Type switch
            {
                DdsMessageType.GeometryPointStamped => CddsNative.cdds_publish_point_stamped(handle, timestampNs, channel.FrameId, x, y, z),
                DdsMessageType.GeometryVector3Stamped => CddsNative.cdds_publish_vector3_stamped(handle, timestampNs, channel.FrameId, x, y, z),
                DdsMessageType.GeometryQuaternionStamped => CddsNative.cdds_publish_quaternion_stamped(handle, timestampNs, channel.FrameId, qx, qy, qz, qw),
                DdsMessageType.GeometryPoseStamped => CddsNative.cdds_publish_pose_stamped(handle, timestampNs, channel.FrameId, x, y, z, qx, qy, qz, qw),
                DdsMessageType.GeometryTwist => channel.ConvertCoordinates
                    ? CddsNative.cdds_publish_twist(handle, vector.y, -vector.x, 0, 0, 0, 0)
                    : CddsNative.cdds_publish_twist(handle, vector.x, vector.y, 0, 0, 0, 0),
                DdsMessageType.GeometryTwistStamped => CddsNative.cdds_publish_twist_stamped(handle, timestampNs, channel.FrameId, x, y, z, ax, ay, az),
                DdsMessageType.StdUInt32 => CddsNative.cdds_publish_uint32(handle, unchecked((uint)sample.Integer)),
                DdsMessageType.StdFloat32 => CddsNative.cdds_publish_float(handle, sample.Scalar),
                DdsMessageType.StdBool => CddsNative.cdds_publish_bool(handle, sample.Integer != 0),
                DdsMessageType.StdInt32 => CddsNative.cdds_publish_int32(handle, sample.Integer),
                DdsMessageType.StdFloat32MultiArray => CddsNative.cdds_publish_float32_multi_array(handle, sample.Array),
                _ => throw new InvalidOperationException($"Unsupported publish type: {channel.Type}.")
            };
            if (result < 0) throw new InvalidOperationException($"Publish '{channel.Topic}' failed: DDS error {result}.");
        }

        private static void Check(int result, string operation)
        {
            if (result < 0) throw new InvalidOperationException($"{operation} failed: DDS error {result}.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                foreach (var writer in _writers) CddsNative.cdds_destroy_entity(writer.Handle);
            }
            finally
            {
                foreach (var buffer in _joy.Values) buffer.Dispose();
                _joy.Clear();
                _writers.Clear();
                if (_ownsRuntime) { _ownsRuntime = false; CddsNative.cdds_shutdown(); }
            }
        }
        // Publish-only CDR sample view. All nested memory is ours; DDS borrows it during Write.
        private sealed class JoyBuffer : IDisposable
        {
            [StructLayout(LayoutKind.Sequential)] private struct Header { public DdsTime stamp; public IntPtr frame; }
            [StructLayout(LayoutKind.Sequential)] private struct Joy { public Header header; public DdsSequence axes, buttons; }
            private IntPtr _sample, _frame, _axes, _buttons;
            private int _axisCount = -1, _buttonCount;
            public JoyBuffer(string frame)
            {
                if (string.IsNullOrWhiteSpace(frame)) throw new ArgumentException("Joy requires a frame ID.");
                try
                {
                    _sample = Marshal.AllocHGlobal(Marshal.SizeOf<Joy>());
                    _frame = Marshal.StringToCoTaskMemUTF8(frame);
                }
                catch { Dispose(); throw; }
            }
            public int Write(int handle, XRPublishSample sample, long timestamp)
            {
                var axes = sample.Array ?? throw new InvalidOperationException("Joy axes are missing.");
                var buttons = sample.Buttons ?? throw new InvalidOperationException("Joy buttons are missing.");
                foreach (float value in axes) if (!XRDeviceStateCore.IsFinite(value)) throw new InvalidOperationException("Non-finite Joy axis.");
                if (_axisCount < 0)
                {
                    _axisCount = axes.Length; _buttonCount = buttons.Length;
                    _axes = Marshal.AllocHGlobal(axes.Length * sizeof(float));
                    _buttons = Marshal.AllocHGlobal(buttons.Length * sizeof(int));
                }
                if (axes.Length != _axisCount || buttons.Length != _buttonCount) throw new InvalidOperationException("Joy shape changed during publishing.");
                Marshal.Copy(axes, 0, _axes, axes.Length); Marshal.Copy(buttons, 0, _buttons, buttons.Length);
                var view = new Joy
                {
                    header = new Header { stamp = new DdsTime((int)(timestamp / 1000000000L), (uint)(timestamp % 1000000000L)), frame = _frame },
                    axes = new DdsSequence { maximum = (uint)_axisCount, length = (uint)_axisCount, buffer = _axes },
                    buttons = new DdsSequence { maximum = (uint)_buttonCount, length = (uint)_buttonCount, buffer = _buttons }
                };
                Marshal.StructureToPtr(view, _sample, false);
                return CddsNative.cdds_publish_raw(handle, _sample);
            }
            public void Dispose()
            {
                Marshal.FreeHGlobal(_axes); Marshal.FreeHGlobal(_buttons); Marshal.FreeHGlobal(_sample); Marshal.FreeCoTaskMem(_frame);
                _axes = _buttons = _sample = _frame = IntPtr.Zero;
            }
        }
    }
}
