using System;

namespace CycloneDDS
{
    /// <summary>
    /// Owns a reusable native receive sample, including its nested strings and sequences.
    /// Use with a subscriber of the same Type, on one thread, and dispose explicitly.
    /// Nested pointers remain valid until the next Take or Dispose; copy data before retaining it.
    /// </summary>
    public sealed class DdsRawSample : IDisposable
    {
        private IntPtr pointer;

        public DdsMessageType Type { get; }
        public UIntPtr Size { get; }

        public IntPtr Pointer
        {
            get
            {
                if (pointer == IntPtr.Zero) throw new ObjectDisposedException(nameof(DdsRawSample));
                return pointer;
            }
        }

        public DdsRawSample(DdsMessageType type)
        {
            Type = type;
            Size = CddsNative.cdds_sample_size(type);
            if (Size == UIntPtr.Zero) throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported DDS type.");
            pointer = CddsNative.cdds_sample_alloc(type);
            if (pointer == IntPtr.Zero) throw new OutOfMemoryException("DDS sample allocation failed.");
        }

        /// <summary>Returns true for new valid data, false for no valid data; DDS errors throw.</summary>
        public bool Take(int subscriber)
        {
            int result = CddsNative.cdds_take_raw(subscriber, Pointer, Size);
            if (result < 0) throw new InvalidOperationException($"DDS take failed: {result} (subscriber {subscriber}).");
            return result == 1;
        }

        public void Dispose()
        {
            if (pointer == IntPtr.Zero) return;
            CddsNative.cdds_sample_free(Type, pointer);
            pointer = IntPtr.Zero;
        }
    }
}
