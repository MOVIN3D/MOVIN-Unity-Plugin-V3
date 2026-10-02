using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using UnityEngine;
using MOVIN.OSC;

namespace MOVIN
{
    public partial class MOVINStreamReceiver
    {
        // A partially filled frame is treated as complete once this long passes with no newer
        // frame arriving, so playback is not stalled waiting on a dropped trailing packet.
        private const double FrameStalenessSeconds = 0.05;
        // The wire format has no session id. Recover after one second without an accepted frame.
        private const double StreamRestartSeconds = 1.0;
        private const double SourceTimeoutSeconds = 2.0;
        private const int MaxBonesPerFrame = 1024;

        protected class FramePose
        {
            private readonly List<BonePose> _bones = new List<BonePose>();
            private readonly Dictionary<string, int> _boneIndices = new Dictionary<string, int>();

            public FramePose(int wireFrame, int frame)
            {
                WireFrame = wireFrame;
                Frame = frame;
            }

            public int WireFrame { get; }
            public int Frame { get; }
            public long LastReceiveUtcTicks { get; private set; }
            public bool HasRoot { get; private set; }
            public string RootName { get; private set; }
            public Vector3 RootPosition { get; private set; }
            public Quaternion RootRotation { get; private set; }
            public Vector3? RootScale { get; private set; }
            public List<BonePose> Bones => _bones;

            public void SetRoot(string name, Vector3 position, Quaternion rotation, Vector3? scale)
            {
                RootName = name;
                RootPosition = position;
                RootRotation = rotation;
                RootScale = scale;
                HasRoot = true;
                LastReceiveUtcTicks = DateTime.UtcNow.Ticks;
            }

            public void SetBone(string name, Vector3 position, Quaternion rotation)
            {
                var bone = new BonePose(name, position, rotation);
                if (_boneIndices.TryGetValue(name, out var index))
                {
                    _bones[index] = bone;
                }
                else
                {
                    if (_bones.Count >= MaxBonesPerFrame)
                    {
                        throw new FormatException("Too many bones in a motion frame.");
                    }
                    _boneIndices[name] = _bones.Count;
                    _bones.Add(bone);
                }

                LastReceiveUtcTicks = DateTime.UtcNow.Ticks;
            }
        }

        protected struct BonePose
        {
            public BonePose(string name, Vector3 position, Quaternion rotation)
            {
                Name = name;
                Position = position;
                Rotation = rotation;
            }

            public string Name { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }

        private void ClearFrameBuffer()
        {
            lock (_frameLock)
            {
                _frameBuffer.Clear();
                _staleFrameScratch.Clear();
                _currentBufferedFrame = int.MinValue;
                _latestCompleteFrame = int.MinValue;
                _lastAppliedBufferedFrame = int.MinValue;
                _currentBufferedFrameTicks = 0;
                _lastAcceptedFrameTimestamp = 0;
            }
        }

        private int GetBufferedFrameCount()
        {
            lock (_frameLock)
            {
                CompleteCurrentFrameIfStale();
                if (_latestCompleteFrame == int.MinValue)
                    return 0;

                var count = 0;
                foreach (var frame in _frameBuffer.Keys)
                {
                    if (IsPendingCompleteFrameLocked(frame))
                        count++;
                }

                return count;
            }
        }

        /// <summary>
        /// Receive-thread entry point. Returns true when the message is a motion message, which is
        /// consumed here whether or not it could be buffered; everything else goes to the main thread.
        /// </summary>
        private bool TryBufferMotionMessage(OSCMessage msg)
        {
            EnsureStreamAddresses();

            if (IsRootAddress(msg.Address))
            {
                BufferRootPoseMessage(msg);
                return true;
            }

            if (IsBoneAddress(msg.Address))
            {
                BufferBonePoseMessage(msg);
                return true;
            }

            return false;
        }

        // (int frame, string name, pos xyz, rot xyzw[, scale xyz])
        private void BufferRootPoseMessage(OSCMessage msg)
        {
            if (!TryReadMotionHeader(msg, out var wireFrame, out var offset, out var rootName))
                return;

            var position = ReadVector3(msg, offset + 1);
            var rotation = ReadQuaternion(msg, offset + 4);
            Vector3? scale = msg.Args.Length == offset + 11 ? ReadVector3(msg, offset + 8) : null;

            MarkPoseMessage(rootName, wireFrame);
            BufferRootPose(wireFrame, rootName, position, rotation, scale);
        }

        // (int frame, string name, pos xyz, rot xyzw)
        private void BufferBonePoseMessage(OSCMessage msg)
        {
            if (!TryReadMotionHeader(msg, out var wireFrame, out var offset, out var boneName))
                return;

            var position = ReadVector3(msg, offset + 1);
            var rotation = ReadQuaternion(msg, offset + 4);

            MarkPoseMessage(boneName, wireFrame);
            BufferBonePose(wireFrame, boneName, position, rotation);
        }

