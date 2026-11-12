using System;
using CycloneDDS;
using UnityEngine;
using UTXR.Devices;

namespace UTXR.Publishing
{
    /// <summary>Application wire schemas. Encodes raw poses without changing the source or DDS core.</summary>
    internal static class XRPublishEncoding
    {
        public static void Hand(XRHandFrame frame, float[] buffer)
        {
            Array.Clear(buffer, 0, buffer.Length);
            for (int i = 0; i < XRHandFrame.JointCount; ++i)
            {
                int offset = i * 8;
                buffer[offset] = frame.States[i];
                if (frame.PoseValid[i]) Pose(frame.Poses[i], buffer, offset + 1);
            }
        }

        public static void Body(XRBodyFrame frame, float[] buffer)
        {
            Array.Clear(buffer, 0, buffer.Length);
            buffer[0] = 1;
            buffer[1] = frame.IsValid ? 1 : 0;
            buffer[2] = frame.Poses.Length;
            if (!frame.IsValid) return;
            for (int i = 0; i < frame.Poses.Length; ++i)
            {
                int offset = 3 + i * 8;
                buffer[offset] = 1;
                Pose(frame.Poses[i], buffer, offset + 1);
            }
        }

        private static void Pose(Pose pose, float[] buffer, int offset)
        {
            if (!XRDeviceStateCore.IsFinite(pose.position) || !XRDeviceStateCore.IsFinite(pose.rotation))
                throw new InvalidOperationException($"Non-finite pose at record offset {offset}.");
            DdsConfig.UnityToRos(pose.position, pose.rotation, out var x, out var y, out var z, out var qx, out var qy, out var qz, out var qw);
            buffer[offset] = (float)x; buffer[offset + 1] = (float)y; buffer[offset + 2] = (float)z;
            buffer[offset + 3] = (float)qx; buffer[offset + 4] = (float)qy; buffer[offset + 5] = (float)qz; buffer[offset + 6] = (float)qw;
        }
    }
}
