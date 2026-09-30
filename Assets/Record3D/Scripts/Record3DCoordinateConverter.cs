using System;
using UnityEngine;

namespace Record3D
{
    /// <summary>
    /// Utility for converting Record3D poses (right-handed) to Unity coordinates (left-handed)
    /// and computing pinhole camera unprojection parameters.
    /// </summary>
    public static class Record3DCoordinateConverter
    {
        public static Vector3 ConvertPosition(float tx, float ty, float tz)
        {
            return new Vector3(tx, ty, -tz);
        }

        public static Vector3 ConvertPosition(Vector3 position)
        {
            return new Vector3(position.x, position.y, -position.z);
        }

        public static Quaternion ConvertRotation(float qx, float qy, float qz, float qw)
        {
            return new Quaternion(-qx, -qy, qz, qw);
        }

        public static Quaternion ConvertRotation(Quaternion rotation)
        {
            return new Quaternion(-rotation.x, -rotation.y, rotation.z, rotation.w);
        }

        public static void ConvertPose(
            float[] rawPose,
            out Vector3 position,
            out Quaternion rotation,
            out Matrix4x4 poseMatrix)
        {
            if (rawPose == null || rawPose.Length < 7)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                poseMatrix = Matrix4x4.identity;
                return;
            }

            position = ConvertPosition(rawPose[4], rawPose[5], rawPose[6]);
            rotation = ConvertRotation(rawPose[0], rawPose[1], rawPose[2], rawPose[3]);
            poseMatrix = Matrix4x4.TRS(position, rotation, Vector3.one);
        }

        public static Vector4 ComputeUnprojection(float[] intrinsicMatrix, int currentW, int currentH, int origW, int origH)
        {
            if (origH <= 0) origH = 960;
            float scale = (float)currentH / origH;

            float fx = (intrinsicMatrix != null && intrinsicMatrix.Length >= 1) ? intrinsicMatrix[0] * scale : 713.09f * scale;
            float fy = (intrinsicMatrix != null && intrinsicMatrix.Length >= 5) ? intrinsicMatrix[4] * scale : 713.09f * scale;
            float cx = (intrinsicMatrix != null && intrinsicMatrix.Length >= 7) ? intrinsicMatrix[6] * scale : (currentW * 0.5f);
            float cy = (intrinsicMatrix != null && intrinsicMatrix.Length >= 8) ? intrinsicMatrix[7] * scale : (currentH * 0.5f);

            float ifx = 1.0f / (fx > 1e-4f ? fx : 1.0f);
            float ify = 1.0f / (fy > 1e-4f ? fy : 1.0f);
            float itx = -cx / (fx > 1e-4f ? fx : 1.0f);
            float ity = -cy / (fy > 1e-4f ? fy : 1.0f);

            return new Vector4(ifx, ify, itx, ity);
        }
    }
}
