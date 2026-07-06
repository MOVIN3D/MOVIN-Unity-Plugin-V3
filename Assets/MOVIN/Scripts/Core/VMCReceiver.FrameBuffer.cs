using System;
using System.Collections.Generic;
using UnityEngine;

public partial class VMCReceiver
{
    // A partially filled frame is treated as complete once this long passes with no newer
    // frame arriving, so playback is not stalled waiting on a dropped trailing packet.
    private const double FrameStalenessSeconds = 0.05;

    protected class VMCFramePose
    {
        private readonly List<VMCBonePose> _bones = new List<VMCBonePose>();
        private readonly Dictionary<string, int> _boneIndices = new Dictionary<string, int>();

        public VMCFramePose(int wireFrame, int frame)
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
        public Vector3? RootOffset { get; private set; }
        public List<VMCBonePose> Bones => _bones;

        public void SetRoot(string name, Vector3 position, Quaternion rotation, Vector3? scale, Vector3? offset)
        {
            RootName = name;
            RootPosition = position;
            RootRotation = rotation;
            RootScale = scale;
            RootOffset = offset;
            HasRoot = true;
            LastReceiveUtcTicks = DateTime.UtcNow.Ticks;
        }

        public void SetBone(string name, Vector3 position, Quaternion rotation)
        {
            var bone = new VMCBonePose(name, position, rotation);
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

    protected struct VMCBonePose
    {
        public VMCBonePose(string name, Vector3 position, Quaternion rotation)
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

    private bool TryBufferMotionMessage(OSCMessage msg)
    {
        if (msg.Address == "/VMC/Ext/Root/Pos")
            return TryBufferRootPose(msg);

        if (msg.Address == "/VMC/Ext/Bone/Pos")
            return TryBufferBonePose(msg);

        return false;
    }

    private bool TryBufferRootPose(OSCMessage msg)
    {
        var hasFrame = TryReadFrameIndex(msg, out var wireFrame, out var offset);
        if (msg.Args.Length < offset + 8 || msg.Args[offset] is not string rootName)
            return true;

        var position = new Vector3((float)msg.Args[offset + 1], (float)msg.Args[offset + 2], (float)msg.Args[offset + 3]);
        var rotation = new Quaternion((float)msg.Args[offset + 4], (float)msg.Args[offset + 5], (float)msg.Args[offset + 6], (float)msg.Args[offset + 7]);
        Vector3? scale = null, rootOffset = null;
        if (msg.Args.Length >= offset + 11)
            scale = new Vector3((float)msg.Args[offset + 8], (float)msg.Args[offset + 9], (float)msg.Args[offset + 10]);
        if (msg.Args.Length >= offset + 14)
            rootOffset = new Vector3((float)msg.Args[offset + 11], (float)msg.Args[offset + 12], (float)msg.Args[offset + 13]);

        if (!passthroughUnityCoordinates)
        {
            position = ConvertCoords(position);
            rotation = ConvertRot(rotation);
        }

        if (!hasFrame)
            return false;

        MarkPoseMessage(rootName, true, wireFrame);
        BufferRootPose(wireFrame, rootName, position, rotation, scale, rootOffset);

        return true;
    }

    private bool TryBufferBonePose(OSCMessage msg)
    {
        var hasFrame = TryReadFrameIndex(msg, out var wireFrame, out var offset);
        if (msg.Args.Length < offset + 8 || msg.Args[offset] is not string boneName)
            return true;

        var position = new Vector3((float)msg.Args[offset + 1], (float)msg.Args[offset + 2], (float)msg.Args[offset + 3]);
        var rotation = new Quaternion((float)msg.Args[offset + 4], (float)msg.Args[offset + 5], (float)msg.Args[offset + 6], (float)msg.Args[offset + 7]);

        if (!passthroughUnityCoordinates)
        {
            position = ConvertCoords(position);
            rotation = ConvertRot(rotation);
        }

        if (!hasFrame)
            return false;

        MarkPoseMessage(boneName, true, wireFrame);
        BufferBonePose(wireFrame, boneName, position, rotation);

        return true;
    }

    private void BufferRootPose(int wireFrame, string rootName, Vector3 position, Quaternion rotation, Vector3? scale, Vector3? rootOffset)
    {
        lock (_frameLock)
        {
            var frame = GetFrameForBufferLocked(wireFrame);
            frame?.SetRoot(rootName, position, rotation, scale, rootOffset);
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

    private VMCFramePose GetFrameForBufferLocked(int wireFrame)
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
            pose = new VMCFramePose(wireFrame, frame);
            _frameBuffer[frame] = pose;
        }

        return pose;
    }

    private void ApplyBufferedFrameIfAvailable()
    {
        if (TryTakeFrameForPlayback(out var frame))
            ApplyBufferedFrame(frame);
    }

    private bool TryTakeFrameForPlayback(out VMCFramePose frame)
    {
        return TryTakeBufferedFrame(out frame, true);
    }

    private bool TryTakeNextFrameForPlayback(out VMCFramePose frame)
    {
        return TryTakeBufferedFrame(out frame, false);
    }

    private bool TryTakeBufferedFrame(out VMCFramePose frame, bool dropWhenPlaybackIsLagging)
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

    private void ApplyBufferedFrame(VMCFramePose frame)
    {
        var privatePoseScope = EnterPrivatePoseFrame(true, frame.WireFrame);
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

    protected virtual void ApplyFramePose(VMCFramePose frame)
    {
        if (frame.HasRoot)
            OnRootPose?.Invoke(frame.RootName, frame.RootPosition, frame.RootRotation, frame.RootScale, frame.RootOffset);

        foreach (var bone in frame.Bones)
        {
            BonePoses[bone.Name] = (bone.Position, bone.Rotation);
            OnBonePose?.Invoke(bone.Name, bone.Position, bone.Rotation);
        }
    }
}
