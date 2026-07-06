using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public partial class VMCReceiver
{
    private const string ValidationBeginAddress = "/MOVIN/StreamValidation/Begin";
    private const string ValidationEndAddress = "/MOVIN/StreamValidation/End";
    private const string ValidationPacketHeader = "MOVIN_STREAM_VALIDATION_PACKET_V1";
    private const string ValidationPoseHeader = "MOVIN_STREAM_VALIDATION_POSE_V1";
    private const string ValidationPacketFormat = "base64_udp_datagram";
    private const string ValidationFloatFormat = "round6";
    private const string ValidationTargetUnity = "Unity";
    private const string ValidationAppSuffix = "_App";
    private const int ValidationFlushLineInterval = 512;
    private const long ValidationFlushIntervalTicks = 5000000L;
    private const double ValidationFallbackMaxAppFileAgeSeconds = 300.0;

    private enum ValidationAppHeaderStatus
    {
        Valid,
        Incomplete,
        Invalid,
    }

    [Header("Validation Logging")]
    [Tooltip("Write App-driven stream validation files for negative frameIndex packets.")]
    public bool validationLogging = true;

    [Tooltip("Optional output directory. Empty uses Documents/MOVIN Studio/StreamValidation/Unity.")]
    public string validationLogDirectory = "";

    [Tooltip("Current App-driven validation session id.")]
    public string validationSessionId = "";

    private readonly object _validationLock = new object();
    private StreamWriter _validationWriter;
    private StreamWriter _validationAppliedWriter;
    private long _lastValidationPacketWritten = -1;
    private int _validationPacketIndex;
    private int _currentValidationWireFrame = int.MinValue;
    private string _validationTarget = ValidationTargetUnity;
    private int _validationLinesSinceFlush;
    private long _lastValidationFlushUtcTicks;
    private readonly HashSet<string> _endedValidationSessions = new HashSet<string>();
    private string _validationLogPath = "";

    public struct PrivateDiagnosticsSnapshot
    {
        public bool ValidationLoggingEnabled;
        public bool ValidationLogOpen;
        public string ValidationSessionId;
        public string ValidationLogPath;
    }

    public PrivateDiagnosticsSnapshot GetPrivateDiagnosticsSnapshot()
    {
        lock (_validationLock)
        {
            return new PrivateDiagnosticsSnapshot
            {
                ValidationLoggingEnabled = validationLogging,
                ValidationLogOpen = _validationWriter != null,
                ValidationSessionId = validationSessionId,
                ValidationLogPath = _validationLogPath,
            };
        }
    }

    private bool TryHandlePrivateControlMessage(OSCMessage msg)
    {
        switch (msg.Address)
        {
            case ValidationBeginAddress:
                BeginValidationSession(msg);
                return true;

            case ValidationEndAddress:
                EndValidationSession(msg);
                return true;

            default:
                return false;
        }
    }

    private bool TryHandlePrivateReceiveThreadControlMessage(OSCMessage msg)
    {
        if (msg.Address != ValidationBeginAddress)
            return false;

        BeginValidationSession(msg);
        return true;
    }

    private void RecordPrivateRawPacket(OSCMessage msg)
    {
        RecordValidationRawPacket(msg);
    }

    private void OnPrivateReceiverStopping()
    {
        CloseValidationLog();
    }

    private PrivatePoseScope EnterPrivatePoseFrame(bool hasFrame, int wireFrame)
    {
        var scope = new PrivatePoseScope(_currentValidationWireFrame);
        _currentValidationWireFrame = hasFrame ? wireFrame : int.MinValue;
        return scope;
    }

    private void ExitPrivatePoseFrame(PrivatePoseScope scope)
    {
        _currentValidationWireFrame = scope.PreviousWireFrame;
    }

    private void RecordValidationRawPacket(OSCMessage msg)
    {
        if (!validationLogging
            || msg.PacketData == null
            || msg.PacketLength <= 0
            || !IsValidationMotionMessage(msg))
        {
            return;
        }

        lock (_validationLock)
        {
            if (msg.PacketSequence == _lastValidationPacketWritten)
                return;

            if (_validationWriter == null && !TryBeginValidationFromLatestAppFile())
                return;

            _validationWriter.Write(_validationPacketIndex.ToString("D6", CultureInfo.InvariantCulture));
            _validationWriter.Write("|");
            _validationWriter.WriteLine(Convert.ToBase64String(msg.PacketData, 0, msg.PacketLength));
            _validationPacketIndex++;
            _lastValidationPacketWritten = msg.PacketSequence;
            NoteValidationLineWrittenLocked();
        }
    }

    private static bool IsValidationMotionMessage(OSCMessage msg)
    {
        var isPose =
            msg.Address == "/VMC/Ext/Root/Pos"
            || msg.Address == "/VMC/Ext/Bone/Pos";
        return isPose
            && TryReadFrameIndex(msg, out var frameIdx, out _)
            && frameIdx < 0;
    }

    private void BeginValidationSession(OSCMessage msg)
    {
        if (!validationLogging)
            return;

        if (!TryReadValidationBegin(msg, out var sessionId, out var target, out var directory))
        {
            Debug.LogWarning("Invalid stream validation begin packet.");
            return;
        }

        if (target != ValidationTargetUnity)
        {
            Debug.LogWarning($"Unsupported stream validation target for Unity plugin: {target}");
            return;
        }

        try
        {
            OpenValidationSession(sessionId, target, directory, false);
        }
        catch (Exception ex)
        {
            CloseValidationLog();
            Debug.LogWarning($"Failed to open VMC validation log: {ex.Message}");
        }
    }

    private bool TryBeginValidationFromLatestAppFile()
    {
        try
        {
            var directory = GetValidationLogDirectory();
            if (!Directory.Exists(directory))
                return false;

            var nowUtc = DateTime.UtcNow;
            FileInfo latestAppFile = null;
            var latestSessionId = "";
            foreach (var path in Directory.EnumerateFiles(directory, $"*{ValidationAppSuffix}"))
            {
                var appFile = new FileInfo(path);
                if (!TryGetValidationFallbackSession(appFile, nowUtc, out var sessionId))
                    continue;

                if (latestAppFile == null || appFile.LastWriteTimeUtc > latestAppFile.LastWriteTimeUtc)
                {
                    latestAppFile = appFile;
                    latestSessionId = sessionId;
                }
            }

            if (latestAppFile == null)
                return false;

            return OpenValidationSession(latestSessionId, ValidationTargetUnity, directory, true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to open VMC validation fallback log: {ex.Message}");
            return false;
        }
    }

    private static bool TryGetValidationFallbackSession(FileInfo appFile, DateTime nowUtc, out string sessionId)
    {
        sessionId = "";
        if (appFile == null || !appFile.Name.EndsWith(ValidationAppSuffix, StringComparison.Ordinal))
            return false;

        var ageSeconds = (nowUtc - appFile.LastWriteTimeUtc).TotalSeconds;
        if (ageSeconds > ValidationFallbackMaxAppFileAgeSeconds)
            return false;

        sessionId = appFile.Name.Substring(0, appFile.Name.Length - ValidationAppSuffix.Length);
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        var headerStatus = ReadValidationAppHeaderStatus(appFile.FullName, sessionId);
        return headerStatus != ValidationAppHeaderStatus.Invalid;
    }

    private static ValidationAppHeaderStatus ReadValidationAppHeaderStatus(string path, string sessionId)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var header = reader.ReadLine();
            if (header == null)
                return ValidationAppHeaderStatus.Incomplete;
            if (header != ValidationPacketHeader)
                return ValidationAppHeaderStatus.Invalid;

            var session = reader.ReadLine();
            if (session == null)
                return ValidationAppHeaderStatus.Incomplete;
            if (session != $"session={sessionId}")
                return ValidationAppHeaderStatus.Invalid;

            var target = reader.ReadLine();
            if (target == null)
                return ValidationAppHeaderStatus.Incomplete;
            if (target != $"target={ValidationTargetUnity}")
                return ValidationAppHeaderStatus.Invalid;

            var packetFormat = reader.ReadLine();
            if (packetFormat == null)
                return ValidationAppHeaderStatus.Incomplete;
            if (packetFormat != $"packet_format={ValidationPacketFormat}")
                return ValidationAppHeaderStatus.Invalid;

            return ValidationAppHeaderStatus.Valid;
        }
        catch (IOException)
        {
            return ValidationAppHeaderStatus.Incomplete;
        }
        catch (UnauthorizedAccessException)
        {
            return ValidationAppHeaderStatus.Incomplete;
        }
    }

    private bool OpenValidationSession(string sessionId, string target, string directory, bool fallback)
    {
        lock (_validationLock)
        {
            var sessionKey = ValidationSessionKey(sessionId, target);
            if (fallback && _endedValidationSessions.Contains(sessionKey))
                return false;

            if (!fallback)
                _endedValidationSessions.Remove(sessionKey);

            if (_validationWriter != null
                && validationSessionId == sessionId
                && _validationTarget == target)
            {
                return true;
            }

            CloseValidationLogLocked();
            if (string.IsNullOrWhiteSpace(directory))
                directory = GetValidationLogDirectory();

            Directory.CreateDirectory(directory);
            validationSessionId = sessionId;
            validationLogDirectory = directory;
            _validationTarget = target;
            _validationPacketIndex = 0;
            _lastValidationPacketWritten = -1;
            _currentValidationWireFrame = int.MinValue;
            _validationLinesSinceFlush = 0;
            _lastValidationFlushUtcTicks = DateTime.UtcNow.Ticks;

            var rawPath = Path.Combine(directory, $"{sessionId}_Plugin");
            var appliedPath = Path.Combine(directory, $"{sessionId}_PluginApplied");
            _validationWriter = OpenSharedWriter(rawPath);
            _validationAppliedWriter = OpenSharedWriter(appliedPath);
            WriteValidationPacketHeader(_validationWriter, sessionId, target);
            WriteValidationPoseHeader(_validationAppliedWriter, sessionId, target);
            _validationLogPath = rawPath;

            var source = fallback ? "fallback" : "control";
            Debug.Log($"VMC validation log ({source}): {rawPath}");
            return true;
        }
    }

    private void EndValidationSession(OSCMessage msg)
    {
        if (!TryReadValidationEnd(msg, out var sessionId, out var target))
        {
            Debug.LogWarning("Invalid stream validation end packet.");
            return;
        }

        if (IsValidationSessionOpen(sessionId, target))
            DrainBufferedFramesForValidationEnd();

        lock (_validationLock)
        {
            _endedValidationSessions.Add(ValidationSessionKey(sessionId, target));

            if (_validationWriter != null
                && validationSessionId == sessionId
                && _validationTarget == target)
            {
                CloseValidationLogLocked();
                Debug.Log($"VMC validation log closed: {sessionId}");
            }
        }
    }

    private bool IsValidationSessionOpen(string sessionId, string target)
    {
        lock (_validationLock)
        {
            return _validationWriter != null
                && validationSessionId == sessionId
                && _validationTarget == target;
        }
    }

    private void DrainBufferedFramesForValidationEnd()
    {
        ForceCompleteCurrentFrame();

        var safety = 10000;
        while (safety-- > 0 && TryTakeNextFrameForPlayback(out var frame))
            ApplyBufferedFrame(frame);
    }

    private void ForceCompleteCurrentFrame()
    {
        lock (_frameLock)
        {
            if (_currentBufferedFrame != int.MinValue)
                _latestCompleteFrame = Math.Max(_latestCompleteFrame, _currentBufferedFrame);
        }
    }

    protected void CapturePrivateAppliedPose(string boneName, Transform boneTransform, bool includeScale)
    {
        var scale = includeScale ? boneTransform.localScale : Vector3.one;
        var position = boneTransform.localPosition;
        var rotation = boneTransform.localRotation;

        lock (_validationLock)
        {
            if (!validationLogging
                || _validationAppliedWriter == null
                || _currentValidationWireFrame >= 0)
            {
                return;
            }

            WriteValidationPoseLine(
                _currentValidationWireFrame,
                boneName,
                position,
                rotation,
                scale
            );
            NoteValidationLineWrittenLocked();
        }
    }

    [Obsolete("Use CapturePrivateAppliedPose.")]
    protected void CaptureValidationAppliedPose(string boneName, Transform boneTransform, bool includeScale)
    {
        CapturePrivateAppliedPose(boneName, boneTransform, includeScale);
    }

    private static bool TryReadValidationBegin(OSCMessage msg, out string sessionId, out string target, out string directory)
    {
        sessionId = "";
        target = "";
        directory = "";

        if (msg.Args.Length < 4)
            return false;

        var sessionArg = msg.Args[0] as string;
        var targetArg = msg.Args[1] as string;
        var directoryArg = msg.Args[3] as string;
        if (string.IsNullOrWhiteSpace(sessionArg)
            || string.IsNullOrWhiteSpace(targetArg)
            || directoryArg == null)
        {
            return false;
        }

        sessionId = sessionArg;
        target = targetArg;
        directory = directoryArg;
        return true;
    }

    private static bool TryReadValidationEnd(OSCMessage msg, out string sessionId, out string target)
    {
        sessionId = "";
        target = "";

        if (msg.Args.Length < 2)
            return false;

        var sessionArg = msg.Args[0] as string;
        var targetArg = msg.Args[1] as string;
        if (string.IsNullOrWhiteSpace(sessionArg) || string.IsNullOrWhiteSpace(targetArg))
            return false;

        sessionId = sessionArg;
        target = targetArg;
        return true;
    }

    private void CloseValidationLog()
    {
        lock (_validationLock)
            CloseValidationLogLocked();
    }

    private void CloseValidationLogLocked()
    {
        FlushValidationWritersLocked();

        try { _validationWriter?.Dispose(); }
        catch { /* ignore */ }

        try { _validationAppliedWriter?.Dispose(); }
        catch { /* ignore */ }

        _validationWriter = null;
        _validationAppliedWriter = null;
        _currentValidationWireFrame = int.MinValue;
        _validationLinesSinceFlush = 0;
        _validationLogPath = "";
    }

    private void NoteValidationLineWrittenLocked()
    {
        _validationLinesSinceFlush++;
        var nowTicks = DateTime.UtcNow.Ticks;
        if (_validationLinesSinceFlush >= ValidationFlushLineInterval
            || nowTicks - _lastValidationFlushUtcTicks >= ValidationFlushIntervalTicks)
        {
            FlushValidationWritersLocked();
        }
    }

    private void FlushValidationWritersLocked()
    {
        try { _validationWriter?.Flush(); }
        catch { /* ignore */ }

        try { _validationAppliedWriter?.Flush(); }
        catch { /* ignore */ }

        _validationLinesSinceFlush = 0;
        _lastValidationFlushUtcTicks = DateTime.UtcNow.Ticks;
    }

    private static string ValidationSessionKey(string sessionId, string target)
    {
        return $"{target}|{sessionId}";
    }

    private string GetValidationLogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(validationLogDirectory))
            return validationLogDirectory;

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
            documents = Directory.GetCurrentDirectory();

        return Path.Combine(documents, "MOVIN Studio", "StreamValidation", "Unity");
    }

    private static StreamWriter OpenSharedWriter(string path)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.NewLine = "\n";
        return writer;
    }

    private static void WriteValidationPacketHeader(StreamWriter writer, string sessionId, string target)
    {
        writer.WriteLine(ValidationPacketHeader);
        writer.WriteLine($"session={sessionId}");
        writer.WriteLine($"target={target}");
        writer.WriteLine($"packet_format={ValidationPacketFormat}");
        writer.Flush();
    }

    private static void WriteValidationPoseHeader(StreamWriter writer, string sessionId, string target)
    {
        writer.WriteLine(ValidationPoseHeader);
        writer.WriteLine($"session={sessionId}");
        writer.WriteLine($"target={target}");
        writer.WriteLine($"float={ValidationFloatFormat}");
        writer.Flush();
    }

    private void WriteValidationPoseLine(
        int frameIdx,
        string boneName,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale
    )
    {
        _validationAppliedWriter.Write(frameIdx.ToString(CultureInfo.InvariantCulture));
        _validationAppliedWriter.Write("|");
        _validationAppliedWriter.Write(boneName);
        _validationAppliedWriter.Write("|");
        _validationAppliedWriter.Write(ValidationFloat(position.x));
        _validationAppliedWriter.Write("|");
        _validationAppliedWriter.Write(ValidationFloat(position.y));
        _validationAppliedWriter.Write("|");
        _validationAppliedWriter.Write(ValidationFloat(position.z));
        _validationAppliedWriter.Write("|");
        _validationAppliedWriter.Write(ValidationFloat(rotation.x));
        _validationAppliedWriter.Write("|");
        _validationAppliedWriter.Write(ValidationFloat(rotation.y));
        _validationAppliedWriter.Write("|");
        _validationAppliedWriter.Write(ValidationFloat(rotation.z));
        _validationAppliedWriter.Write("|");
        _validationAppliedWriter.Write(ValidationFloat(rotation.w));
        _validationAppliedWriter.Write("|");
        _validationAppliedWriter.Write(ValidationFloat(scale.x));
        _validationAppliedWriter.Write("|");
        _validationAppliedWriter.Write(ValidationFloat(scale.y));
        _validationAppliedWriter.Write("|");
        _validationAppliedWriter.WriteLine(ValidationFloat(scale.z));
    }

    private static string ValidationFloat(float value)
    {
        var rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        // Collapse negative zero so the formatted value never reads "-0.000000".
        if (rounded == 0.0)
            rounded = 0.0;

        return rounded.ToString("0.000000", CultureInfo.InvariantCulture);
    }
}
