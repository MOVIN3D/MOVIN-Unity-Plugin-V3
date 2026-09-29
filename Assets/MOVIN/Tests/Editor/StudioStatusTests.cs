using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using MOVIN.OSC;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MOVIN.Tests
{
    public class StudioStatusTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _object;
        private MOVINStreamReceiver _receiver;
        private UdpClient _sender, _replies;
        private IPEndPoint _destination;

        [SetUp] public void Setup()
        {
            _object = new GameObject("수신기 <test>");
            _object.SetActive(false);
            _receiver = _object.AddComponent<MOVINStreamReceiver>();
            _receiver.bindAddress = "127.0.0.1";
            _receiver.listenPort = 0;
#if MOVIN_STREAM_VALIDATION
            _receiver.validationLogging = false;
#endif
            _receiver.StartReceiver();
            _destination = (IPEndPoint)((UdpClient)Get("_udp")).Client.LocalEndPoint;
            _sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _replies = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _replies.Client.ReceiveTimeout = 2000;
        }

        [TearDown] public void Cleanup()
        {
            _receiver.StopReceiver();
            _sender.Dispose();
            _replies.Dispose();
            UnityEngine.Object.DestroyImmediate(_object);
        }

        [Test] public void ReplyRequiresMainThreadAndReportsIdleWithoutInventingFrames()
        {
            var token = Request();
            Assert.That(_replies.Available, Is.Zero);
            Call("Update");
            var message = Receive();
            Assert.That(message.Address, Is.EqualTo("/MOVIN/Unity/Status"));
            Assert.That(message.Types, Is.EqualTo(",sisiiffiiiffisiisis"));
            Assert.That(message.Args[0], Is.EqualTo(token));
            Assert.That(message.Args[1], Is.EqualTo(2));
            Assert.That(message.Args[2], Is.EqualTo(_object.name));
            Assert.That(message.Args[3], Is.EqualTo(0));
            Assert.That(message.Args[4], Is.EqualTo(-1));
            Assert.That(message.Args[6], Is.EqualTo(-1f));
            Assert.That(message.Args[11], Is.EqualTo(-1f));
            Assert.That(message.Args[16], Is.EqualTo(""));
            Assert.That(message.Args[17], Is.EqualTo(0));
            Assert.That(message.Args[18], Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
        }

        [Test] public void StatusCountsOnlyProcessedMotionAndCompleteClouds()
        {
            Send("/MOVIN/Unity/Bone", ",isfffffff", 10, "Hips", 1f, 2f, 3f, 0f, 0f, 0f, 1f);
            Send("/MOVIN/PointCloud", ",iiiiifff", 1, 101, 1, 2, 1, 1f, 2f, 3f);
            WaitMessages(2);
            Call("ForceCompleteCurrentFrame");
            Request();
            Call("Update");
            var first = Receive();
            Assert.That(first.Args[4], Is.EqualTo(10));
            Assert.That((float)first.Args[6], Is.GreaterThanOrEqualTo(0));
            Assert.That(first.Args[11], Is.EqualTo(-1f), "An incomplete cloud must not be reported as received.");
            Assert.That(first.Args[14], Is.EqualTo(1));

            Send("/MOVIN/PointCloud", ",iiiiifff", 2, 1, 0, 1, 1, 1f, 2f, 3f);
            WaitMessages(4);
            Set("_statusSentAt", 0L);
            Request();
            Call("Update");
            var second = Receive();
            Assert.That(second.Args[9], Is.EqualTo(1));
            Assert.That((float)second.Args[11], Is.GreaterThanOrEqualTo(0));
            Assert.That(second.Args[15], Is.EqualTo(1));
            Set("_statusSampleAt", Stopwatch.GetTimestamp() - Stopwatch.Frequency * 2);
            Set("_statusSentAt", 0L);
            Request();
            Call("Update");
            var rates = Receive();
            Assert.That((float)rates.Args[5], Is.InRange(.1f, 1f));
            Assert.That((float)rates.Args[10], Is.InRange(.1f, 1f));
        }

        [Test] public void AnotherSenderIsNotReportedAsTheRequestingStudio()
        {
            using var other = new UdpClient();
            var bytes = Packet("/MOVIN/Unity/Bone", ",isfffffff", 0, "Hips", 0f, 0f, 0f, 0f, 0f, 0f, 1f);
            other.Send(bytes, bytes.Length, _destination);
            WaitMessages(1);
            Call("ForceCompleteCurrentFrame");
            Request();
            Call("Update");
            Assert.That(Receive().Args[14], Is.EqualTo(0));
        }

        [Test] public void CharacterStatusCountsActualTransformMatchesInTheLastAppliedFrame()
        {
            _receiver.StopReceiver();
            UnityEngine.Object.DestroyImmediate(_receiver);
            var root = new GameObject("Root").transform;
            root.SetParent(_object.transform);
            var hips = new GameObject("Hips").transform;
            hips.SetParent(root);
            var helper = new GameObject("HipsBoneObject").transform;
            helper.SetParent(hips);
            new GameObject("HipsBone").transform.SetParent(helper);
            new GameObject("HipsJoint", typeof(MeshRenderer)).transform.SetParent(hips);
            _receiver = _object.AddComponent<MocapReceiver>();
            ((MocapReceiver)_receiver).characterName = "Avatar";
            typeof(MocapReceiver).GetMethod("BuildFrom", Flags).Invoke(_receiver, new object[] { _object.transform });
            _receiver.bindAddress = "127.0.0.1";
            _receiver.listenPort = 0;
#if MOVIN_STREAM_VALIDATION
            _receiver.validationLogging = false;
#endif
            _receiver.StartReceiver();
            _destination = (IPEndPoint)((UdpClient)Get("_udp")).Client.LocalEndPoint;
            Send("/MOVIN/Unity/Root", ",isffffffffff", 10, "Root", 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f);
            Send("/MOVIN/Unity/Bone", ",isfffffff", 10, "Hips", 1f, 2f, 3f, 0f, 0f, 0f, 1f);
            Send("/MOVIN/Unity/Bone", ",isfffffff", 10, "Absent", 0f, 0f, 0f, 0f, 0f, 0f, 1f);
            WaitMessages(3);
            Call("ForceCompleteCurrentFrame");
            Request();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Absent.*"));
            Call("Update");
            var message = Receive();
            Assert.That(message.Args[3], Is.EqualTo(1));
            Assert.That(message.Args[7], Is.EqualTo(2));
            Assert.That(message.Args[8], Is.EqualTo(1));
            Assert.That(message.Args[16], Is.EqualTo("Avatar"));
            Assert.That(message.Args[17], Is.EqualTo(2), "Drawing helpers must not inflate the target skeleton count.");
            Assert.That(message.Args[18], Has.Length.EqualTo(64));
            Assert.That(hips.localPosition, Is.EqualTo(new Vector3(1, 2, 3)));
        }

        [Test] public void CharacterIdentityUsesConfiguredNameOrGameObjectWithoutCloneSuffix()
        {
            _receiver.StopReceiver();
            UnityEngine.Object.DestroyImmediate(_receiver);
            _object.name = "Avatar(Clone)";
            var receiver = _object.AddComponent<MocapReceiver>();
            _receiver = receiver;
            var identity = typeof(MocapReceiver).GetMethod("GetStatusCharacter", Flags);
            Assert.That(((ValueTuple<string, string[]>)identity.Invoke(receiver, null)).Item1, Is.EqualTo("Avatar"));
            receiver.characterName = "Studio Model";
            Assert.That(((ValueTuple<string, string[]>)identity.Invoke(receiver, null)).Item1, Is.EqualTo("Studio Model"));
        }

        [Test] public void ExplicitSkeletonRootIsNotReplacedByAStreamedChild()
        {
            _receiver.StopReceiver();
            UnityEngine.Object.DestroyImmediate(_receiver);
            var root = new GameObject("Armature").transform;
            root.SetParent(_object.transform);
            new GameObject("Hips").transform.SetParent(root);
            var receiver = _object.AddComponent<MocapReceiver>();
            _receiver = receiver;
            typeof(MocapReceiver).GetField("rootBoneName", Flags).SetValue(receiver, "Armature");
            typeof(MocapReceiver).GetMethod("Build", Flags).Invoke(receiver, new object[] { "Armature" });
            typeof(MocapReceiver).GetMethod("TryAdoptStreamedSkeletonRoot", Flags).Invoke(receiver, new object[] { "Hips" });
            var identity = (ValueTuple<string, string[]>)typeof(MocapReceiver).GetMethod("GetStatusCharacter", Flags).Invoke(receiver, null);
            Assert.That(identity.Item2, Is.EquivalentTo(new[] { "Armature", "Hips" }));
        }

        [Test] public void PendingRequestsAreBoundedAndExpiredAfterEditorPause()
        {
            Request();
            var newest = Request();
            Call("Update");
            Assert.That(Receive().Args[0], Is.EqualTo(newest));
            Request();
            Set("_statusRequestedAt", Stopwatch.GetTimestamp() - Stopwatch.Frequency * 3);
            Set("_statusSentAt", 0L);
            Call("Update");
            Assert.That(_replies.Available, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(65536)]
        public void InvalidReplyPortIsReportedAndNextProbeRecovers(int port)
        {
            LogAssert.Expect(LogType.Warning, "Motion buffer error for /MOVIN/Unity/Status/Request: Invalid Studio status request.");
            Send("/MOVIN/Unity/Status/Request", ",si", Guid.NewGuid().ToString("N"), port);
            Assert.That(SpinWait.SpinUntil(() => _receiver.GetMonitorSnapshot().ProcessingErrors == 1, 2000), Is.True);
            var token = Request();
            Call("Update");
            var message = Receive();
            Assert.That(message.Args[0], Is.EqualTo(token));
            Assert.That(message.Args[12], Is.EqualTo(1));
        }

        [Test] public void RestartClearsPreviousStreamAndReplyState()
        {
            Request();
            Set("_statusMotionFrame", 99);
            _receiver.StopReceiver();
            _receiver.StartReceiver();
            _destination = (IPEndPoint)((UdpClient)Get("_udp")).Client.LocalEndPoint;
            Call("Update");
            Assert.That(_replies.Available, Is.Zero);
            Request();
            Call("Update");
            Assert.That(Receive().Args[4], Is.EqualTo(-1));
        }

        private string Request()
        {
            var token = Guid.NewGuid().ToString("N");
            Send("/MOVIN/Unity/Status/Request", ",si", token, ((IPEndPoint)_replies.Client.LocalEndPoint).Port);
            Assert.That(SpinWait.SpinUntil(() => Equals(Get("_statusRequest"), token), 2000), Is.True);
            return token;
        }

        private OSCMessage Receive()
        {
            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            var bytes = _replies.Receive(ref endpoint);
            Assert.That(endpoint.Port, Is.EqualTo(_destination.Port));
            OSCMessage message = null;
            OSCParser.ParsePacket(bytes, 0, bytes.Length, m => message = m);
            return message;
        }

        private void WaitMessages(long count) => Assert.That(SpinWait.SpinUntil(() => _receiver.GetMonitorSnapshot().MessagesDispatched >= count, 2000), Is.True);
        private object Get(string name) => typeof(MOVINStreamReceiver).GetField(name, Flags).GetValue(_receiver);
        private void Set(string name, object value) => typeof(MOVINStreamReceiver).GetField(name, Flags).SetValue(_receiver, value);
        private void Call(string name) => typeof(MOVINStreamReceiver).GetMethod(name, Flags).Invoke(_receiver, null);
        private void Send(string address, string types, params object[] args)
        {
            var bytes = Packet(address, types, args);
            _sender.Send(bytes, bytes.Length, _destination);
        }

        private static byte[] Packet(string address, string types, params object[] args)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            WriteString(address);
            WriteString(types);
            foreach (var value in args)
            {
                if (value is string text) { WriteString(text); }
                else { writer.Write(IPAddress.HostToNetworkOrder(value is int number ? number : BitConverter.SingleToInt32Bits((float)value))); }
            }
            return stream.ToArray();
            void WriteString(string value)
            {
                writer.Write(Encoding.UTF8.GetBytes(value));
                writer.Write((byte)0);
                while (stream.Length % 4 != 0) { writer.Write((byte)0); }
            }
        }
    }
}
