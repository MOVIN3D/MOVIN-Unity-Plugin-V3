using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MOVIN.OSC;

namespace MOVIN.Tests
{
    public class MOVINStreamReceiverEditModeTests
    {
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const string RootAddress = "/MOVIN/Unity/Root";
        private const string BoneAddress = "/MOVIN/Unity/Bone";
        private const string VmcBoneAddress = "/VMC/Ext/Bone/Pos";

        [Test]
        public void StreamAddressesFollowTheTargetSegment()
        {
            Assert.That(MOVINStreamReceiver.RootAddressFor("Unity"), Is.EqualTo(RootAddress));
            Assert.That(MOVINStreamReceiver.BoneAddressFor("Unity"), Is.EqualTo(BoneAddress));
            Assert.That(MOVINStreamReceiver.BoneAddressFor(" /WARUDO/ "), Is.EqualTo("/MOVIN/WARUDO/Bone"));
            Assert.That(MOVINStreamReceiver.RootAddressFor(""), Is.EqualTo(RootAddress));
            Assert.That(MOVINStreamReceiver.RootAddressFor(null), Is.EqualTo(RootAddress));
        }

        [Test]
        public void TryReadFrameIndexReadsIntegerPrefix()
        {
            var msg = new OSCMessage { Args = new object[] { -3, "Root" } };
            var args = new object[] { msg, 0, 0 };

            var result = (bool)InvokeStatic("TryReadFrameIndex", args);

            Assert.That(result, Is.True);
            Assert.That(args[1], Is.EqualTo(-3));
            Assert.That(args[2], Is.EqualTo(1));
        }

        [Test]
        public void TryReadFrameIndexLeavesMessageWithoutFrameAtOffsetZero()
        {
            var msg = new OSCMessage { Args = new object[] { "Root" } };
            var args = new object[] { msg, 0, 0 };

            var result = (bool)InvokeStatic("TryReadFrameIndex", args);

            Assert.That(result, Is.False);
            Assert.That(args[1], Is.EqualTo(0));
            Assert.That(args[2], Is.EqualTo(0));
        }

