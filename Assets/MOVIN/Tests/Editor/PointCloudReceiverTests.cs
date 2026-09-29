using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using MOVIN.OSC;
using NUnit.Framework;
using UnityEngine;

namespace MOVIN.Tests
{
    public class PointCloudReceiverTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _object;
        private MOVINStreamReceiver _receiver;
        private Vector3[] _points;
        private int _frame;
        private int _events;

        [SetUp] public void Setup()
        {
            _object = new GameObject("Point cloud tests");
            _object.SetActive(false);
            _receiver = _object.AddComponent<MOVINStreamReceiver>();
            _events = 0;
            _points = null;
            _receiver.OnPointCloud += (frame, points) => { _frame = frame; _points = points; _events++; };
        }

        [TearDown] public void Cleanup()
        {
            _receiver.StopReceiver();
            UnityEngine.Object.DestroyImmediate(_object);
        }

        [Test] public void ReorderedAndDuplicateChunksProduceOneCompleteFrame()
        {
            Call("TryBufferPointCloudMessage", Chunk(3, 101, 1));
            Call("TryBufferPointCloudMessage", Chunk(3, 101, 1));
            Call("ApplyPointCloudIfAvailable");
            Assert.That(_events, Is.Zero);
            Call("TryBufferPointCloudMessage", Chunk(3, 101, 0));
            Call("ApplyPointCloudIfAvailable");
            Assert.That(_events, Is.EqualTo(1));
            Assert.That(_frame, Is.EqualTo(3));
            Assert.That(_points.Length, Is.EqualTo(101));
            Assert.That(_points[100], Is.EqualTo(new Vector3(100f, 101f, 102f)));
            Call("TryBufferPointCloudMessage", Chunk(3, 101, 0));
            Call("ApplyPointCloudIfAvailable");
            Assert.That(_events, Is.EqualTo(1));
        }

        [Test] public void NewestCompleteFrameSkipsIncompleteOlderFrame()
        {
            Call("TryBufferPointCloudMessage", Chunk(1, 101, 0));
            Call("TryBufferPointCloudMessage", Chunk(2, 1, 0));
            Call("ApplyPointCloudIfAvailable");
            Assert.That(_frame, Is.EqualTo(2));
            Assert.That(_events, Is.EqualTo(1));
            Call("TryBufferPointCloudMessage", Chunk(1, 101, 1));
            Call("ApplyPointCloudIfAvailable");
            Assert.That(_events, Is.EqualTo(1));
        }

        [TestCase("large")]
        [TestCase("negative")]
        [TestCase("count")]
        [TestCase("float_header")]
        [TestCase("nan")]
        [TestCase("infinity")]
        [TestCase("integer_coordinate")]
        public void MalformedChunksAreRejectedBeforeAllocation(string kind)
        {
            var msg = Chunk(0, 1, 0);
            if (kind == "large") msg.Args[1] = int.MaxValue;
            if (kind == "negative") msg.Args[2] = -1;
            if (kind == "count") msg.Args[4] = 100;
            if (kind == "float_header") msg.Args[0] = 0f;
            if (kind == "nan") msg.Args[5] = float.NaN;
            if (kind == "infinity") msg.Args[5] = float.PositiveInfinity;
            if (kind == "integer_coordinate") msg.Args[5] = 1;
            Assert.That(Assert.Throws<TargetInvocationException>(() => Call("TryBufferPointCloudMessage", msg)).InnerException, Is.TypeOf<FormatException>());
            Assert.That(BufferCount(), Is.Zero);
        }

        [Test] public void InconsistentFrameMetadataIsRejected()
        {
            Call("TryBufferPointCloudMessage", Chunk(1, 101, 0));
            Assert.That(Assert.Throws<TargetInvocationException>(() => Call("TryBufferPointCloudMessage", Chunk(1, 102, 1))).InnerException, Is.TypeOf<FormatException>());
            Call("TryBufferPointCloudMessage", Chunk(1, 101, 1));
            Call("ApplyPointCloudIfAvailable");
            Assert.That(_points.Length, Is.EqualTo(101));
        }

        [Test] public void PausedMainThreadRetainsAtMostThreeFrames()
        {
            for (var i = 0; i < 1000; i++) Call("TryBufferPointCloudMessage", Chunk(i, 101, 0));
            Assert.That(BufferCount(), Is.EqualTo(3));
            Call("ApplyPointCloudIfAvailable");
            Assert.That(_events, Is.Zero);
            _receiver.StopReceiver();
            Assert.That(BufferCount(), Is.Zero);
        }

        [Test] public void CounterRestartRecoversAfterOneSecond()
        {
            Call("TryBufferPointCloudMessage", Chunk(100, 1, 0));
            Call("ApplyPointCloudIfAvailable");
            Call("TryBufferPointCloudMessage", Chunk(0, 1, 0));
            Call("ApplyPointCloudIfAvailable");
            Assert.That(_events, Is.EqualTo(1));
            typeof(MOVINStreamReceiver).GetField("_lastPointCloudTimestamp", Flags).SetValue(_receiver,
                System.Diagnostics.Stopwatch.GetTimestamp() - 2 * System.Diagnostics.Stopwatch.Frequency);
            Call("TryBufferPointCloudMessage", Chunk(1, 1, 0));
            Call("ApplyPointCloudIfAvailable");
            Assert.That(_events, Is.EqualTo(2));
            Assert.That(_frame, Is.EqualTo(1));
        }

        [Test] public void EmptyPointCloudCanClearTheConsumersDisplay()
        {
            Call("TryBufferPointCloudMessage", Chunk(0, 0, 0));
            Call("ApplyPointCloudIfAvailable");
            Assert.That(_events, Is.EqualTo(1));
            Assert.That(_points, Is.Empty);
        }

        [Test] public void DuplicateRigNamesFailBeforeBuildingAnAmbiguousMap()
        {
            var root = new GameObject("Root").transform;
            root.SetParent(_object.transform);
            new GameObject("Bone").transform.SetParent(root);
            new GameObject("Bone").transform.SetParent(root);
            var receiver = _object.AddComponent<MocapReceiver>();
            var error = Assert.Throws<TargetInvocationException>(() => typeof(MocapReceiver)
                .GetMethod("BuildFrom", Flags).Invoke(receiver, new object[] { root }));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(error.InnerException.Message, Does.Contain("Duplicate bone/Transform name 'Bone'"));
        }

        private object Call(string name, params object[] args) => typeof(MOVINStreamReceiver).GetMethod(name, Flags).Invoke(_receiver, args);
        private int BufferCount() => ((ICollection)typeof(MOVINStreamReceiver).GetField("_pointCloudFrames", Flags).GetValue(_receiver)).Count;
        private static OSCMessage Chunk(int frame, int total, int chunk)
        {
            var count = Math.Min(100, total - chunk * 100);
            return new OSCMessage
            {
                Address = "/MOVIN/PointCloud",
                Args = new object[] { frame, total, chunk, Math.Max(1, (total + 99) / 100), count }
                    .Concat(Enumerable.Range(0, count).SelectMany(i => new object[] { (float)(chunk * 100 + i), (float)(chunk * 100 + i + 1), (float)(chunk * 100 + i + 2) })).ToArray(),
            };
        }
    }
}
