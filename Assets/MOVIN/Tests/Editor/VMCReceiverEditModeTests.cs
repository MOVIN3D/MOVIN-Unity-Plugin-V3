using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class VMCReceiverEditModeTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

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
    public void TryReadFrameIndexLeavesLegacyMessageAtOffsetZero()
    {
        var msg = new OSCMessage { Args = new object[] { "Root" } };
        var args = new object[] { msg, 0, 0 };

        var result = (bool)InvokeStatic("TryReadFrameIndex", args);

        Assert.That(result, Is.False);
        Assert.That(args[1], Is.EqualTo(0));
        Assert.That(args[2], Is.EqualTo(0));
    }

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
        var gameObject = new GameObject("VMCReceiver validation begin test");
        gameObject.SetActive(false);

        try
        {
            var receiver = gameObject.AddComponent<VMCReceiver>();
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
            var motion = new OSCMessage
            {
                Address = "/VMC/Ext/Bone/Pos",
                Args = new object[] { -1, "Hips", 0f, 0f, 0f, 0f, 0f, 0f, 1f },
                PacketData = packet,
                PacketLength = packet.Length,
                PacketSequence = 1,
            };
            InvokeInstance(receiver, "RecordPrivateRawPacket", motion);
            InvokeInstance(receiver, "OnPrivateReceiverStopping");

            var lines = File.ReadAllLines(Path.Combine(directory, $"{sessionId}_Plugin"));
            Assert.That(lines.Length, Is.GreaterThanOrEqualTo(5));
            Assert.That(lines[0], Is.EqualTo("MOVIN_STREAM_VALIDATION_PACKET_V1"));
            Assert.That(lines[1], Is.EqualTo($"session={sessionId}"));
            Assert.That(lines[2], Is.EqualTo("target=Unity"));
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
        var gameObject = new GameObject("VMCReceiver validation end test");
        gameObject.SetActive(false);

        try
        {
            var receiver = gameObject.AddComponent<VMCReceiver>();
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
        var gameObject = new GameObject("VMCReceiver validation fallback test");
        gameObject.SetActive(false);
        VMCReceiver receiver = null;

        try
        {
            receiver = gameObject.AddComponent<VMCReceiver>();
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
        var gameObject = new GameObject("VMCReceiver validation fallback reject test");
        gameObject.SetActive(false);

        try
        {
            var receiver = gameObject.AddComponent<VMCReceiver>();
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

    [Test]
    public void FrameBufferKeepsPlaybackOrderWhenBacklogIsBelowDropThreshold()
    {
        var gameObject = new GameObject("VMCReceiver test");
        gameObject.SetActive(false);

        try
        {
            var receiver = gameObject.AddComponent<VMCReceiver>();
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
        var gameObject = new GameObject("VMCReceiver lag test");
        gameObject.SetActive(false);

        try
        {
            var receiver = gameObject.AddComponent<VMCReceiver>();
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

    private static object InvokeStatic(string methodName, params object[] args)
    {
        return typeof(VMCReceiver)
            .GetMethod(methodName, PrivateStatic)
            .Invoke(null, args);
    }

    private static object InvokeInstance(VMCReceiver receiver, string methodName, params object[] args)
    {
        return typeof(VMCReceiver)
            .GetMethod(methodName, PrivateInstance)
            .Invoke(receiver, args);
    }

    private static bool TryTakeFrame(VMCReceiver receiver, out object frame)
    {
        var args = new object[] { null };
        var result = (bool)InvokeInstance(receiver, "TryTakeFrameForPlayback", args);
        frame = args[0];
        return result;
    }

    private static int GetFrameNumber(object frame)
    {
        return (int)frame.GetType().GetProperty("Frame").GetValue(frame);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VMCReceiverEditModeTests", Guid.NewGuid().ToString("N"));
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