        /// <summary>
        /// Reads the leading frame index and bone name shared by both motion messages and checks
        /// that the pose arguments follow. A message without a frame index cannot be placed in a
        /// frame, so it is dropped rather than applied out of order.
        /// </summary>
        private bool TryReadMotionHeader(OSCMessage msg, out int wireFrame, out int offset, out string name)
        {
            const int poseArgCount = 8; // name + pos xyz + rot xyzw

            name = null;
            if (!TryReadFrameIndex(msg, out wireFrame, out offset)
                || (msg.Args.Length != offset + poseArgCount
                    && !(IsRootAddress(msg.Address) && msg.Args.Length == offset + poseArgCount + 3))
                || msg.Args[offset] is not string streamedName
                || string.IsNullOrWhiteSpace(streamedName)
                || streamedName.Length > 256)
            {
                System.Threading.Interlocked.Increment(ref _processingErrors);
                Debug.LogWarning($"Invalid motion frame header: {msg.Address} {msg.Types}");
                return false;
            }

            for (var i = offset + 1; i < msg.Args.Length; i++)
            {
                if (msg.Args[i] is not float value || float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new FormatException("Motion pose components must be finite OSC floats.");
                }
            }

            var rotation = ReadQuaternion(msg, offset + 4);
            var magnitudeSquared = Quaternion.Dot(rotation, rotation);
            if (float.IsInfinity(magnitudeSquared) || magnitudeSquared < 0.000001f)
            {
                throw new FormatException("Motion rotation must be a nonzero finite quaternion.");
            }
            name = streamedName;
            return true;
        }

        private static Vector3 ReadVector3(OSCMessage msg, int index)
        {
            return new Vector3((float)msg.Args[index], (float)msg.Args[index + 1], (float)msg.Args[index + 2]);
        }

        private static Quaternion ReadQuaternion(OSCMessage msg, int index)
        {
            return new Quaternion((float)msg.Args[index], (float)msg.Args[index + 1], (float)msg.Args[index + 2], (float)msg.Args[index + 3]);
        }

        private void BufferRootPose(int wireFrame, string rootName, Vector3 position, Quaternion rotation, Vector3? scale)
        {
            lock (_frameLock)
            {
                if (AcceptMotionSource())
                {
                    var frame = GetFrameForBufferLocked(wireFrame);
                    frame?.SetRoot(rootName, position, rotation, scale);
                }
            }
        }

        private void BufferBonePose(int wireFrame, string boneName, Vector3 position, Quaternion rotation)
        {
            lock (_frameLock)
            {
                if (AcceptMotionSource())
                {
                    var frame = GetFrameForBufferLocked(wireFrame);
                    frame?.SetBone(boneName, position, rotation);
                }
            }
        }

        // Called under _frameLock after validating the incoming pose.
        private bool AcceptMotionSource()
        {
            lock (_statusLock)
            {
                var available = _motionSource == null || Equals(_motionSource, _remoteAny)
                    || (Stopwatch.GetTimestamp() - _lastAcceptedFrameTimestamp) / (double)Stopwatch.Frequency >= SourceTimeoutSeconds;
                if (available && !Equals(_motionSource, _remoteAny))
                {
                    ClearFrameBuffer();
                    lock (_monitorLock) { _lastAppliedFrameForMonitor = int.MinValue; }
                    _motionSource = _remoteAny;
                    _statusMotionFrame = -1;
                    _statusMotionAt = 0;
                    _statusMotionCount = 0;
                    _statusMotionFps = 0;
                }
                return available;
            }
        }

        private FramePose GetFrameForBufferLocked(int wireFrame)
        {
            var frame = WireFrameToFrame(wireFrame);
            var now = Stopwatch.GetTimestamp();
            if (_lastAcceptedFrameTimestamp != 0
                && (now - _lastAcceptedFrameTimestamp) / (double)Stopwatch.Frequency >= StreamRestartSeconds)
            {
                ClearFrameBuffer();
                lock (_monitorLock)
                {
                    _lastAppliedFrameForMonitor = int.MinValue;
                }
                lock (_statusLock) { _statusMotionFrame = -1; }
            }
            if (frame <= _lastAppliedBufferedFrame)
                return null;

            _lastAcceptedFrameTimestamp = now;
            if (_currentBufferedFrame == int.MinValue)
            {
                _currentBufferedFrame = frame;
                _currentBufferedFrameTicks = DateTime.UtcNow.Ticks;
            }
            else if (frame > _currentBufferedFrame)
            {
                _latestCompleteFrame = Math.Max(_latestCompleteFrame, _currentBufferedFrame);
                _currentBufferedFrame = frame;
                _currentBufferedFrameTicks = DateTime.UtcNow.Ticks;
            }
            else if (frame == _currentBufferedFrame)
            {
                _currentBufferedFrameTicks = DateTime.UtcNow.Ticks;
            }
            else
            {
                _latestCompleteFrame = Math.Max(_latestCompleteFrame, frame);
            }

            if (!_frameBuffer.TryGetValue(frame, out var pose))
            {
                pose = new FramePose(wireFrame, frame);
                _frameBuffer[frame] = pose;
                lock (_monitorLock)
                {
                    _inputFramesReceived++;
                }
                // Count each accepted wire frame once, independently of main-thread playback.
                lock (_statusLock)
                {
                    _statusMotionFrame = Math.Max(_statusMotionFrame, frame);
                    _statusMotionAt = now;
                    _statusMotionCount++;
                }

                // Keep at most the configured completed backlog plus the frame being received.
                var limit = Mathf.Clamp(maxBufferedFramesBeforeDrop, 1, 120) + 1;
                while (_frameBuffer.Count > limit)
                {
                    var oldest = _frameBuffer.Keys.Min();
                    _frameBuffer.Remove(oldest);
                    _lastAppliedBufferedFrame = Math.Max(_lastAppliedBufferedFrame, oldest);
                }
            }

            return frame > _lastAppliedBufferedFrame ? pose : null;
        }

