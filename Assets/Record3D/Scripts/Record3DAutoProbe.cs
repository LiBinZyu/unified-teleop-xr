using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Record3D
{
    /// <summary>
    /// Decoupled, high-performance background service that automatically detects
    /// whether the Record3D streaming server on iOS is active and reachable,
    /// triggering auto-connection when online and auto-reconnection when lost.
    /// 
    /// Encapsulation Design:
    /// - All parameters and probing routines are strictly private.
    /// - IP and Port are owned and held exclusively by Record3DManager.
    /// - Probing uses non-intrusive TCP socket ping, avoiding dummy SDP offer generation.
    /// - Probing interval is fixed at 5 seconds.
    /// </summary>
    public class Record3DAutoProbe : IDisposable
    {
        private const float ProbeIntervalSeconds = 5.0f;

        private CancellationTokenSource _cts;
        private Task _probeTask;
        private bool _enableAutoProbe = true;
        private bool _isServiceOnline = false;
        private bool _isRunning = false;

        private readonly Func<string> _getHostFunc;
        private readonly Func<int> _getPortFunc;
        private readonly Func<bool> _isBusyFunc;
        private readonly Action _onConnectAction;
        private readonly Action<bool> _onStatusChanged;

        /// <summary>
        /// Enable or disable the AutoProbe background detection service.
        /// </summary>
        public void SetEnable(bool enable)
        {
            if (_enableAutoProbe == enable && _isRunning == enable) return;
            _enableAutoProbe = enable;
            Record3DLogger.WebRTC($"AutoProbe enable state set to: {_enableAutoProbe}");
            if (_enableAutoProbe && !_isRunning)
            {
                StartInternal();
            }
            else if (!_enableAutoProbe && _isRunning)
            {
                StopInternal();
            }
        }

        public bool EnableAutoProbe
        {
            get => _enableAutoProbe;
            set => SetEnable(value);
        }

        public bool IsServiceOnline => _isServiceOnline;

        public Record3DAutoProbe(
            Func<string> getHostFunc,
            Func<int> getPortFunc,
            Func<bool> isBusyFunc,
            Action onConnectAction,
            Action<bool> onStatusChanged = null)
        {
            _getHostFunc = getHostFunc;
            _getPortFunc = getPortFunc;
            _isBusyFunc = isBusyFunc;
            _onConnectAction = onConnectAction;
            _onStatusChanged = onStatusChanged;
        }

        private void StartInternal()
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            _probeTask = Task.Run(() => ProbeLoopAsync(_cts.Token));
            Record3DLogger.WebRTC("Record3DAutoProbe background service started (interval: 5s).");
        }

        private void StopInternal()
        {
            if (!_isRunning) return;
            _isRunning = false;
            try
            {
                _cts?.Cancel();
            }
            catch (Exception) { }
            _cts?.Dispose();
            _cts = null;
            _probeTask = null;
            _isServiceOnline = false;
            Record3DLogger.WebRTC("Record3DAutoProbe background service stopped.");
        }

        private async Task ProbeLoopAsync(CancellationToken token)
        {
            // Initial delay before first check
            try
            {
                await Task.Delay(500, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_enableAutoProbe)
                    {
                        string host = _getHostFunc?.Invoke();
                        int port = _getPortFunc?.Invoke() ?? 80;
                        bool isBusy = _isBusyFunc != null && _isBusyFunc();

                        if (!string.IsNullOrEmpty(host) && !isBusy)
                        {
                            bool online = await CheckPortReachableAsync(host, port, 1500).ConfigureAwait(false);
                            if (online)
                            {
                                if (!_isServiceOnline)
                                {
                                    _isServiceOnline = true;
                                    Record3DLogger.WebRTC($"AutoProbe: Record3D service is ONLINE at {host}:{port}. Triggering auto-connection.");
                                    _onStatusChanged?.Invoke(true);
                                }
                                _onConnectAction?.Invoke();
                            }
                            else
                            {
                                if (_isServiceOnline)
                                {
                                    _isServiceOnline = false;
                                    Record3DLogger.WebRTC($"AutoProbe: Record3D service is OFFLINE at {host}:{port}.");
                                    _onStatusChanged?.Invoke(false);
                                }
                            }
                        }
                        else if (isBusy)
                        {
                            _isServiceOnline = true;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Record3DLogger.WebRTC($"AutoProbe probe note: {ex.Message}", Record3DLogLevel.Warning);
                }

                try
                {
                    await Task.Delay((int)(ProbeIntervalSeconds * 1000f), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static async Task<bool> CheckPortReachableAsync(string host, int port, int timeoutMs = 1500)
        {
            try
            {
                using var client = new TcpClient();
                Task connectTask;
                if (IPAddress.TryParse(host, out var ipAddress))
                {
                    connectTask = client.ConnectAsync(ipAddress, port);
                }
                else
                {
                    connectTask = client.ConnectAsync(host, port);
                }

                var timeoutTask = Task.Delay(timeoutMs);
                var completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (completed == connectTask)
                {
                    await connectTask.ConfigureAwait(false);
                    return client.Connected;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            StopInternal();
        }
    }
}
