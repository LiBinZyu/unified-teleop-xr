using System;

namespace UTXR.Devices
{
    // Numeric values preserve existing scene configuration.
    public enum FilterMethod { None, BitwiseDebounce, KinematicDrift, Combined }

    [Serializable]
    public sealed class XRDeviceStateSettings
    {
        public FilterMethod Filter = FilterMethod.None;
        public bool Require6DoF = true;
        public int FrameTolerance = 20;
        public float LostToleranceSeconds = 0.1f;
        public float MaxHumanSpeed = 8f;
        public float MinLinearVelocity = 0.001f;
        public float PositionJumpMinDelta = 0.015f;
        public float PositionJumpMaxSpeed = 4f;
        public float WarningHoldSeconds = 2f;
        public bool EnableSwingDetection = true;
        public float SwingSpeedThreshold = 1.5f;
        public float SwingCooldownSeconds = 0.15f;
        public float TripleSwingWindowSeconds = 1f;

        public bool UsesKinematicFilter => Filter == FilterMethod.KinematicDrift || Filter == FilterMethod.Combined;
        internal XRDeviceStateSettings Copy() => (XRDeviceStateSettings)MemberwiseClone();

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(FilterMethod), Filter)) throw new ArgumentOutOfRangeException(nameof(Filter));
            if (FrameTolerance < 1 || FrameTolerance > 32) throw new ArgumentOutOfRangeException(nameof(FrameTolerance));
            RequireNonNegative(LostToleranceSeconds, nameof(LostToleranceSeconds));
            RequireNonNegative(MaxHumanSpeed, nameof(MaxHumanSpeed));
            RequireNonNegative(MinLinearVelocity, nameof(MinLinearVelocity));
            RequireNonNegative(PositionJumpMinDelta, nameof(PositionJumpMinDelta));
            RequireNonNegative(PositionJumpMaxSpeed, nameof(PositionJumpMaxSpeed));
            RequireNonNegative(WarningHoldSeconds, nameof(WarningHoldSeconds));
            RequireNonNegative(SwingSpeedThreshold, nameof(SwingSpeedThreshold));
            if (SwingSpeedThreshold == 0f) throw new ArgumentOutOfRangeException(nameof(SwingSpeedThreshold));
            RequireNonNegative(SwingCooldownSeconds, nameof(SwingCooldownSeconds));
            RequireNonNegative(TripleSwingWindowSeconds, nameof(TripleSwingWindowSeconds));
        }

        private static void RequireNonNegative(float value, string name)
        {
            if (!XRDeviceStateCore.IsFinite(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name, "Must be finite and non-negative.");
        }
    }
}