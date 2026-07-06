using System;
using System.Threading;
using UnityEngine;

public partial class VMCReceiver
{
    public struct MonitorSnapshot
    {
        public bool IsRunning;
        public int ListenPort;
        public string BindAddress;
        public int QueuedMessages;
        public long MainThreadFrames;
        public long PacketsReceived;
        public long MessagesReceived;
        public long MessagesDispatched;
        public long ProcessingErrors;
        public long InputFramesReceived;
        public long AppliedFramesReceived;
        public long DroppedFrameCount;
        public int BufferedFrameCount;
        public double PlaybackLatencyMs;
        public long LastPacketUtcTicks;
        public long LastDispatchUtcTicks;
        public string LastOscAddress;
        public string LastPoseName;
        public int LastWireFrame;
        public int LastFrame;
        public int LastDroppedFrameStart;
        public int LastDroppedFrameEnd;
        public bool ForceRunInBackground;
        public bool ApplicationRunInBackground;
    }

    public MonitorSnapshot GetMonitorSnapshot()
    {
        var bufferedFrameCount = GetBufferedFrameCount();
        lock (_monitorLock)
        {
            return new MonitorSnapshot
            {
                IsRunning = _running,
                ListenPort = listenPort,
                BindAddress = bindAddress,
                QueuedMessages = _queue.Count,
                MainThreadFrames = Interlocked.Read(ref _mainThreadFrames),
                PacketsReceived = Interlocked.Read(ref _packetsReceived),
                MessagesReceived = Interlocked.Read(ref _messagesReceived),
                MessagesDispatched = Interlocked.Read(ref _messagesDispatched),
                ProcessingErrors = Interlocked.Read(ref _processingErrors),
                InputFramesReceived = _inputFramesReceived,
                AppliedFramesReceived = _appliedFramesReceived,
                DroppedFrameCount = _droppedFrameCount,
                BufferedFrameCount = bufferedFrameCount,
                PlaybackLatencyMs = _lastPlaybackLatencyMs,
                LastPacketUtcTicks = Interlocked.Read(ref _lastPacketUtcTicks),
                LastDispatchUtcTicks = Interlocked.Read(ref _lastDispatchUtcTicks),
                LastOscAddress = _lastOscAddress,
                LastPoseName = _lastPoseName,
                LastWireFrame = _lastWireFrame,
                LastFrame = _lastFrame,
                LastDroppedFrameStart = _lastDroppedFrameStart,
                LastDroppedFrameEnd = _lastDroppedFrameEnd,
                ForceRunInBackground = forceRunInBackground,
                ApplicationRunInBackground = Application.runInBackground,
            };
        }
    }

    public void ResetMonitorStats()
    {
        Interlocked.Exchange(ref _mainThreadFrames, 0);
        Interlocked.Exchange(ref _packetsReceived, 0);
        Interlocked.Exchange(ref _messagesReceived, 0);
        Interlocked.Exchange(ref _messagesDispatched, 0);
        Interlocked.Exchange(ref _processingErrors, 0);
        Interlocked.Exchange(ref _lastPacketUtcTicks, 0);
        Interlocked.Exchange(ref _lastDispatchUtcTicks, 0);

        lock (_monitorLock)
        {
            _lastOscAddress = "";
            _lastPoseName = "";
            _lastWireFrame = int.MinValue;
            _lastFrame = int.MinValue;
            _inputFramesReceived = 0;
            _appliedFramesReceived = 0;
            _droppedFrameCount = 0;
            _lastInputFrameForMonitor = int.MinValue;
            _lastAppliedFrameForMonitor = int.MinValue;
            _lastDroppedFrameStart = int.MinValue;
            _lastDroppedFrameEnd = int.MinValue;
            _lastPlaybackLatencyMs = -1.0;
        }

        ClearFrameBuffer();
    }

    private void MarkMessageReceived(OSCMessage msg)
    {
        Interlocked.Increment(ref _messagesReceived);
        lock (_monitorLock)
            _lastOscAddress = msg.Address;
    }

    private void MarkMessageDispatched()
    {
        Interlocked.Increment(ref _messagesDispatched);
        Interlocked.Exchange(ref _lastDispatchUtcTicks, DateTime.UtcNow.Ticks);
    }

    private void MarkPoseMessage(string poseName, bool hasFrame, int wireFrame)
    {
        lock (_monitorLock)
        {
            _lastPoseName = poseName;
            if (!hasFrame)
                return;

            var frame = WireFrameToFrame(wireFrame);
            _lastWireFrame = wireFrame;
            _lastFrame = frame;

            if (frame != _lastInputFrameForMonitor)
            {
                _lastInputFrameForMonitor = frame;
                _inputFramesReceived++;
            }
        }
    }

    protected void MarkMonitorPoseApplied(VMCFramePose frame)
    {
        if (_currentDispatchFrame == int.MinValue)
            return;

        var latencyMs = frame.LastReceiveUtcTicks > 0
            ? (DateTime.UtcNow - new DateTime(frame.LastReceiveUtcTicks, DateTimeKind.Utc)).TotalMilliseconds
            : -1.0;
        lock (_monitorLock)
        {
            if (_currentDispatchFrame != _lastAppliedFrameForMonitor)
            {
                if (_lastAppliedFrameForMonitor != int.MinValue && _currentDispatchFrame > _lastAppliedFrameForMonitor + 1)
                {
                    _lastDroppedFrameStart = _lastAppliedFrameForMonitor + 1;
                    _lastDroppedFrameEnd = _currentDispatchFrame - 1;
                    _droppedFrameCount += _currentDispatchFrame - _lastAppliedFrameForMonitor - 1;
                }

                _lastAppliedFrameForMonitor = _currentDispatchFrame;
                _appliedFramesReceived++;
                _lastPlaybackLatencyMs = latencyMs;
            }
        }
    }
}
