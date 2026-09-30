using System;
using System.Collections;
using System.Text;
using Unity.WebRTC;
using UnityEngine;

namespace Record3D
{
    public class Record3DWebRTCReceiver : IRecord3DReceiver
    {
        public event Action<Texture, Record3DMetadataInfo> OnFrameReceived;
        public event Action OnVideoTextureChanging;

        private readonly MonoBehaviour _coroutineHost;
        private string _deviceAddress = "192.168.0.194";
        private bool _isConnected = false;
        private bool _isConnecting = false;

        private RTCPeerConnection _peerConnection;
        private RTCDataChannel _dataChannel;
        private VideoStreamTrack _videoTrack;
        private Texture _activeVideoTexture;
        private Texture _pendingVideoTexture;
        private int _pendingVideoTextureFrame = -1;
        private Coroutine _connectCoroutine;
        private Coroutine _webrtcUpdateCoroutine;
        private OnVideoReceived _onVideoReceivedDelegate;
        private int _sessionGeneration;
        private bool _isStopping;
        private bool _isDisposed;

        // DataChannel delegates held as class fields to prevent GC collection
        private DelegateOnDataChannel _onDataChannelDelegate;
        private DelegateOnMessage _onDataChannelMessageDelegate;
        private DelegateOnOpen _onDataChannelOpenDelegate;
        private DelegateOnClose _onDataChannelCloseDelegate;
        private int _poseUpdateCount = 0;

        // Metadata tracking
        private Record3DMetadataInfo _latestMetadata;
        private float[] _activeK = null;
        private bool _loggedMissingActiveK = false;
        private int _originalRgbWidth = 720;
        private int _originalRgbHeight = 960;
        private bool _iceGatheringComplete = false;

        public bool IsConnected => _isConnected;
        public bool IsConnecting => _isConnecting;
        public string DeviceAddress
        {
            get => _deviceAddress;
            set => _deviceAddress = value;
        }

        public Record3DWebRTCReceiver(MonoBehaviour host, string deviceAddress = "192.168.0.194")
        {
            _coroutineHost = host;
            _deviceAddress = deviceAddress;
            InitDefaultMetadata();
        }

        private void InitDefaultMetadata()
        {
            _latestMetadata = new Record3DMetadataInfo
            {
                currentRgbWidth = 720,
                currentRgbHeight = 960,
                cameraPosition = Vector3.zero,
                cameraRotation = Quaternion.identity,
                poseMatrix = Matrix4x4.identity,
                minDepth = 0.1f,
                maxDepth = 2.8f,
                depthThreshold = 0.05f,
                unprojection = Vector4.zero
            };
        }

        public void StartReceiving()
        {
            if (_isDisposed || _isStopping || _isConnecting || _isConnected)
                return;

            int session = ++_sessionGeneration;

            if (_webrtcUpdateCoroutine == null)
            {
                _webrtcUpdateCoroutine = _coroutineHost.StartCoroutine(WebRTC.Update());
            }

            _connectCoroutine = _coroutineHost.StartCoroutine(ConnectFlow(session));
        }

        public void StopReceiving()
        {
            if (_isStopping)
                return;

            _isStopping = true;
            ++_sessionGeneration; // Invalidate every callback queued by the old native session.

            if (_connectCoroutine != null)
            {
                _coroutineHost.StopCoroutine(_connectCoroutine);
                _connectCoroutine = null;
            }

            _isConnected = false;
            _isConnecting = false;
            _activeK = null;
            _loggedMissingActiveK = false;
            _poseUpdateCount = 0;
            _activeVideoTexture = null;
            _pendingVideoTexture = null;
            _pendingVideoTextureFrame = -1;

            // Clear the instance fields first. A queued native callback can then no longer
            // publish an old track/texture into a subsequent connection.
            VideoStreamTrack videoTrack = _videoTrack;
            RTCDataChannel dataChannel = _dataChannel;
            RTCPeerConnection peerConnection = _peerConnection;
            OnVideoReceived videoReceivedDelegate = _onVideoReceivedDelegate;
            _videoTrack = null;
            _dataChannel = null;
            _peerConnection = null;
            _onVideoReceivedDelegate = null;

            // Detach all managed callbacks before touching native ownership. WebRTC posts
            // callbacks through Unity's SynchronizationContext, so some may already be queued.
            if (videoTrack != null && videoReceivedDelegate != null)
            {
                videoTrack.OnVideoReceived -= videoReceivedDelegate;
            }

            if (dataChannel != null)
            {
                dataChannel.OnMessage = null;
                dataChannel.OnOpen = null;
                dataChannel.OnClose = null;
            }

            if (peerConnection != null)
            {
                peerConnection.OnIceGatheringStateChange = null;
                peerConnection.OnIceCandidate = null;
                peerConnection.OnIceConnectionChange = null;
                peerConnection.OnConnectionStateChange = null;
                peerConnection.OnTrack = null;
                peerConnection.OnDataChannel = null;
                peerConnection.OnNegotiationNeeded = null;
            }

            // The peer connection owns received tracks/transceivers. Disposing the track here
            // and then disposing the peer connection can race/double-release its video renderer.
            // Let RTCPeerConnection.Dispose() release the received VideoStreamTrack exactly once.
            SafeCloseAndDispose(dataChannel);
            SafeDispose(peerConnection);
            if (peerConnection == null)
            {
                SafeDispose(videoTrack);
            }

            _onDataChannelDelegate = null;
            _onDataChannelMessageDelegate = null;
            _onDataChannelOpenDelegate = null;
            _onDataChannelCloseDelegate = null;
            _isStopping = false;
        }

