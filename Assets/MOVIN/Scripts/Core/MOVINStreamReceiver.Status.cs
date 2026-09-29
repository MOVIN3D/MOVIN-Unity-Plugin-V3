using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using MOVIN.OSC;
using Stopwatch = System.Diagnostics.Stopwatch;
using Debug = UnityEngine.Debug;

namespace MOVIN
{
    public partial class MOVINStreamReceiver
    {
        private const string StatusRequestAddress = "/MOVIN/Unity/Status/Request";
        private const string StatusAddress = "/MOVIN/Unity/Status";
        private readonly object _statusLock = new object();
        private string _statusRequest;
        private IPEndPoint _statusReply, _statusSource, _motionSource, _cloudSource;
        private long _statusRequestedAt, _statusSentAt, _statusSampleAt;
        private long _statusMotionAt, _statusCloudAt;
        private int _statusMotionFrame = -1, _statusPointCount;
        private int _statusMotionCount, _statusCloudCount;
        private float _statusMotionFps, _statusCloudFps;
        protected int StatusMatchedBones, StatusMissingBones;
        protected virtual bool AppliesToCharacter => false;
        protected virtual (string Name, string[] Bones) GetStatusCharacter() => ("", Array.Empty<string>());

        private void ResetStudioStatus()
        {
            lock (_statusLock)
            {
                _statusRequest = null;
                _statusReply = _statusSource = _motionSource = _cloudSource = null;
            }
            _statusRequestedAt = _statusSentAt = _statusMotionAt = _statusCloudAt = 0;
            _statusSampleAt = Stopwatch.GetTimestamp();
            _statusMotionFrame = -1;
            _statusPointCount = _statusMotionCount = _statusCloudCount = 0;
            _statusMotionFps = _statusCloudFps = 0;
            StatusMatchedBones = StatusMissingBones = 0;
        }

        private bool TryHandleStatusRequest(OSCMessage msg)
        {
            var handled = msg.Address == StatusRequestAddress;
            if (handled)
            {
                if (msg.Args.Length != 2 || msg.Args[0] is not string token
                    || token.Length != 32 || !Guid.TryParseExact(token, "N", out _)
                    || msg.Args[1] is not int port || port < 1 || port > 65535)
                {
                    throw new FormatException("Invalid Studio status request.");
                }
                // Only the latest request is retained, including while the Editor is paused.
                lock (_statusLock)
                {
                    _statusRequest = token;
                    _statusSource = _remoteAny;
                    _statusReply = new IPEndPoint(_remoteAny.Address, port);
                    _statusRequestedAt = Stopwatch.GetTimestamp();
                }
            }
            return handled;
        }

        private void ReplyToStudio()
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - _statusSampleAt) / (double)Stopwatch.Frequency;
            if (elapsed >= 1)
            {
                _statusMotionFps = (float)(_statusMotionCount / elapsed);
                _statusCloudFps = (float)(_statusCloudCount / elapsed);
                _statusMotionCount = _statusCloudCount = 0;
                _statusSampleAt = now;
            }

            string token = null;
            IPEndPoint reply = null;
            var motionSourceMatches = false;
            var cloudSourceMatches = false;
            lock (_statusLock)
            {
                if (_running && _statusRequest != null
                    && (now - _statusSentAt) / (double)Stopwatch.Frequency >= 0.5)
                {
                    if ((now - _statusRequestedAt) / (double)Stopwatch.Frequency < 2)
                    {
                        token = _statusRequest;
                        reply = _statusReply;
                        motionSourceMatches = Equals(_statusSource, _motionSource);
                        cloudSourceMatches = Equals(_statusSource, _cloudSource);
                    }
                    _statusRequest = null;
                }
            }
            if (token != null)
            {
                // This runs after pose application on the main thread, so a paused Editor stops replying.
                using var packet = new MemoryStream();
                using var writer = new BinaryWriter(packet, Encoding.UTF8, true);
                WriteString(StatusAddress);
                WriteString(",sisiiffiiiffisiisis");
                WriteString(token);
                WriteInt(2);
                WriteString(name.Length > 128 ? name.Substring(0, 128) : name);
                WriteInt(AppliesToCharacter ? 1 : 0);
                WriteInt(_statusMotionFrame);
                WriteFloat(_statusMotionFps);
                WriteFloat(Age(_statusMotionAt));
                WriteInt(StatusMatchedBones);
                WriteInt(StatusMissingBones);
                WriteInt(_statusPointCount);
                WriteFloat(_statusCloudFps);
                WriteFloat(Age(_statusCloudAt));
                WriteInt((int)Math.Min(int.MaxValue, Interlocked.Read(ref _processingErrors)));
                WriteString(_rootAddress);
                WriteInt(motionSourceMatches ? 1 : 0);
                WriteInt(cloudSourceMatches ? 1 : 0);
                var character = GetStatusCharacter();
                WriteString(character.Name.Length > 256 ? character.Name.Substring(0, 256) : character.Name);
                WriteInt(character.Bones.Length);
                // Sorted, length-prefixed UTF-8 names; shared with Studio's bone_signature.
                using var names = new MemoryStream();
                using var namesWriter = new BinaryWriter(names, Encoding.UTF8, true);
                foreach (var bone in character.Bones.OrderBy(n => n, StringComparer.Ordinal))
                {
                    namesWriter.Write(bone);
                }
                using var hash = SHA256.Create();
                WriteString(BitConverter.ToString(hash.ComputeHash(names.ToArray())).Replace("-", "").ToLowerInvariant());
                var bytes = packet.ToArray();
                _statusSentAt = now;
                try
                {
                    _udp.Send(bytes, bytes.Length, reply);
                }
                catch (SocketException ex)
                {
                    Interlocked.Increment(ref _processingErrors);
                    Debug.LogWarning($"MOVIN Studio status reply failed: {ex.Message}");
                }

                float Age(long timestamp) => timestamp == 0 ? -1f : (float)((now - timestamp) / (double)Stopwatch.Frequency);
                void WriteInt(int value) => writer.Write(IPAddress.HostToNetworkOrder(value));
                void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));
                void WriteString(string value)
                {
                    writer.Write(Encoding.UTF8.GetBytes(value));
                    writer.Write((byte)0);
                    while (packet.Length % 4 != 0)
                    {
                        writer.Write((byte)0);
                    }
                }
            }
        }
    }
}
