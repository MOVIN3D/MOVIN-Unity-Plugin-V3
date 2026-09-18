using System;
using System.Collections.Generic;
using UnityEngine;
using MOVIN.OSC;

namespace MOVIN
{
    public partial class MotionStreamReceiver
    {
        // A partially filled frame is treated as complete once this long passes with no newer
        // frame arriving, so playback is not stalled waiting on a dropped trailing packet.
        private const double FrameStalenessSeconds = 0.05;

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
            Vector3? scale = msg.Args.Length >= offset + 11 ? ReadVector3(msg, offset + 8) : null;

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
                || msg.Args.Length < offset + poseArgCount
                || msg.Args[offset] is not string streamedName)
            {
                if (verboseLogging)
                    Debug.Log($"Ignored motion message without a frame index or pose arguments: {msg.Address} {msg.Types}");
                return false;
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
                var frame = GetFrameForBufferLocked(wireFrame);
                frame?.SetRoot(rootName, position, rotation, scale);
            }
        }

        private void BufferBonePose(int wireFrame, string boneName, Vector3 position, Quaternion rotation)
        {
            lock (_frameLock)
            {
                var frame = GetFrameForBufferLocked(wireFrame);
                frame?.SetBone(boneName, position, rotation);
            }
        }

        private FramePose GetFrameForBufferLocked(int wireFrame)
        {
            var frame = WireFrameToFrame(wireFrame);
            if (frame <= _lastAppliedBufferedFrame)
                return null;

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
            }

            return pose;
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
                    && pendingCount >= Mathf.Max(1, maxBufferedFramesBeforeDrop);
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
            var privatePoseScope = EnterPrivatePoseFrame(frame.WireFrame);
            var previousDispatchFrame = _currentDispatchFrame;
            _currentDispatchFrame = frame.Frame;
            try
            {
                ApplyFramePose(frame);
                MarkMonitorPoseApplied(frame);
            }
            finally
            {
                ExitPrivatePoseFrame(privatePoseScope);
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