        private IEnumerator ConnectFlow(int session)
        {
            _isConnecting = true;
            Record3DLogger.WebRTC($"Initiating WebRTC connection to {_deviceAddress}...");

            // Use an Inspector-configured component when present. An absent component is
            // created only at runtime with its declared default codec; no scene is changed.
            var signaling = _coroutineHost.GetComponent<Record3DSignaling>();
            if (signaling == null)
                signaling = _coroutineHost.gameObject.AddComponent<Record3DSignaling>();
            signaling.DeviceAddress = _deviceAddress;

            // Fetch SDP Offer
            string offerSdp = null;
            string offerError = null;
            yield return signaling.RetrieveOffer(
                sdp => { if (IsSessionCurrent(session)) offerSdp = sdp; },
                err => { if (IsSessionCurrent(session)) offerError = err; }
            );

            if (!IsSessionCurrent(session)) yield break;

            if (string.IsNullOrEmpty(offerSdp))
            {
                Record3DLogger.WebRTC($"Connection aborted: failed to retrieve offer: {offerError}", Record3DLogLevel.Error);
                _isConnecting = false;
                _connectCoroutine = null;
                yield break;
            }
            Record3DLogger.WebRTC($"Offer video codecs: {DescribeVideoCodecs(offerSdp)}");

            // Create PeerConnection
            var config = new RTCConfiguration
            {
                iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } }
            };
            _peerConnection = new RTCPeerConnection(ref config);
            Record3DLogger.WebRTC("Created RTCPeerConnection instance");

            _iceGatheringComplete = false;
            _peerConnection.OnIceGatheringStateChange = state =>
            {
                if (!IsSessionCurrent(session)) return;
                Record3DLogger.WebRTC($"ICE Gathering State: {state}");
                if (state == RTCIceGatheringState.Complete)
                {
                    _iceGatheringComplete = true;
                }
            };

            _peerConnection.OnIceCandidate = candidate =>
            {
                if (!IsSessionCurrent(session)) return;
                if (candidate == null)
                {
                    Record3DLogger.WebRTC("ICE candidate gathering finished (candidate=null)");
                    _iceGatheringComplete = true;
                }
            };

            _peerConnection.OnIceConnectionChange = state =>
            {
                if (!IsSessionCurrent(session)) return;
                Record3DLogger.WebRTC($"ICE Connection State: {state}", (state == RTCIceConnectionState.Failed) ? Record3DLogLevel.Error : Record3DLogLevel.Info);
                if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
                {
                    _isConnected = true;
                }
                else if (state == RTCIceConnectionState.Disconnected || state == RTCIceConnectionState.Failed)
                {
                    _isConnected = false;
                }
            };

            _peerConnection.OnTrack = e =>
            {
                if (!IsSessionCurrent(session)) return;
                if (e.Track is VideoStreamTrack vt)
                {
                    Record3DLogger.WebRTC("VideoStreamTrack attached from remote peer!");
                    if (_videoTrack != null && _onVideoReceivedDelegate != null)
                    {
                        _videoTrack.OnVideoReceived -= _onVideoReceivedDelegate;
                    }
                    _videoTrack = vt;
                    _onVideoReceivedDelegate = tex => HandleVideoReceived(session, vt, tex);
                    _videoTrack.OnVideoReceived += _onVideoReceivedDelegate;
                }
            };