        [Test]
        public void BoneMessageIsBufferedByFrame()
        {
            var gameObject = new GameObject("MOVINStreamReceiver bone test");
            gameObject.SetActive(false);

            try
            {
                var receiver = gameObject.AddComponent<MOVINStreamReceiver>();

                Assert.That(TryBuffer(receiver, BoneMessage(BoneAddress, 0, "Hips")), Is.True);
                InvokeInstance(receiver, "ForceCompleteCurrentFrame");

                Assert.That(TryTakeFrame(receiver, out var frame), Is.True);
                Assert.That(GetFrameNumber(frame), Is.EqualTo(0));
                Assert.That(GetBoneCount(frame), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void VmcAddressIsNotTreatedAsMotion()
        {
            var gameObject = new GameObject("MOVINStreamReceiver VMC address test");
            gameObject.SetActive(false);

            try
            {
                var receiver = gameObject.AddComponent<MOVINStreamReceiver>();

                Assert.That(TryBuffer(receiver, BoneMessage(VmcBoneAddress, 4, "Hips")), Is.False);
                Assert.That(TryBuffer(receiver, BoneMessage("/VMC/Ext/Root/Pos", 4, "Root")), Is.False);
                InvokeInstance(receiver, "ForceCompleteCurrentFrame");

                Assert.That(TryTakeFrame(receiver, out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void MotionMessageWithoutFrameIndexIsConsumedButIgnored()
        {
            var gameObject = new GameObject("MOVINStreamReceiver frameless test");
            gameObject.SetActive(false);

            try
            {
                var receiver = gameObject.AddComponent<MOVINStreamReceiver>();
                var frameless = new OSCMessage
                {
                    Address = BoneAddress,
                    Args = new object[] { "Hips", 0f, 0f, 0f, 0f, 0f, 0f, 1f },
                };

                Assert.That(TryBuffer(receiver, frameless), Is.True);
                InvokeInstance(receiver, "ForceCompleteCurrentFrame");

                Assert.That(TryTakeFrame(receiver, out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void NonMotionAddressIsLeftForTheMainThread()
        {
            var gameObject = new GameObject("MOVINStreamReceiver non-motion test");
            gameObject.SetActive(false);

            try
            {
                var receiver = gameObject.AddComponent<MOVINStreamReceiver>();
                var blendShape = new OSCMessage
                {
                    Address = "/VMC/Ext/Blend/Val",
                    Args = new object[] { "Joy", 1f },
                };

                Assert.That(TryBuffer(receiver, blendShape), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RootMessageBuffersOptionalScale()
        {
            var gameObject = new GameObject("MOVINStreamReceiver root test");
            gameObject.SetActive(false);

            try
            {
                var receiver = gameObject.AddComponent<MOVINStreamReceiver>();
                var root = new OSCMessage
                {
                    Address = RootAddress,
                    Args = new object[] { 3, "Root", 0f, 1f, 0f, 0f, 0f, 0f, 1f, 2f, 2f, 2f },
                };

                Assert.That(TryBuffer(receiver, root), Is.True);
                InvokeInstance(receiver, "ForceCompleteCurrentFrame");

                Assert.That(TryTakeFrame(receiver, out var frame), Is.True);
                Assert.That(GetFrameNumber(frame), Is.EqualTo(3));
                Assert.That(GetFrameProperty(frame, "HasRoot"), Is.True);
                Assert.That(GetFrameProperty(frame, "RootName"), Is.EqualTo("Root"));
                Assert.That(GetFrameProperty(frame, "RootPosition"), Is.EqualTo(new Vector3(0f, 1f, 0f)));
                Assert.That(GetFrameProperty(frame, "RootScale"), Is.EqualTo(new Vector3(2f, 2f, 2f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void StreamTargetSelectsTheAcceptedAddresses()
        {
            var gameObject = new GameObject("MOVINStreamReceiver target test");
            gameObject.SetActive(false);

            try
            {
                var receiver = gameObject.AddComponent<MOVINStreamReceiver>();
                receiver.streamTarget = "WARUDO";

                Assert.That(TryBuffer(receiver, BoneMessage("/MOVIN/WARUDO/Bone", 0, "Hips")), Is.True);
                Assert.That(TryBuffer(receiver, BoneMessage(BoneAddress, 0, "Hips")), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

#if MOVIN_STREAM_VALIDATION
        [Test]
        public void ValidationBeginAndEndPacketsParseExpectedFields()
        {
            var begin = new OSCMessage { Args = new object[] { "session-1", "Unity", "unused", "C:/tmp/movin" } };
            var beginArgs = new object[] { begin, "", "", "" };

            var beginResult = (bool)InvokeStatic("TryReadValidationBegin", beginArgs);

            Assert.That(beginResult, Is.True);
            Assert.That(beginArgs[1], Is.EqualTo("session-1"));
            Assert.That(beginArgs[2], Is.EqualTo("Unity"));
            Assert.That(beginArgs[3], Is.EqualTo("C:/tmp/movin"));

            var end = new OSCMessage { Args = new object[] { "session-1", "Unity" } };
            var endArgs = new object[] { end, "", "" };

            var endResult = (bool)InvokeStatic("TryReadValidationEnd", endArgs);

            Assert.That(endResult, Is.True);
            Assert.That(endArgs[1], Is.EqualTo("session-1"));
            Assert.That(endArgs[2], Is.EqualTo("Unity"));
        }

        [Test]
        public void ReceiveThreadBeginOpensValidationBeforeFirstRawPacket()
        {
            var directory = CreateTempDirectory();
            var gameObject = new GameObject("MOVINStreamReceiver validation begin test");
            gameObject.SetActive(false);

            try
            {
                var receiver = gameObject.AddComponent<MOVINStreamReceiver>();
                receiver.validationLogDirectory = directory;
                var sessionId = "session-receive-begin";
                var begin = new OSCMessage
                {
                    Address = "/MOVIN/StreamValidation/Begin",
                    Args = new object[] { sessionId, "Unity", 60, directory },
                };

                var handled = (bool)InvokeInstance(receiver, "TryHandlePrivateReceiveThreadControlMessage", begin);

                Assert.That(handled, Is.True);
                var diagnostics = receiver.GetPrivateDiagnosticsSnapshot();
                Assert.That(diagnostics.ValidationLogOpen, Is.True);
                Assert.That(diagnostics.ValidationSessionId, Is.EqualTo(sessionId));

                var packet = new byte[] { 1, 2, 3, 4 };
                var motion = BoneMessage(BoneAddress, -1, "Hips");
                motion.PacketData = packet;
                motion.PacketLength = packet.Length;
                motion.PacketSequence = 1;
                InvokeInstance(receiver, "RecordPrivateRawPacket", motion);
                InvokeInstance(receiver, "OnPrivateReceiverStopping");

                var lines = File.ReadAllLines(Path.Combine(directory, $"{sessionId}_Plugin"));
                Assert.That(lines.Length, Is.GreaterThanOrEqualTo(5));
                Assert.That(lines[0], Is.EqualTo("MOVIN_STREAM_VALIDATION_PACKET_V1"));
                Assert.That(lines[1], Is.EqualTo($"session={sessionId}"));
                Assert.That(lines[2], Is.EqualTo("target=unity"));
                Assert.That(lines[3], Is.EqualTo("packet_format=base64_udp_datagram"));
                Assert.That(lines[4], Is.EqualTo("000000|AQIDBA=="));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void ReceiveThreadControlHandlerLeavesEndForMainThread()
        {
            var gameObject = new GameObject("MOVINStreamReceiver validation end test");
            gameObject.SetActive(false);

            try
            {
                var receiver = gameObject.AddComponent<MOVINStreamReceiver>();
                var end = new OSCMessage
                {
                    Address = "/MOVIN/StreamValidation/End",
                    Args = new object[] { "session-1", "Unity" },
                };

                var handled = (bool)InvokeInstance(receiver, "TryHandlePrivateReceiveThreadControlMessage", end);

                Assert.That(handled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ValidationFallbackOpensFreshAppFileWithMatchingHeader()
        {
            var directory = CreateTempDirectory();
            var gameObject = new GameObject("MOVINStreamReceiver validation fallback test");
            gameObject.SetActive(false);
            MOVINStreamReceiver receiver = null;

            try
            {
                receiver = gameObject.AddComponent<MOVINStreamReceiver>();
                receiver.validationLogDirectory = directory;
                var sessionId = "session-fresh";
                WriteAppValidationFile(directory, sessionId, "Unity");

                var opened = (bool)InvokeInstance(receiver, "TryBeginValidationFromLatestAppFile");

                Assert.That(opened, Is.True);
                var diagnostics = receiver.GetPrivateDiagnosticsSnapshot();
                Assert.That(diagnostics.ValidationLogOpen, Is.True);
                Assert.That(diagnostics.ValidationSessionId, Is.EqualTo(sessionId));
            }
            finally
            {
                if (receiver != null)
                    InvokeInstance(receiver, "OnPrivateReceiverStopping");
                UnityEngine.Object.DestroyImmediate(gameObject);
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void ValidationFallbackRejectsStaleOrWrongTargetAppFile()
        {
            var directory = CreateTempDirectory();
            var gameObject = new GameObject("MOVINStreamReceiver validation fallback reject test");
            gameObject.SetActive(false);

            try
            {
                var receiver = gameObject.AddComponent<MOVINStreamReceiver>();
                receiver.validationLogDirectory = directory;

                var staleSession = "session-stale";
                var stalePath = WriteAppValidationFile(directory, staleSession, "Unity");
                File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow.AddMinutes(-10));

                var wrongTargetSession = "session-osc";
                WriteAppValidationFile(directory, wrongTargetSession, "OSC");

                var opened = (bool)InvokeInstance(receiver, "TryBeginValidationFromLatestAppFile");

                Assert.That(opened, Is.False);
                Assert.That(receiver.GetPrivateDiagnosticsSnapshot().ValidationLogOpen, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void ValidationFloatUsesSixDecimalsAndNormalizesNegativeZero()
        {
            Assert.That(InvokeStatic("ValidationFloat", 1.2345678f), Is.EqualTo("1.234568"));
            Assert.That(InvokeStatic("ValidationFloat", -0.0000004f), Is.EqualTo("0.000000"));
        }
#endif

        [Test]
        public void FrameBufferKeepsPlaybackOrderWhenBacklogIsBelowDropThreshold()
        {
            var gameObject = new GameObject("MOVINStreamReceiver test");
            gameObject.SetActive(false);

            try
            {
                var receiver = gameObject.AddComponent<MOVINStreamReceiver>();
                InvokeInstance(receiver, "BufferBonePose", 0, "Hips", Vector3.zero, Quaternion.identity);
                InvokeInstance(receiver, "BufferBonePose", 2, "Hips", Vector3.one, Quaternion.identity);
                InvokeInstance(receiver, "BufferBonePose", 1, "Hips", Vector3.right, Quaternion.identity);

                Assert.That(TryTakeFrame(receiver, out var frame), Is.True);
                Assert.That(GetFrameNumber(frame), Is.EqualTo(0));

                Assert.That(TryTakeFrame(receiver, out frame), Is.True);
                Assert.That(GetFrameNumber(frame), Is.EqualTo(1));

                InvokeInstance(receiver, "ForceCompleteCurrentFrame");

                Assert.That(TryTakeFrame(receiver, out frame), Is.True);
                Assert.That(GetFrameNumber(frame), Is.EqualTo(2));

                Assert.That(TryTakeFrame(receiver, out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void FrameBufferDropsToLatestCompleteFrameWhenBacklogReachesThreshold()
        {
            var gameObject = new GameObject("MOVINStreamReceiver lag test");
            gameObject.SetActive(false);

            try
            {
                var receiver = gameObject.AddComponent<MOVINStreamReceiver>();
                receiver.maxBufferedFramesBeforeDrop = 3;
                InvokeInstance(receiver, "BufferBonePose", 0, "Hips", Vector3.zero, Quaternion.identity);
                InvokeInstance(receiver, "BufferBonePose", 1, "Hips", Vector3.right, Quaternion.identity);
                InvokeInstance(receiver, "BufferBonePose", 2, "Hips", Vector3.up, Quaternion.identity);
                InvokeInstance(receiver, "BufferBonePose", 3, "Hips", Vector3.one, Quaternion.identity);

                Assert.That(TryTakeFrame(receiver, out var frame), Is.True);
                Assert.That(GetFrameNumber(frame), Is.EqualTo(2));

                InvokeInstance(receiver, "ForceCompleteCurrentFrame");

                Assert.That(TryTakeFrame(receiver, out frame), Is.True);
                Assert.That(GetFrameNumber(frame), Is.EqualTo(3));

                Assert.That(TryTakeFrame(receiver, out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RestartedSenderRecoversAfterTimeoutButLatePacketsStayIgnored()
        {
            var go = new GameObject("Restart test");
            go.SetActive(false);
            try
            {
                var receiver = go.AddComponent<MOVINStreamReceiver>();
                TryBuffer(receiver, BoneMessage(BoneAddress, 100, "Hips"));
                TryBuffer(receiver, BoneMessage(BoneAddress, 101, "Hips"));
                Assert.That(TryTakeFrame(receiver, out var first), Is.True);
                InvokeInstance(receiver, "ApplyBufferedFrame", first);
                TryBuffer(receiver, BoneMessage(BoneAddress, 0, "Hips"));
                Assert.That(((IDictionary)typeof(MOVINStreamReceiver).GetField("_frameBuffer", PrivateInstance).GetValue(receiver)).Contains(0), Is.False);

                typeof(MOVINStreamReceiver).GetField("_lastAcceptedFrameTimestamp", PrivateInstance).SetValue(receiver,
                    System.Diagnostics.Stopwatch.GetTimestamp() - 2 * System.Diagnostics.Stopwatch.Frequency);
                TryBuffer(receiver, BoneMessage(BoneAddress, 1, "Hips"));
                TryBuffer(receiver, BoneMessage(BoneAddress, 2, "Hips"));
                Assert.That(TryTakeFrame(receiver, out var restarted), Is.True);
                Assert.That(GetFrameNumber(restarted), Is.EqualTo(1));
                InvokeInstance(receiver, "ApplyBufferedFrame", restarted);
                Assert.That(receiver.GetMonitorSnapshot().AppliedFramesReceived, Is.EqualTo(2));
                Assert.That(receiver.GetMonitorSnapshot().DroppedFrameCount, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [TestCase(6, 7)]
        [TestCase(int.MaxValue, 121)]
        public void FrameStorageIsBoundedWithoutMainThreadUpdates(int threshold, int limit)
        {
            var go = new GameObject("Bounded frames test");
            go.SetActive(false);
            try
            {
                var receiver = go.AddComponent<MOVINStreamReceiver>();
                receiver.maxBufferedFramesBeforeDrop = threshold;
                for (var i = 0; i < 10000; i++)
                    TryBuffer(receiver, BoneMessage(BoneAddress, i, "Hips"));
                var frames = (IDictionary)typeof(MOVINStreamReceiver).GetField("_frameBuffer", PrivateInstance).GetValue(receiver);
                Assert.That(frames.Count, Is.LessThanOrEqualTo(limit));
                Assert.That(frames.Contains(9999), Is.True);
                Assert.That(TryTakeFrame(receiver, out var latest), Is.True);
                Assert.That(GetFrameNumber(latest), Is.GreaterThanOrEqualTo(9998));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void ReorderedPoseDoesNotDoubleCountInputFrames()
        {
            var go = new GameObject("Input count test");
            go.SetActive(false);
            try
            {
                var receiver = go.AddComponent<MOVINStreamReceiver>();
                TryBuffer(receiver, BoneMessage(BoneAddress, 1, "Hips"));
                TryBuffer(receiver, BoneMessage(BoneAddress, 2, "Hips"));
                TryBuffer(receiver, BoneMessage(BoneAddress, 1, "Foot"));
                Assert.That(receiver.GetMonitorSnapshot().InputFramesReceived, Is.EqualTo(2));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFinitePoseIsRejectedBeforeBuffering(float value)
        {
            var go = new GameObject("Invalid pose test");
            go.SetActive(false);
            try
            {
                var receiver = go.AddComponent<MOVINStreamReceiver>();
                var msg = BoneMessage(BoneAddress, 0, "Hips");
                msg.Args[2] = value;
                Assert.That(Assert.Throws<TargetInvocationException>(() => TryBuffer(receiver, msg)).InnerException, Is.TypeOf<FormatException>());
                Assert.That(receiver.GetMonitorSnapshot().InputFramesReceived, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void WrongNumericTypeAndZeroQuaternionAreRejected()
        {
            var go = new GameObject("Invalid type test");
            go.SetActive(false);
            try
            {
                var receiver = go.AddComponent<MOVINStreamReceiver>();
                var msg = BoneMessage(BoneAddress, 0, "Hips");
                msg.Args[2] = 1;
                Assert.That(Assert.Throws<TargetInvocationException>(() => TryBuffer(receiver, msg)).InnerException, Is.TypeOf<FormatException>());
                msg.Args[2] = 1f;
                msg.Args[8] = 0f;
                Assert.That(Assert.Throws<TargetInvocationException>(() => TryBuffer(receiver, msg)).InnerException, Is.TypeOf<FormatException>());
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

#if MOVIN_STREAM_VALIDATION
        [TestCase("../outside")]
        [TestCase("..\\outside")]
        [TestCase("C:/outside")]
        [TestCase("session:stream")]
        [TestCase("session\nheader")]
        public void ValidationRejectsUnsafeSessionNames(string name)
        {
            var msg = new OSCMessage { Args = new object[] { name, "unity", 60, "C:/tmp" } };
            Assert.That(InvokeStatic("TryReadValidationBegin", new object[] { msg, "", "", "" }), Is.False);
        }

        [Test]
        public void ValidationUsesLocalDirectoryAndPreservesExistingFiles()
        {
            var directory = CreateTempDirectory();
            var remoteDirectory = Path.Combine(directory, "remote");
            var go = new GameObject("Validation boundary test");
            go.SetActive(false);
            var receiver = go.AddComponent<MOVINStreamReceiver>();
            receiver.validationLogDirectory = directory;
            var begin = new OSCMessage { Args = new object[] { "safe-session", "unity", 60, remoteDirectory } };
            try
            {
                InvokeInstance(receiver, "BeginValidationSession", begin);
                Assert.That(receiver.GetPrivateDiagnosticsSnapshot().ValidationLogOpen, Is.True);
                Assert.That(Directory.Exists(remoteDirectory), Is.False);
                InvokeInstance(receiver, "EndValidationSession", new OSCMessage { Args = new object[] { "safe-session", "Unity" } });
                Assert.That(receiver.GetPrivateDiagnosticsSnapshot().ValidationLogOpen, Is.False);
                var path = Path.Combine(directory, "safe-session_Plugin");
                Assert.That(File.ReadAllLines(path)[2], Is.EqualTo("target=unity"));
                File.WriteAllText(path, "preserve");
                InvokeInstance(receiver, "BeginValidationSession", begin);
                Assert.That(receiver.GetPrivateDiagnosticsSnapshot().ValidationLogOpen, Is.False);
                Assert.That(File.ReadAllText(path), Is.EqualTo("preserve"));
            }
            finally
            {
                receiver.StopReceiver();
                UnityEngine.Object.DestroyImmediate(go);
                DeleteTempDirectory(directory);
            }
        }
#endif

        [Test]
        public void MultipleReceiversRestoreBackgroundSettingAfterLastStops()
        {
            var previous = Application.runInBackground;
            var go = new GameObject("Background test");
            go.SetActive(false);
            var first = go.AddComponent<MOVINStreamReceiver>();
            var second = go.AddComponent<MOVINStreamReceiver>();
            try
            {
                Application.runInBackground = false;
                InvokeInstance(first, "ApplyRunInBackgroundOverride");
                InvokeInstance(second, "ApplyRunInBackgroundOverride");
                InvokeInstance(first, "RestoreRunInBackgroundOverride");
                Assert.That(Application.runInBackground, Is.True);
                InvokeInstance(second, "RestoreRunInBackgroundOverride");
                Assert.That(Application.runInBackground, Is.False);
            }
            finally
            {
                first.StopReceiver();
                second.StopReceiver();
                UnityEngine.Object.DestroyImmediate(go);
                Application.runInBackground = previous;
            }
        }

        [Test]
        public void ParserRejectsOversizedBlobAndBundleLengthsWithoutAllocation()
        {
            var blob = new byte[] { 47, 120, 0, 0, 44, 98, 0, 0, 127, 255, 255, 255 };
            Assert.Throws<FormatException>(() => OSCParser.ParsePacket(blob, 0, blob.Length, _ => Assert.Fail("Malformed blob dispatched")));
            var bundle = new byte[] { 35, 98, 117, 110, 100, 108, 101, 0, 0, 0, 0, 0, 0, 0, 0, 1, 127, 255, 255, 255 };
            Assert.Throws<FormatException>(() => OSCParser.ParsePacket(bundle, 0, bundle.Length, _ => Assert.Fail("Malformed bundle dispatched")));
        }

        [Test]
        public void ParserNeverDispatchesPartialUnsupportedMessages()
        {
            var packet = new byte[] { 47, 120, 0, 0, 44, 105, 84, 0, 0, 0, 0, 1 };
            Assert.Throws<FormatException>(() => OSCParser.ParsePacket(packet, 0, packet.Length, _ => Assert.Fail("Partial message dispatched")));
        }

        [Test]
        public void ParserBoundsNestedBundlesAndKeepsValidMessages()
        {
            var packet = new byte[] { 47, 120, 0, 0, 44, 105, 0, 0, 0, 0, 0, 1 };
            OSCMessage parsed = null;
            OSCParser.ParsePacket(packet, 0, packet.Length, msg => parsed = msg);
            Assert.That(parsed.Args[0], Is.EqualTo(1));
            for (var i = 0; i < 18; i++)
            {
                var nested = new byte[packet.Length + 20];
                System.Text.Encoding.ASCII.GetBytes("#bundle").CopyTo(nested, 0);
                nested[15] = 1;
                var length = packet.Length;
                nested[16] = (byte)(length >> 24);
                nested[17] = (byte)(length >> 16);
                nested[18] = (byte)(length >> 8);
                nested[19] = (byte)length;
                packet.CopyTo(nested, 20);
                packet = nested;
            }
            Assert.Throws<FormatException>(() => OSCParser.ParsePacket(packet, 0, packet.Length, _ => Assert.Fail("Excessive nesting dispatched")));
        }

        [Test]
        public void UdpReceiverIgnoresUnknownTrafficAndContinuesAfterMalformedPacket()
        {
            var go = new GameObject("UDP regression test");
            go.SetActive(false);
            var receiver = go.AddComponent<MOVINStreamReceiver>();
            receiver.listenPort = 0;
            receiver.bindAddress = "127.0.0.1";
#if MOVIN_STREAM_VALIDATION
            receiver.validationLogging = false;
#endif
            try
            {
                receiver.StartReceiver();
                var udp = (System.Net.Sockets.UdpClient)typeof(MOVINStreamReceiver).GetField("_udp", PrivateInstance).GetValue(receiver);
                var endpoint = (System.Net.IPEndPoint)udp.Client.LocalEndPoint;
                using var sender = new System.Net.Sockets.UdpClient();
                var unknown = new byte[] { 47, 120, 0, 0, 44, 105, 0, 0, 0, 0, 0, 1 };
                for (var i = 0; i < 100; i++)
                    sender.Send(unknown, unknown.Length, endpoint);
                var malformed = new byte[] { 47, 120, 0, 0, 44, 98, 0, 0, 127, 255, 255, 255 };
                sender.Send(malformed, malformed.Length, endpoint);

                byte[] EncodeBone(int frame)
                {
                    using var bytes = new MemoryStream();
                    using var writer = new BinaryWriter(bytes);
                    void String(string value)
                    {
                        writer.Write(System.Text.Encoding.UTF8.GetBytes(value));
                        writer.Write((byte)0);
                        while (bytes.Length % 4 != 0) writer.Write((byte)0);
                    }
                    void Number(byte[] value)
                    {
                        if (BitConverter.IsLittleEndian) Array.Reverse(value);
                        writer.Write(value);
                    }
                    String(BoneAddress);
                    String(",isfffffff");
                    Number(BitConverter.GetBytes(frame));
                    String("Hips");
                    foreach (var value in new[] { 1f, 2f, 3f, 0f, 0f, 0f, 1f })
                        Number(BitConverter.GetBytes(value));
                    return bytes.ToArray();
                }
                for (var frame = 0; frame < 2; frame++)
                {
                    var packet = EncodeBone(frame);
                    sender.Send(packet, packet.Length, endpoint);
                }
                Assert.That(System.Threading.SpinWait.SpinUntil(() => receiver.GetMonitorSnapshot().InputFramesReceived == 2, 3000), Is.True);
                Assert.That(receiver.GetMonitorSnapshot().QueuedMessages, Is.Zero);
                Assert.That(receiver.GetMonitorSnapshot().ProcessingErrors, Is.EqualTo(1));
                InvokeInstance(receiver, "Update");
                Assert.That(receiver.BonePoses["Hips"].pos, Is.EqualTo(new Vector3(1f, 2f, 3f)));
                receiver.StopReceiver();
                receiver.StartReceiver();
                Assert.That(receiver.GetMonitorSnapshot().IsRunning, Is.True);
            }
            finally
            {
                receiver.StopReceiver();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static OSCMessage BoneMessage(string address, int frame, string boneName)
        {
            return new OSCMessage
            {
                Address = address,
                Args = new object[] { frame, boneName, 0f, 0f, 0f, 0f, 0f, 0f, 1f },
            };
        }

        private static bool TryBuffer(MOVINStreamReceiver receiver, OSCMessage msg)
        {
            return (bool)InvokeInstance(receiver, "TryBufferMotionMessage", msg);
        }

        private static object InvokeStatic(string methodName, params object[] args)
        {
            return typeof(MOVINStreamReceiver)
                .GetMethod(methodName, PrivateStatic)
                .Invoke(null, args);
        }

        private static object InvokeInstance(MOVINStreamReceiver receiver, string methodName, params object[] args)
        {
            return typeof(MOVINStreamReceiver)
                .GetMethod(methodName, PrivateInstance)
                .Invoke(receiver, args);
        }

        private static bool TryTakeFrame(MOVINStreamReceiver receiver, out object frame)
        {
            var args = new object[] { null };
            var result = (bool)InvokeInstance(receiver, "TryTakeFrameForPlayback", args);
            frame = args[0];
            return result;
        }

        private static int GetFrameNumber(object frame)
        {
            return (int)GetFrameProperty(frame, "Frame");
        }

        private static int GetBoneCount(object frame)
        {
            return ((ICollection)GetFrameProperty(frame, "Bones")).Count;
        }

        private static object GetFrameProperty(object frame, string propertyName)
        {
            return frame.GetType().GetProperty(propertyName).GetValue(frame);
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "MOVINStreamReceiverEditModeTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void DeleteTempDirectory(string directory)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        private static string WriteAppValidationFile(string directory, string sessionId, string target)
        {
            var path = Path.Combine(directory, $"{sessionId}_App");
            File.WriteAllLines(path, new[]
            {
                "MOVIN_STREAM_VALIDATION_PACKET_V1",
                $"session={sessionId}",
                $"target={target}",
                "packet_format=base64_udp_datagram",
            });
            return path;
        }
    }
}
