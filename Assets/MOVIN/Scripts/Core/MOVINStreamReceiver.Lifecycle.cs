using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using MOVIN.OSC;

namespace MOVIN
{
    public partial class MOVINStreamReceiver
    {
        protected virtual void OnEnable()
        {
            StartReceiver();
        }

        protected virtual void OnDisable()
        {
            StopReceiver();
        }

        public void StartReceiver()
        {
            if (_running) return;
            if (_thread != null && _thread.IsAlive)
            {
                Debug.LogError("The previous motion receiver is still stopping. Retry StartReceiver after it exits.");
                return;
            }
            try
            {
                ApplyAppFrameRatePolicy();
                ApplyRunInBackgroundOverride();
                ResolveStreamAddresses();
                _remoteAny = new IPEndPoint(IPAddress.Any, 0);
                var local = string.IsNullOrWhiteSpace(bindAddress) ? IPAddress.Any : IPAddress.Parse(bindAddress);
                _udp = new UdpClient(new IPEndPoint(local, listenPort));
                _udp.Client.ReceiveBufferSize = 1 << 20; // 1MB
                ClearFrameBuffer();
                ClearPointCloudBuffer();
                ResetStudioStatus();
                _running = true;
                _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "MOVINStreamReceiver" };
                _thread.Start();
                Debug.Log($"MOVINStreamReceiver listening on {local}:{listenPort} for {_rootAddress} and {_boneAddress}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"MOVINStreamReceiver failed to start: {ex}");
                _running = false;
                _udp?.Close();
                _udp = null;
                RestoreRunInBackgroundOverride();
                RestoreAppFrameRatePolicy();
            }
        }

        public void StopReceiver()
        {
            _running = false;
            try { _udp?.Close(); } catch { /* ignore */ }
            _udp = null;
            if (_thread != null && _thread.Join(100))
            {
                _thread = null;
            }
            while (_queue.TryDequeue(out _)) { }
            ClearFrameBuffer();
            ClearPointCloudBuffer();
            OnPrivateReceiverStopping();
            RestoreRunInBackgroundOverride();
            RestoreAppFrameRatePolicy();
        }

        private void ApplyAppFrameRatePolicy()
        {
            if (_frameRatePolicyOverridden)
                return;

            lock (FrameRatePolicyLock)
            {
                if (_frameRatePolicyRefCount == 0)
                {
                    _sharedPreviousVSyncCount = QualitySettings.vSyncCount;
                    _sharedPreviousTargetFrameRate = Application.targetFrameRate;
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = TargetFrameRate;
                }

                _frameRatePolicyRefCount++;
                _frameRatePolicyOverridden = true;
            }
        }

        private void RestoreAppFrameRatePolicy()
        {
            if (!_frameRatePolicyOverridden)
                return;

            lock (FrameRatePolicyLock)
            {
                _frameRatePolicyOverridden = false;
                _frameRatePolicyRefCount = Math.Max(0, _frameRatePolicyRefCount - 1);
                if (_frameRatePolicyRefCount == 0)
                {
                    QualitySettings.vSyncCount = _sharedPreviousVSyncCount;
                    Application.targetFrameRate = _sharedPreviousTargetFrameRate;
                }
            }
        }

        private void ApplyRunInBackgroundOverride()
        {
            if (!forceRunInBackground || _runInBackgroundOverridden)
                return;

            if (_runInBackgroundRefCount == 0)
            {
                _previousRunInBackground = Application.runInBackground;
                Application.runInBackground = true;
            }
            _runInBackgroundRefCount++;
            _runInBackgroundOverridden = true;
        }

        private void RestoreRunInBackgroundOverride()
        {
            if (!_runInBackgroundOverridden)
                return;

            _runInBackgroundOverridden = false;
            _runInBackgroundRefCount--;
            if (_runInBackgroundRefCount == 0)
            {
                Application.runInBackground = _previousRunInBackground;
            }
        }

        private void ReceiveLoop()
        {
            var udp = _udp;
            while (_running)
            {
                try
                {
                    var data = udp.Receive(ref _remoteAny);
                    var packetSequence = Interlocked.Increment(ref _packetSequence);
                    Interlocked.Increment(ref _packetsReceived);
                    Interlocked.Exchange(ref _lastPacketUtcTicks, DateTime.UtcNow.Ticks);
                    OSCParser.ParsePacket(data, 0, data.Length, (msg) =>
                    {
                        if (!_running) return;
                        msg.PacketData = data;
                        msg.PacketLength = data.Length;
                        msg.PacketSequence = packetSequence;
                        MarkMessageReceived(msg);
                        try
                        {
                            if (TryHandleStatusRequest(msg))
                            {
                                MarkMessageDispatched();
                            }
                            else if (TryHandlePrivateReceiveThreadControlMessage(msg))
                            {
                                MarkMessageDispatched();
                            }
                            else if (TryBufferMotionMessage(msg))
                            {
                                lock (_statusLock) { _motionSource = _remoteAny; }
                                RecordPrivateRawPacket(msg);
                                MarkMessageDispatched();
                            }
                            else if (TryBufferPointCloudMessage(msg))
                            {
                                lock (_statusLock) { _cloudSource = _remoteAny; }
                                MarkMessageDispatched();
                            }
                            else if (validationLogging && msg.Address == ValidationEndAddress)
                            {
                                if (_queue.Count >= 64)
                                {
                                    throw new FormatException("Too many pending validation controls.");
                                }
                                _queue.Enqueue(msg);
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref _processingErrors);
                            Debug.LogWarning($"Motion buffer error for {msg.Address}: {ex.Message}");
                        }
                    });
                }
                catch (SocketException)
                {
                    // likely closing; ignore
                    if (!_running) break;
                }
                catch (ObjectDisposedException) when (!_running) { }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _processingErrors);
                    Debug.LogWarning($"MOVINStreamReceiver receive error: {ex.Message}");
                }
            }
        }

        protected virtual void Update()
        {
            Interlocked.Increment(ref _mainThreadFrames);

            int safety = 10000; // process up to N msgs per frame to avoid stalls
            while (safety-- > 0 && _queue.TryDequeue(out var msg))
            {
                try
                {
                    DispatchMainThreadMessage(msg);
                    MarkMessageDispatched();
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _processingErrors);
                    Debug.LogWarning($"Dispatch error for {msg.Address}: {ex.Message}");
                }
            }

            ApplyBufferedFrameIfAvailable();
            ApplyPointCloudIfAvailable();
            ReplyToStudio();
        }
    }
}