            // Retain unmanaged delegates in class fields so GC cannot collect them
            _onDataChannelMessageDelegate = bytes =>
            {
                if (!IsSessionCurrent(session)) return;
                if (bytes == null || bytes.Length == 0) return;
                string json = Encoding.UTF8.GetString(bytes);
                ParseDataChannelJson(json);
            };

            _onDataChannelOpenDelegate = () =>
            {
                if (!IsSessionCurrent(session)) return;
                Record3DLogger.WebRTC("RTCDataChannel is now OPEN and active.");
            };

            _onDataChannelCloseDelegate = () =>
            {
                if (!IsSessionCurrent(session)) return;
                Record3DLogger.WebRTC("RTCDataChannel has closed.");
            };

            _onDataChannelDelegate = channel =>
            {
                if (!IsSessionCurrent(session))
                {
                    SafeCloseAndDispose(channel);
                    return;
                }
                Record3DLogger.WebRTC($"RTCDataChannel received: '{channel.Label}' (ID: {channel.Id}, ReadyState: {channel.ReadyState})");
                _dataChannel = channel;
                _dataChannel.OnMessage = _onDataChannelMessageDelegate;
                _dataChannel.OnOpen = _onDataChannelOpenDelegate;
                _dataChannel.OnClose = _onDataChannelCloseDelegate;
            };

            _peerConnection.OnDataChannel = _onDataChannelDelegate;

            // Set Remote Description (Offer)
            var remoteOffer = new RTCSessionDescription
            {
                type = RTCSdpType.Offer,
                sdp = offerSdp
            };
            var setRemoteOp = _peerConnection.SetRemoteDescription(ref remoteOffer);
            yield return setRemoteOp;

            if (!IsSessionCurrent(session)) yield break;

            if (setRemoteOp.IsError)
            {
                Record3DLogger.WebRTC($"SetRemoteDescription error: {setRemoteOp.Error.message}", Record3DLogLevel.Error);
                _isConnecting = false;
                _connectCoroutine = null;
                yield break;
            }
            Record3DLogger.WebRTC("Remote description (Offer) set successfully");

            if (!signaling.TryApplyVideoCodec(_peerConnection, offerSdp, out string codecError))
            {
                Record3DLogger.WebRTC($"Video codec negotiation aborted: {codecError}", Record3DLogLevel.Error);
                _isConnecting = false;
                _connectCoroutine = null;
                yield break;
            }

            // Create Answer
            var createAnswerOp = _peerConnection.CreateAnswer();
            yield return createAnswerOp;

            if (!IsSessionCurrent(session)) yield break;

            if (createAnswerOp.IsError)
            {
                Record3DLogger.WebRTC($"CreateAnswer error: {createAnswerOp.Error.message}", Record3DLogLevel.Error);
                _isConnecting = false;
                _connectCoroutine = null;
                yield break;
            }

            var localAnswer = createAnswerOp.Desc;
            if (!signaling.TryValidateVideoAnswer(localAnswer.sdp, out string answerCodecError))
            {
                Record3DLogger.WebRTC($"Video codec negotiation aborted: {answerCodecError}", Record3DLogLevel.Error);
                _isConnecting = false;
                _connectCoroutine = null;
                yield break;
            }
            var setLocalOp = _peerConnection.SetLocalDescription(ref localAnswer);
            yield return setLocalOp;

            if (!IsSessionCurrent(session)) yield break;

            if (setLocalOp.IsError)
            {
                Record3DLogger.WebRTC($"SetLocalDescription error: {setLocalOp.Error.message}", Record3DLogLevel.Error);
                _isConnecting = false;
                _connectCoroutine = null;
                yield break;
            }
            Record3DLogger.WebRTC("Local description (Answer) set successfully");

            // Wait for ICE gathering to complete or timeout after 1 second
            float waitTimer = 0f;
            while (!_iceGatheringComplete && waitTimer < 1.0f)
            {
                if (!IsSessionCurrent(session)) yield break;
                waitTimer += Time.unscaledDeltaTime;
                yield return null;
            }

