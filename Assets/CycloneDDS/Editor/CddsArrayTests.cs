using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CycloneDDS.Editor
{
    public static class CddsArrayTests
    {
        public static void RunBatch()
        {
            try
            {
                Run();
                Debug.Log("CDDS_ARRAY_TESTS_PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Tools/CycloneDDS/Test Array Interop")]
        public static void Run()
        {
            if (CddsNative.cdds_is_initialized()) throw new InvalidOperationException("Stop the active DDS session before running this test.");
            DdsRawSample sample = null;
            try
            {
                const string config = "<CycloneDDS><Domain><General><Interfaces><NetworkInterface address=\"127.0.0.1\"/></Interfaces><AllowMulticast>false</AllowMulticast></General></Domain></CycloneDDS>";
                Require(CddsNative.cdds_init(198, config) == 0, "DDS initialization failed.");
                int publisher = CddsNative.cdds_create_publisher("managed_array_test", DdsMessageType.StdFloat32MultiArray, DdsQosPreset.Default);
                int subscriber = CddsNative.cdds_create_subscriber("managed_array_test", DdsMessageType.StdFloat32MultiArray, DdsQosPreset.Default);
                Require(publisher > 0 && subscriber > 0, "Entity creation failed.");
                WaitUntil(() => CddsNative.cdds_get_matched_subscriptions(publisher) > 0);

                sample = new DdsRawSample(DdsMessageType.StdFloat32MultiArray);
                Require((ulong)Marshal.SizeOf<StdFloat32MultiArray>() == sample.Size.ToUInt64(), "Managed/native sample ABI mismatch.");
                Require(!sample.Take(subscriber), "Unexpected initial data.");
                var sent = new float[182];
                var received = new float[sent.Length];
                for (int pass = 0; pass < 16; ++pass)
                {
                    for (int j = 0; j < sent.Length; ++j) sent[j] = pass * 1000 + j + 0.25f;
                    Require(CddsNative.cdds_publish_float32_multi_array(publisher, sent) == 0, "Array publish failed.");
                    WaitUntil(() => sample.Take(subscriber));
                    var view = Marshal.PtrToStructure<StdFloat32MultiArray>(sample.Pointer);
                    Require(view.layout.dim.length == 0 && view.layout.data_offset == 0, "Unexpected layout.");
                    Require(view.data.length == sent.Length, "Wrong sequence length.");
                    Marshal.Copy(view.data.buffer, received, 0, received.Length);
                    for (int j = 0; j < received.Length; ++j) Require(received[j] == sent[j], "Array data mismatch.");
                }
                Require(CddsNative.cdds_publish_float32_multi_array(publisher, Array.Empty<float>()) == 0, "Empty array publish failed.");
                WaitUntil(() => sample.Take(subscriber));
                Require(Marshal.PtrToStructure<StdFloat32MultiArray>(sample.Pointer).data.length == 0, "Empty array receive failed.");
                bool failed = false;
                try { sample.Take(-1); }
                catch (InvalidOperationException) { failed = true; }
                Require(failed, "Native errors must be reported.");
                CddsNative.cdds_shutdown();
                Require(Marshal.PtrToStructure<StdFloat32MultiArray>(sample.Pointer).data.length == 0, "Sample lost ownership after shutdown.");
                sample.Dispose();
                sample.Dispose();
                failed = false;
                try { _ = sample.Pointer; }
                catch (ObjectDisposedException) { failed = true; }
                Require(failed, "Disposed sample must reject access.");
            }
            finally
            {
                CddsNative.cdds_shutdown();
                sample?.Dispose();
            }
        }

        private static void WaitUntil(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition())
            {
                if (timer.ElapsedMilliseconds > 5000) throw new TimeoutException("DDS test timed out.");
                Thread.Sleep(10);
            }
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
