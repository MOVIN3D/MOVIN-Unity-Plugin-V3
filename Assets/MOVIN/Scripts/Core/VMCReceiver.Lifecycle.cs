using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public partial class VMCReceiver
{
    protected virtual void OnEnable()
    {
        ApplyAppFrameRatePolicy();
        StartReceiver();
    }

    protected virtual void OnDisable()
    {
        StopReceiver();
    }

    public void StartReceiver()
    {
        if (_running) return;
        try
        {
            ApplyRunInBackgroundOverride();
            _remoteAny = new IPEndPoint(IPAddress.Any, 0);
            var local = string.IsNullOrWhiteSpace(bindAddress) ? IPAddress.Any : IPAddress.Parse(bindAddress);
            _udp = new UdpClient(new IPEndPoint(local, listenPort));
            _udp.Client.ReceiveBufferSize = 1 << 20; // 1MB
            ClearFrameBuffer();
            _running = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "VMCReceiver" };
            _thread.Start();
            Debug.Log($"VMCReceiver listening on {local}:{listenPort}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"VMCReceiver failed to start: {ex}");
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
        try { _thread?.Join(100); } catch { /* ignore */ }
        _thread = null;
        ClearFrameBuffer();
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

        _previousRunInBackground = Application.runInBackground;
        Application.runInBackground = true;
        _runInBackgroundOverridden = true;
    }

    private void RestoreRunInBackgroundOverride()
    {
        if (!_runInBackgroundOverridden)
            return;

        Application.runInBackground = _previousRunInBackground;
        _runInBackgroundOverridden = false;
    }

    private void ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                var data = _udp.Receive(ref _remoteAny);
                var packetSequence = Interlocked.Increment(ref _packetSequence);
                Interlocked.Increment(ref _packetsReceived);
                Interlocked.Exchange(ref _lastPacketUtcTicks, DateTime.UtcNow.Ticks);
                OSCParser.ParsePacket(data, 0, data.Length, (msg) =>
                {
                    msg.PacketData = data;
                    msg.PacketLength = data.Length;
                    msg.PacketSequence = packetSequence;
                    MarkMessageReceived(msg);
                    if (TryHandlePrivateReceiveThreadControlMessage(msg))
                    {
                        MarkMessageDispatched();
                    }
                    else if (TryBufferMotionMessage(msg))
                    {
                        try
                        {
                            RecordPrivateRawPacket(msg);
                            MarkMessageDispatched();
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref _processingErrors);
                            Debug.LogWarning($"Motion buffer error for {msg.Address}: {ex.Message}");
                        }
                    }
                    else
                    {
                        _queue.Enqueue(msg);
                    }
                });
            }
            catch (SocketException)
            {
                // likely closing; ignore
                if (!_running) break;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"VMCReceiver receive error: {ex.Message}");
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
                RecordPrivateRawPacket(msg);
                DispatchVMC(msg);
                MarkMessageDispatched();
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _processingErrors);
                Debug.LogWarning($"Dispatch error for {msg.Address}: {ex.Message}");
            }
        }

        ApplyBufferedFrameIfAvailable();
    }
}