            // Send local Answer to Record3D
            string finalAnswerSdp = _peerConnection.LocalDescription.sdp;
            Record3DLogger.WebRTC($"Answer video codecs: {DescribeVideoCodecs(finalAnswerSdp)}");
            yield return signaling.SendAnswer(
                finalAnswerSdp,
                () =>
                {
                    if (!IsSessionCurrent(session)) return;
                    Record3DLogger.WebRTC("Handshake complete: Answer accepted by device! Live stream active.");
                    _isConnected = true;
                    _isConnecting = false;
                    _connectCoroutine = null;
                },
                err =>
                {
                    if (!IsSessionCurrent(session)) return;
                    Record3DLogger.WebRTC($"Handshake failed when sending answer: {err}", Record3DLogLevel.Error);
                    _isConnecting = false;
                    _connectCoroutine = null;
                }
            );
        }

        private void HandleVideoReceived(int session, VideoStreamTrack track, Texture tex)
        {
            if (!IsSessionCurrent(session) || !ReferenceEquals(track, _videoTrack) || tex == null)
                return;

            // A resolution change destroys the package-owned old Texture and creates a new one.
            // Finish the last GPU read before WebRTC destroys the old native texture. Then remove
            // the old source immediately, so the independently-clocked ingest loop cannot submit
            // another dispatch against it during this resize frame.
            if (_activeVideoTexture != null && !ReferenceEquals(_activeVideoTexture, tex))
            {
                OnVideoTextureChanging?.Invoke();
            }
            _activeVideoTexture = null;

            // Publish the replacement on the next Unity frame, never from inside WebRTC's resize
            // callback. Normal ingest remains fully decoupled between resolution changes.
            _pendingVideoTexture = tex;
            _pendingVideoTextureFrame = Time.frameCount;
            Record3DLogger.WebRTC($"Video texture available: {tex.width}x{tex.height} ({tex.graphicsFormat}); activating next frame.");
        }

        public void Update()
        {
            if (_isDisposed || _isStopping || !_isConnected)
                return;

            if (_pendingVideoTexture != null && Time.frameCount > _pendingVideoTextureFrame)
            {
                _activeVideoTexture = _pendingVideoTexture;
                _pendingVideoTexture = null;
                _pendingVideoTextureFrame = -1;
            }

            Texture tex = _activeVideoTexture;
            if (tex == null && _videoTrack != null)
            {
                Texture trackTexture = _videoTrack.Texture;
                if (trackTexture != null)
                {
                    _pendingVideoTexture = trackTexture;
                    _pendingVideoTextureFrame = Time.frameCount;
                }
                return;
            }

            if (tex == null)
                return;

            int currentRgbW = tex.width / 2;
            int currentRgbH = tex.height;

            if (currentRgbW <= 0 || currentRgbH <= 0)
                return;

            _latestMetadata.currentRgbWidth = currentRgbW;
            _latestMetadata.currentRgbHeight = currentRgbH;

            if (_activeK == null || _activeK.Length < 9)
            {
                if (!_loggedMissingActiveK)
                {
                    _loggedMissingActiveK = true;
                    Record3DLogger.Error(Record3DLogCategory.Metadata, "[Record3D] _activeK is null or invalid! No camera intrinsics received from DataChannel yet. Skipping unprojection.");
                }
                return;
            }
            _loggedMissingActiveK = false;

            // Recompute unprojection every frame based on the active texture resolution and intrinsics.
            // As WebRTC dynamically adapts video resolution, this ensures 3D metric scale remains 100% constant!
            _latestMetadata.unprojection = Record3DCoordinateConverter.ComputeUnprojection(
                _activeK,
                currentRgbW,
                currentRgbH,
                _originalRgbWidth,
                _originalRgbHeight);

            OnFrameReceived?.Invoke(tex, _latestMetadata);
        }

        private void ParseDataChannelJson(string json)
        {
            try
            {
                Record3DDataChannelPayload payload = null;

                try
                {
                    payload = JsonUtility.FromJson<Record3DDataChannelPayload>(json);
                }
                catch
                {
                    // Fall back to direct extraction below
                }

                if (payload != null)
                {
                    _latestMetadata.timestamp = payload.timestamp;
                    if (payload.originalRgbWidth > 0) _originalRgbWidth = payload.originalRgbWidth;
                    if (payload.originalRgbHeight > 0) _originalRgbHeight = payload.originalRgbHeight;
                }

                // 1. Intrinsics matrix
                float[] k = (payload != null && payload.intrinsicMatrixRgb != null && payload.intrinsicMatrixRgb.Length >= 9)
                    ? payload.intrinsicMatrixRgb
                    : ExtractFloatArray(json, "\"intrinsicMatrixRgb\":");

                if (k != null && k.Length >= 9)
                {
                    _activeK = k;
                    int curW = _latestMetadata.currentRgbWidth > 0 ? _latestMetadata.currentRgbWidth : _originalRgbWidth;
                    int curH = _latestMetadata.currentRgbHeight > 0 ? _latestMetadata.currentRgbHeight : _originalRgbHeight;
                    _latestMetadata.unprojection = Record3DCoordinateConverter.ComputeUnprojection(_activeK, curW, curH, _originalRgbWidth, _originalRgbHeight);
                }
                else if (_activeK == null)
                {
                    Record3DLogger.Error(Record3DLogCategory.Metadata, "[Record3D] Missing or invalid intrinsicMatrixRgb in DataChannel packet!");
                }

                // 2. Camera pose: [qx, qy, qz, qw, tx, ty, tz]
                float[] pose = (payload != null && payload.pose != null && payload.pose.Length >= 7)
                    ? payload.pose
                    : ExtractFloatArray(json, "\"pose\":");

                if (pose != null && pose.Length >= 7)
                {
                    Record3DCoordinateConverter.ConvertPose(
                        pose,
                        out _latestMetadata.cameraPosition,
                        out _latestMetadata.cameraRotation,
                        out _latestMetadata.poseMatrix
                    );

                    _poseUpdateCount++;
                    if (_poseUpdateCount == 1 || _poseUpdateCount % 300 == 0)
                    {
                        var pos = _latestMetadata.cameraPosition;
                        var rot = _latestMetadata.cameraRotation;
                        Record3DLogger.Metadata($"[Record3D Pose #{_poseUpdateCount}] Pos: ({pos.x:+0.000;-0.000}, {pos.y:+0.000;-0.000}, {pos.z:+0.000;-0.000})m | Rot: ({rot.eulerAngles.x:F1}°, {rot.eulerAngles.y:F1}°, {rot.eulerAngles.z:F1}°)");
                    }
                }
            }
            catch (Exception ex)
            {
                Record3DLogger.Metadata($"Error parsing DataChannel JSON: {ex.Message}", Record3DLogLevel.Warning);
            }
        }

        private static float[] ExtractFloatArray(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int keyIdx = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIdx < 0) return null;
            int start = json.IndexOf('[', keyIdx);
            if (start < 0) return null;
            int end = json.IndexOf(']', start);
            if (end <= start) return null;

            string inner = json.Substring(start + 1, end - start - 1);
            string[] items = inner.Split(',');
            if (items.Length == 0) return null;

            var result = new float[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                float.TryParse(items[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result[i]);
            }
            return result;
        }



        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            StopReceiving();
            if (_webrtcUpdateCoroutine != null && _coroutineHost != null)
            {
                _coroutineHost.StopCoroutine(_webrtcUpdateCoroutine);
                _webrtcUpdateCoroutine = null;
            }
        }

        private bool IsSessionCurrent(int session)
        {
            return !_isDisposed && !_isStopping && session == _sessionGeneration;
        }

        private static void SafeCloseAndDispose(RTCDataChannel channel)
        {
            if (channel == null) return;
            try { channel.Close(); } catch (Exception) { }
            try { channel.Dispose(); } catch (Exception) { }
        }

        private static void SafeDispose(IDisposable disposable)
        {
            if (disposable == null) return;
            try { disposable.Dispose(); } catch (Exception) { }
        }

        // Log codec lines only. The full SDP also contains network addresses and credentials.
        private static string DescribeVideoCodecs(string sdp)
        {
            if (string.IsNullOrEmpty(sdp)) return "(empty)";

            var summary = new StringBuilder();
            bool inVideoSection = false;
            foreach (string rawLine in sdp.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("m=", StringComparison.Ordinal))
                {
                    if (inVideoSection) break;
                    if (!line.StartsWith("m=video ", StringComparison.Ordinal)) continue;
                    inVideoSection = true;
                    summary.Append(line);
                }
                else if (inVideoSection && line.StartsWith("a=rtpmap:", StringComparison.Ordinal))
                {
                    summary.Append(" | ").Append(line);
                    if (summary.Length > 700) break;
                }
            }
            return summary.Length == 0 ? "(no video m-line)" : summary.ToString();
        }
    }
}
