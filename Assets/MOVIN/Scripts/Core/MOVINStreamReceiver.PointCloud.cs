using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MOVIN.OSC;
using UnityEngine;

namespace MOVIN
{
    public partial class MOVINStreamReceiver
    {
        private const int PointsPerChunk = 100;
        private const int MaxPointCloudPoints = 1000000;
        private readonly object _pointCloudLock = new object();
        private readonly SortedDictionary<int, PointCloudFrame> _pointCloudFrames = new();
        private int _lastPointCloudFrame = -1;
        private long _lastPointCloudTimestamp;

        // Complete frames only, in Studio's Unity-space coordinates. Raised on the main thread.
        public event Action<int, Vector3[]> OnPointCloud;

        private class PointCloudFrame
        {
            public Vector3[] Points;
            public bool[] Chunks;
            public int Received;
        }

        private void ClearPointCloudBuffer()
        {
            lock (_pointCloudLock)
            {
                _pointCloudFrames.Clear();
                _lastPointCloudFrame = -1;
                _lastPointCloudTimestamp = 0;
            }
        }

        private bool TryBufferPointCloudMessage(OSCMessage msg)
        {
            if (msg.Address != "/MOVIN/PointCloud")
                return false;

            var args = msg.Args;
            if (args.Length < 5 || args[0] is not int frame || args[1] is not int total
                || args[2] is not int chunk || args[3] is not int chunks || args[4] is not int count
                || frame < 0 || total < 0 || total > MaxPointCloudPoints
                || chunks != Math.Max(1, (total + PointsPerChunk - 1) / PointsPerChunk)
                || chunk < 0 || chunk >= chunks
                || count != Math.Min(PointsPerChunk, total - chunk * PointsPerChunk)
                || args.Length != 5 + count * 3)
            {
                throw new FormatException("Invalid point cloud chunk header.");
            }
            for (var i = 5; i < args.Length; i++)
            {
                if (args[i] is not float value || float.IsNaN(value) || float.IsInfinity(value))
                    throw new FormatException("Point cloud coordinates must be finite OSC floats.");
            }

            lock (_pointCloudLock)
            {
                var now = Stopwatch.GetTimestamp();
                if (_lastPointCloudTimestamp != 0
                    && (now - _lastPointCloudTimestamp) / (double)Stopwatch.Frequency >= 1.0)
                {
                    ClearPointCloudBuffer();
                }
                if (frame > _lastPointCloudFrame)
                {
                    _lastPointCloudTimestamp = now;
                    if (!_pointCloudFrames.TryGetValue(frame, out var cloud))
                    {
                        // Bound storage before allocating another frame, including during Editor pause.
                        if (_pointCloudFrames.Count == 3)
                        {
                            var oldest = _pointCloudFrames.First().Key;
                            _lastPointCloudFrame = Math.Max(_lastPointCloudFrame, Math.Min(frame, oldest));
                            if (frame > oldest)
                                _pointCloudFrames.Remove(oldest);
                        }
                        if (frame > _lastPointCloudFrame)
                        {
                            cloud = new PointCloudFrame
                            {
                                Points = new Vector3[total],
                                Chunks = new bool[chunks],
                            };
                            _pointCloudFrames.Add(frame, cloud);
                        }
                    }
                    if (cloud != null)
                    {
                        if (cloud.Points.Length != total || cloud.Chunks.Length != chunks)
                            throw new FormatException("Point cloud chunks disagree about the frame size.");
                        if (!cloud.Chunks[chunk])
                        {
                            for (var i = 0; i < count; i++)
                            {
                                var offset = 5 + i * 3;
                                cloud.Points[chunk * PointsPerChunk + i] = new Vector3(
                                    (float)args[offset], (float)args[offset + 1], (float)args[offset + 2]);
                            }
                            cloud.Chunks[chunk] = true;
                            cloud.Received++;
                        }
                    }
                }
            }
            return true;
        }

        private void ApplyPointCloudIfAvailable()
        {
            var frame = -1;
            Vector3[] points = null;
            lock (_pointCloudLock)
            {
                foreach (var pair in _pointCloudFrames)
                {
                    if (pair.Value.Received == pair.Value.Chunks.Length)
                    {
                        frame = pair.Key;
                        points = pair.Value.Points;
                    }
                }
                if (points != null)
                {
                    _lastPointCloudFrame = frame;
                    foreach (var old in _pointCloudFrames.Keys.TakeWhile(i => i <= frame).ToArray())
                        _pointCloudFrames.Remove(old);
                }
            }
            if (points != null)
            {
                OnPointCloud?.Invoke(frame, points);
                _statusPointCount = points.Length;
                _statusCloudAt = Stopwatch.GetTimestamp();
                _statusCloudCount++;
            }
        }
    }
}
