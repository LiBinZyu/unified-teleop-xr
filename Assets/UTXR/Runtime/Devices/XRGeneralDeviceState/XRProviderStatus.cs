using System;

namespace UTXR.Devices
{
    // Availability of the selected source; independent of the headset's Controllers/Hands/Both setting.
    public enum XRProviderStatusCode
    {
        Disabled, Ready, InvalidConfiguration, ProviderMissing, ProviderNotRunning,
        DeviceUnavailable, AmbiguousSource, InvalidData, ProviderFaulted
    }

    public readonly struct XRProviderStatus : IEquatable<XRProviderStatus>
    {
        public XRProviderStatusCode Code { get; }
        public string Message { get; }
        public bool IsReady => Code == XRProviderStatusCode.Ready;
        public static XRProviderStatus Ready => new XRProviderStatus(XRProviderStatusCode.Ready, string.Empty);

        public XRProviderStatus(XRProviderStatusCode code, string message)
        {
            Code = code;
            Message = message ?? string.Empty;
        }

        public bool Equals(XRProviderStatus other) => Code == other.Code && Message == other.Message;
        public override bool Equals(object obj) => obj is XRProviderStatus other && Equals(other);
        public override int GetHashCode() => ((int)Code * 397) ^ (Message?.GetHashCode() ?? 0);
        public override string ToString() => string.IsNullOrEmpty(Message) ? Code.ToString() : $"{Code}: {Message}";
    }
}