        private void ApplyBufferedFrameIfAvailable()
        {
            if (TryTakeFrameForPlayback(out var frame))
                ApplyBufferedFrame(frame);
        }

        private bool TryTakeFrameForPlayback(out FramePose frame)
        {
            return TryTakeBufferedFrame(out frame, true);
        }

        private bool TryTakeNextFrameForPlayback(out FramePose frame)
        {
            return TryTakeBufferedFrame(out frame, false);
        }

        private bool TryTakeBufferedFrame(out FramePose frame, bool dropWhenPlaybackIsLagging)
        {
            lock (_frameLock)
            {
                CompleteCurrentFrameIfStale();
                if (_latestCompleteFrame == int.MinValue
                    || !TryFindPendingCompleteFrameRangeLocked(out var pendingCount, out var earliestFrame, out var latestFrame))
                {
                    frame = null;
                    return false;
                }

                var shouldDropToLatest = dropWhenPlaybackIsLagging
                    && pendingCount >= Mathf.Clamp(maxBufferedFramesBeforeDrop, 1, 120);
                var frameNumber = shouldDropToLatest
                    ? latestFrame
                    : earliestFrame;
                if (!_frameBuffer.TryGetValue(frameNumber, out var bufferedFrame))
                {
                    frame = null;
                    return false;
                }

                frame = bufferedFrame;
                _lastAppliedBufferedFrame = frameNumber;
                _staleFrameScratch.Clear();
                foreach (var staleFrame in _frameBuffer.Keys)
                {
                    if (staleFrame <= _lastAppliedBufferedFrame)
                        _staleFrameScratch.Add(staleFrame);
                }

                foreach (var staleFrame in _staleFrameScratch)
                    _frameBuffer.Remove(staleFrame);
                _staleFrameScratch.Clear();

                return true;
            }
        }

        private bool TryFindPendingCompleteFrameRangeLocked(out int count, out int earliestFrame, out int latestFrame)
        {
            count = 0;
            earliestFrame = int.MaxValue;
            latestFrame = int.MinValue;

            foreach (var frame in _frameBuffer.Keys)
            {
                if (!IsPendingCompleteFrameLocked(frame))
                    continue;

                count++;
                if (frame < earliestFrame)
                    earliestFrame = frame;
                if (frame > latestFrame)
                    latestFrame = frame;
            }

            return count > 0;
        }

        private bool IsPendingCompleteFrameLocked(int frame)
        {
            return frame > _lastAppliedBufferedFrame && frame <= _latestCompleteFrame;
        }

        private void CompleteCurrentFrameIfStale()
        {
            if (_currentBufferedFrame == int.MinValue || _latestCompleteFrame >= _currentBufferedFrame)
                return;

            var age = (DateTime.UtcNow - new DateTime(_currentBufferedFrameTicks, DateTimeKind.Utc)).TotalSeconds;
            if (age >= FrameStalenessSeconds)
                _latestCompleteFrame = Math.Max(_latestCompleteFrame, _currentBufferedFrame);
        }

        private void ApplyBufferedFrame(FramePose frame)
        {
#if MOVIN_STREAM_VALIDATION
            var privatePoseScope = EnterPrivatePoseFrame(frame.WireFrame);
#endif
            var previousDispatchFrame = _currentDispatchFrame;
            _currentDispatchFrame = frame.Frame;
            try
            {
                ApplyFramePose(frame);
                MarkMonitorPoseApplied(frame);
            }
            finally
            {
#if MOVIN_STREAM_VALIDATION
                ExitPrivatePoseFrame(privatePoseScope);
#endif
                _currentDispatchFrame = previousDispatchFrame;
            }
        }

        protected virtual void ApplyFramePose(FramePose frame)
        {
            if (frame.HasRoot)
                OnRootPose?.Invoke(frame.RootName, frame.RootPosition, frame.RootRotation, frame.RootScale);

            foreach (var bone in frame.Bones)
            {
                BonePoses[bone.Name] = (bone.Position, bone.Rotation);
                OnBonePose?.Invoke(bone.Name, bone.Position, bone.Rotation);
            }
        }
    }
}
