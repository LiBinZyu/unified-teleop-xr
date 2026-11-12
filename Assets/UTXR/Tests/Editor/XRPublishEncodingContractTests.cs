using System;
using System.Reflection;
using UnityEngine;
using UTXR.Publishing;

namespace UTXR.Tests.Editor
{
    /// <summary>Checks the receiver-facing schemas independently of XR hardware.</summary>
    public static class XRPublishEncodingContractTests
    {
        public static int Run()
        {
            int assertions = 0;
            void Check(bool condition)
            { assertions++; if (!condition) throw new InvalidOperationException("Publishing wire schema mismatch."); }
            var encoding = typeof(XRPublishSample).Assembly.GetType("UTXR.Publishing.XRPublishEncoding", true);
            var hand = new XRHandFrame();
            var poses = Field<Pose[]>(hand, "Poses");
            var states = Field<int[]>(hand, "States");
            var valid = Field<bool[]>(hand, "PoseValid");
            Set(hand, "TrackingState", 15);
            states[0] = 129; valid[0] = true;
            poses[0] = new Pose(new Vector3(1, 2, 3), new Quaternion(0, 0, 0, 1));
            var buffer = new float[208];
            for (int i = 0; i < buffer.Length; ++i) buffer[i] = 77;
            encoding.GetMethod("Hand").Invoke(null, new object[] { hand, buffer });
            Check(buffer.Length == 208 && buffer[0] == 129);
            Check(buffer[1] == 3 && buffer[2] == -1 && buffer[3] == 2 && buffer[7] == 1);
            for (int i = 8; i < buffer.Length; ++i) Check(buffer[i] == 0);
            // Encoding must not mutate raw measurements.
            Check(poses[0].position.x == 1 && poses[0].position.y == 2 && poses[0].position.z == 3);
            typeof(XRHandFrame).GetMethod("Clear", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hand, null);
            encoding.GetMethod("Hand").Invoke(null, new object[] { hand, buffer });
            for (int i = 0; i < buffer.Length; ++i) Check(buffer[i] == 0);

            states[0] = 1; valid[0] = true;
            poses[0] = new Pose(new Vector3(float.NaN, 0, 0), new Quaternion(0, 0, 0, 1));
            bool rejected = false;
            try { encoding.GetMethod("Hand").Invoke(null, new object[] { hand, buffer }); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
            Check(rejected);

            var body = (XRBodyFrame)Activator.CreateInstance(typeof(XRBodyFrame), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { 2 }, null);
            Set(body, "IsValid", true);
            var bodyPoses = Field<Pose[]>(body, "Poses");
            bodyPoses[0] = bodyPoses[1] = new Pose(new Vector3(1, 2, 3), new Quaternion(0, 0, 0, 1));
            var bodyBuffer = new float[19];
            encoding.GetMethod("Body").Invoke(null, new object[] { body, bodyBuffer });
            Check(bodyBuffer[0] == 1 && bodyBuffer[1] == 1 && bodyBuffer[2] == 2);
            Check(bodyBuffer[3] == 1 && bodyBuffer[4] == 3 && bodyBuffer[5] == -1 && bodyBuffer[10] == 1);
            Set(body, "IsValid", false);
            encoding.GetMethod("Body").Invoke(null, new object[] { body, bodyBuffer });
            Check(bodyBuffer[1] == 0 && bodyBuffer[2] == 2);
            for (int i = 3; i < bodyBuffer.Length; ++i) Check(bodyBuffer[i] == 0);
            return assertions;
        }

        private static T Field<T>(object frame, string name) =>
            (T)frame.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(frame);
        private static void Set(object frame, string name, object value) => frame.GetType().GetProperty(name).SetValue(frame, value);
    }
}
