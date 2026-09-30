using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

namespace Record3D
{
    [DisallowMultipleComponent]
    public class Record3DSignaling : MonoBehaviour
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        [SerializeField] private Record3DVideoCodec _videoCodec = Record3DVideoCodec.VP8;
        [SerializeField] private Record3DVideoRepair _videoRepair = Record3DVideoRepair.None;
        private string _deviceAddress = "192.168.0.194";

        /// <summary>The exact video codec to negotiate. Defaults to VP8; never falls back.</summary>
        public Record3DVideoCodec VideoCodec
        {
            get => _videoCodec;
            set => _videoCodec = value;
        }

        /// <summary>Explicit repair-codec policy. Defaults to none, matching the tested VP8 path.</summary>
        public Record3DVideoRepair VideoRepair
        {
            get => _videoRepair;
            set => _videoRepair = value;
        }

        public string DeviceAddress
        {
            get => _deviceAddress;
            set => _deviceAddress = !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException("Device address cannot be empty.", nameof(value));
        }

        private string BaseUrl
        {
            get
            {
                string url = _deviceAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    _deviceAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? _deviceAddress : "http://" + _deviceAddress;
                return url.TrimEnd('/');
            }
        }

        /// <summary>Restrict the video transceiver to the selected codec before CreateAnswer.</summary>
        public bool TryApplyVideoCodec(RTCPeerConnection peerConnection, string offerSdp, out string error)
        {
            error = null;
            if (peerConnection == null)
            {
                error = "PeerConnection is null.";
                return false;
            }
            if (!TryGetCodecNames(out string mimeType, out string sdpName, out string profilePrefix))
            {
                error = $"Unsupported video codec setting: {_videoCodec}.";
                return false;
            }
            if (!TryGetVideoPayload(offerSdp, sdpName, profilePrefix, out string offerPayload))
            {
                error = $"Record3D offer does not advertise the selected codec {_videoCodec}.";
                return false;
            }

            if (!Enum.IsDefined(typeof(Record3DVideoRepair), _videoRepair))
            {
                error = $"Unsupported repair setting: {_videoRepair}.";
                return false;
            }
            if (_videoRepair >= Record3DVideoRepair.RTX && !VideoSdpHasAssociatedRtx(offerSdp, offerPayload) ||
                _videoRepair == Record3DVideoRepair.RTX_RED_ULPFEC &&
                (!VideoSdpHasCodec(offerSdp, "red") || !VideoSdpHasCodec(offerSdp, "ulpfec")))
            {
                error = $"Record3D offer does not advertise the selected repair mode {_videoRepair}.";
                return false;
            }

            var capabilities = RTCRtpReceiver.GetCapabilities(TrackKind.Video).codecs;
            var selected = new List<RTCRtpCodecCapability>();
            foreach (var codec in capabilities)
            {
                if (string.Equals(codec.mimeType, mimeType, StringComparison.OrdinalIgnoreCase) &&
                    codec.clockRate == 90000 &&
                    (profilePrefix == null || FmtpHasProfile(codec.sdpFmtpLine, profilePrefix)))
                    selected.Add(codec);
            }
            if (selected.Count == 0)
            {
                error = $"Local WebRTC receiver cannot decode the selected codec {_videoCodec}.";
                return false;
            }

            bool hasRtx = false, hasRed = false, hasUlpfec = false;
            foreach (var codec in capabilities)
            {
                if (_videoRepair >= Record3DVideoRepair.RTX &&
                    string.Equals(codec.mimeType, "video/rtx", StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add(codec);
                    hasRtx = true;
                }
                if (_videoRepair == Record3DVideoRepair.RTX_RED_ULPFEC &&
                    string.Equals(codec.mimeType, "video/red", StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add(codec);
                    hasRed = true;
                }
                if (_videoRepair == Record3DVideoRepair.RTX_RED_ULPFEC &&
                    string.Equals(codec.mimeType, "video/ulpfec", StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add(codec);
                    hasUlpfec = true;
                }
            }
            if (_videoRepair >= Record3DVideoRepair.RTX && !hasRtx ||
                _videoRepair == Record3DVideoRepair.RTX_RED_ULPFEC && (!hasRed || !hasUlpfec))
            {
                error = $"Local WebRTC receiver lacks the selected repair mode {_videoRepair}.";
                return false;
            }

            int videoCount = 0;
            foreach (var transceiver in peerConnection.GetTransceivers())
            {
                var track = transceiver.Receiver.Track;
                if (track == null || track.Kind != TrackKind.Video) continue;
                var result = transceiver.SetCodecPreferences(selected.ToArray());
                if (result != RTCErrorType.None)
                {
                    error = $"SetCodecPreferences({_videoCodec}) failed: {result}.";
                    return false;
                }
                videoCount++;
            }
            if (videoCount == 0)
            {
                error = "Offer did not create a video transceiver.";
                return false;
            }
            Record3DLogger.Signaling($"Video codec selected: {_videoCodec}; repair: {_videoRepair} (no fallback).");
            return true;
        }

        /// <summary>Fail if the generated answer does not exclusively contain the selected video codec.</summary>
        public bool TryValidateVideoAnswer(string answerSdp, out string error)
        {
            error = null;
            if (!TryGetCodecNames(out _, out string sdpName, out string profilePrefix))
            {
                error = $"Unsupported video codec setting: {_videoCodec}.";
                return false;
            }
            string other = _videoCodec == Record3DVideoCodec.VP8 ? "H264" : "VP8";
            if (!TryGetVideoPayload(answerSdp, sdpName, profilePrefix, out string answerPayload) ||
                VideoSdpHasCodec(answerSdp, other))
            {
                error = $"Generated answer does not exclusively negotiate {_videoCodec}; refusing to send it.";
                return false;
            }
            bool rtx = VideoSdpHasCodec(answerSdp, "rtx");
            bool associatedRtx = VideoSdpHasAssociatedRtx(answerSdp, answerPayload);
            bool red = VideoSdpHasCodec(answerSdp, "red");
            bool ulpfec = VideoSdpHasCodec(answerSdp, "ulpfec");
            bool matchesRepair = _videoRepair == Record3DVideoRepair.None && !rtx && !red && !ulpfec ||
                _videoRepair == Record3DVideoRepair.RTX && associatedRtx && !red && !ulpfec ||
                _videoRepair == Record3DVideoRepair.RTX_RED_ULPFEC && associatedRtx && red && ulpfec;
            if (!matchesRepair)
            {
                error = $"Generated answer repair codecs do not match {_videoRepair}; refusing to send it.";
                return false;
            }
            return true;
        }

        private bool TryGetCodecNames(out string mimeType, out string sdpName, out string profilePrefix)
        {
            mimeType = null;
            sdpName = null;
            profilePrefix = null;
            switch (_videoCodec)
            {
                case Record3DVideoCodec.VP8: mimeType = "video/VP8"; sdpName = "VP8"; return true;
                case Record3DVideoCodec.H264ConstrainedBaseline:
                    mimeType = "video/H264"; sdpName = "H264"; profilePrefix = "42e0"; return true;
                case Record3DVideoCodec.H264High:
                    mimeType = "video/H264"; sdpName = "H264"; profilePrefix = "640c"; return true;
                default: return false;
            }
        }

        private static bool FmtpHasProfile(string fmtp, string profilePrefix)
        {
            return !string.IsNullOrEmpty(fmtp) &&
                fmtp.IndexOf("profile-level-id=" + profilePrefix, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ReadVideoCodecs(string sdp, Dictionary<string, string> rtpmap,
            Dictionary<string, string> fmtp)
        {
            if (string.IsNullOrEmpty(sdp)) return;
            bool inVideo = false;
            foreach (string rawLine in sdp.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("m=", StringComparison.Ordinal))
                    inVideo = line.StartsWith("m=video ", StringComparison.OrdinalIgnoreCase);
                if (!inVideo) continue;
                bool isRtpmap = line.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase);
                bool isFmtp = line.StartsWith("a=fmtp:", StringComparison.OrdinalIgnoreCase);
                if (!isRtpmap && !isFmtp) continue;
                int colon = line.IndexOf(':');
                int space = line.IndexOf(' ', colon + 1);
                if (space < 0) continue;
                string payload = line.Substring(colon + 1, space - colon - 1);
                if (isRtpmap) rtpmap[payload] = line.Substring(space + 1);
                else fmtp[payload] = line.Substring(space + 1);
            }
        }

        private static bool TryGetVideoPayload(string sdp, string codecName, string profilePrefix,
            out string payload)
        {
            payload = null;
            var rtpmap = new Dictionary<string, string>();
            var fmtp = new Dictionary<string, string>();
            ReadVideoCodecs(sdp, rtpmap, fmtp);
            foreach (var entry in rtpmap)
            {
                if (!entry.Value.StartsWith(codecName + "/90000", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (profilePrefix != null &&
                    (!fmtp.TryGetValue(entry.Key, out string parameters) ||
                     !FmtpHasProfile(parameters, profilePrefix))) continue;
                payload = entry.Key;
                return true;
            }
            return false;
        }

        private static bool VideoSdpHasCodec(string sdp, string codecName)
        {
            return TryGetVideoPayload(sdp, codecName, null, out _);
        }

        private static bool VideoSdpHasAssociatedRtx(string sdp, string primaryPayload)
        {
            var rtpmap = new Dictionary<string, string>();
            var fmtp = new Dictionary<string, string>();
            ReadVideoCodecs(sdp, rtpmap, fmtp);
            foreach (var entry in rtpmap)
            {
                if (!entry.Value.StartsWith("rtx/90000", StringComparison.OrdinalIgnoreCase) ||
                    !fmtp.TryGetValue(entry.Key, out string parameters)) continue;
                foreach (string item in parameters.Split(';'))
                {
                    if (string.Equals(item.Trim(), "apt=" + primaryPayload, StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        public IEnumerator RetrieveOffer(Action<string> onOfferReceived, Action<string> onError)
        {
            string url = $"{BaseUrl}/getOffer";
            Record3DLogger.Signaling($"Requesting SDP offer from {url}...");

            Task<string> task = null;
            try
            {
                task = _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                string errMsg = $"Failed to initiate GET {url}: {ex.Message}";
                Record3DLogger.Signaling(errMsg, Record3DLogLevel.Error);
                onError?.Invoke(errMsg);
                yield break;
            }

            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted || task.IsCanceled)
            {
                string errMsg = task.Exception != null 
                    ? task.Exception.GetBaseException().Message 
                    : "Request timed out or cancelled";
                Record3DLogger.Signaling($"Failed to get offer from {url}: {errMsg}", Record3DLogLevel.Error);
                onError?.Invoke(errMsg);
                yield break;
            }

            string text = task.Result;
            Record3DLogger.Signaling($"Received offer payload ({text.Length} bytes)");
            try
            {
                Record3DOfferPayload offer = JsonUtility.FromJson<Record3DOfferPayload>(text);
                if (offer != null && !string.IsNullOrEmpty(offer.sdp))
                {
                    Record3DLogger.Signaling($"Offer parsed successfully, SDP length: {offer.sdp.Length}");
                    onOfferReceived?.Invoke(offer.sdp);
                }
                else
                {
                    string errMsg = $"Invalid offer SDP JSON payload: {text}";
                    Record3DLogger.Signaling(errMsg, Record3DLogLevel.Error);
                    onError?.Invoke(errMsg);
                }
            }
            catch (Exception ex)
            {
                string errMsg = $"JSON parse error on /getOffer: {ex.Message}";
                Record3DLogger.Signaling(errMsg, Record3DLogLevel.Error);
                onError?.Invoke(errMsg);
            }
        }

        public IEnumerator SendAnswer(string sdp, Action onSuccess, Action<string> onError)
        {
            string url = $"{BaseUrl}/answer";
            Record3DLogger.Signaling($"Sending SDP answer to {url} (payload: {sdp.Length} bytes)...");
            string escapedSdp = EscapeJsonString(sdp);
            string json = $"{{\"type\":\"answer\",\"data\":\"{escapedSdp}\"}}";

            Task<HttpResponseMessage> task = null;
            try
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                task = _httpClient.PostAsync(url, content);
            }
            catch (Exception ex)
            {
                string errMsg = $"Failed to initiate POST {url}: {ex.Message}";
                Record3DLogger.Signaling(errMsg, Record3DLogLevel.Error);
                onError?.Invoke(errMsg);
                yield break;
            }

            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted || task.IsCanceled)
            {
                string errMsg = task.Exception != null 
                    ? task.Exception.GetBaseException().Message 
                    : "Request timed out or cancelled";
                Record3DLogger.Signaling($"Failed to send answer to {url}: {errMsg}", Record3DLogLevel.Error);
                onError?.Invoke(errMsg);
                yield break;
            }

            HttpResponseMessage response = task.Result;
            if (!response.IsSuccessStatusCode)
            {
                string errMsg = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                Record3DLogger.Signaling($"Failed to send answer to {url}: {errMsg}", Record3DLogLevel.Error);
                onError?.Invoke(errMsg);
                yield break;
            }

            Record3DLogger.Signaling("Answer delivered to device successfully!");
            onSuccess?.Invoke();
        }

        public IEnumerator RetrieveMetadata(Action<float[], int, int> onMetadataReceived, Action<string> onError)
        {
            string url = $"{BaseUrl}/metadata";
            Record3DLogger.Signaling($"Fetching device baseline metadata from {url}...");

            Task<string> task = null;
            try
            {
                task = _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                string errMsg = $"Failed to initiate GET {url}: {ex.Message}";
                Record3DLogger.Signaling(errMsg, Record3DLogLevel.Warning);
                onError?.Invoke(errMsg);
                yield break;
            }

            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted || task.IsCanceled)
            {
                string errMsg = task.Exception != null 
                    ? task.Exception.GetBaseException().Message 
                    : "Request timed out";
                Record3DLogger.Signaling($"Baseline metadata fetch note: {errMsg}", Record3DLogLevel.Warning);
                onError?.Invoke(errMsg);
                yield break;
            }

            string text = task.Result;
            Record3DLogger.Signaling($"Received /metadata response: {text}");
            try
            {
                Record3DDeviceMetadata meta = JsonUtility.FromJson<Record3DDeviceMetadata>(text);
                if (meta != null && meta.K != null && meta.K.Length >= 9)
                {
                    int w = (meta.originalSize != null && meta.originalSize.Length >= 2) ? meta.originalSize[0] : 720;
                    int h = (meta.originalSize != null && meta.originalSize.Length >= 2) ? meta.originalSize[1] : 960;
                    Record3DLogger.Signaling($"Parsed device metadata: {w}x{h}, fx={meta.K[0]}, fy={meta.K[4]}");
                    onMetadataReceived?.Invoke(meta.K, w, h);
                }
                else
                {
                    string errMsg = $"Metadata payload missing K elements: {text}";
                    Record3DLogger.Signaling(errMsg, Record3DLogLevel.Warning);
                    onError?.Invoke(errMsg);
                }
            }
            catch (Exception ex)
            {
                string errMsg = $"JSON parse error on /metadata: {ex.Message}";
                Record3DLogger.Signaling(errMsg, Record3DLogLevel.Error);
                onError?.Invoke(errMsg);
            }
        }

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 64);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
