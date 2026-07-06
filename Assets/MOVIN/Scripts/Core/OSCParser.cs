using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#region OSC Low-Level

public class OSCMessage
{
    public string Address;
    public string Types; // e.g. ",sff"
    public object[] Args;
    public byte[] PacketData;
    public int PacketLength;
    public long PacketSequence;
}

public static class OSCParser
{
    public static void ParsePacket(byte[] data, int offset, int length, Action<OSCMessage> onMessage)
    {
        if (data == null || onMessage == null || offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
            return;

        var end = offset + length;

        // Bundle or message?
        if (IsBundle(data, offset, end))
        {
            ParseBundle(data, offset, end, onMessage);
        }
        else
        {
            var msg = ParseMessage(data, offset, end);
            if (msg != null) onMessage(msg);
        }
    }

    private static bool IsBundle(byte[] data, int offset, int end)
    {
        const string bundlePrefix = "#bundle";
        if (end - offset < 8) return false;
        // "#bundle" with trailing null and padding to 8 bytes
        for (var i = 0; i < bundlePrefix.Length; i++)
        {
            if (data[offset + i] != (byte)bundlePrefix[i])
                return false;
        }
        return data[offset + bundlePrefix.Length] == 0;
    }

    private static void ParseBundle(byte[] data, int offset, int end, Action<OSCMessage> onMessage)
    {
        int idx = offset;
        if (!TryReadPaddedString(data, end, ref idx, out var tag))
            return;
        if (tag != "#bundle") return;
        if (idx + 8 > end) return;
        idx += 8; // timetag (NTP 64-bit), skip
        while (idx < end)
        {
            if (!TryReadIntBE(data, end, ref idx, out var elemSize)) break;
            if (elemSize <= 0 || idx + elemSize > end) break;
            ParsePacket(data, idx, elemSize, onMessage);
            idx += elemSize;
        }
    }

    private static OSCMessage ParseMessage(byte[] data, int offset, int end)
    {
        int idx = offset;
        if (!TryReadPaddedString(data, end, ref idx, out var address))
            return null;
        if (string.IsNullOrEmpty(address)) return null;
        if (idx >= end) return null;
        if (!TryReadPaddedString(data, end, ref idx, out var types))
            return null;
        if (string.IsNullOrEmpty(types) || types[0] != ',') return null;

        var args = new List<object>();
        for (int i = 1; i < types.Length; i++)
        {
            char t = types[i];
            switch (t)
            {
                case 'i':
                    if (!TryReadIntBE(data, end, ref idx, out var intValue)) return null;
                    args.Add(intValue);
                    break;
                case 'f':
                    if (!TryReadFloatBE(data, end, ref idx, out var floatValue)) return null;
                    args.Add(floatValue);
                    break;
                case 's':
                    if (!TryReadPaddedString(data, end, ref idx, out var stringValue)) return null;
                    args.Add(stringValue);
                    break;
                case 'b':
                    if (!TryReadBlob(data, end, ref idx, out var blobValue)) return null;
                    args.Add(blobValue);
                    break;
                // extend as needed (e.g., 'h', 'd', 'T', 'F')
                default:
                    // Skip unsupported type safely if possible
                    Debug.LogWarning($"Unsupported OSC arg type '{t}' in {address}");
                    return new OSCMessage { Address = address, Types = types, Args = args.ToArray() };
            }
        }

        return new OSCMessage { Address = address, Types = types, Args = args.ToArray() };
    }

    private static bool TryReadPaddedString(byte[] data, int end, ref int idx, out string value)
    {
        value = null;
        if (idx < 0 || idx >= end)
            return false;

        int start = idx;
        while (idx < end && data[idx] != 0) idx++;
        if (idx >= end)
            return false;

        value = Encoding.UTF8.GetString(data, start, idx - start);
        // skip null and pad to 4-byte boundary
        var afterNull = idx + 1;
        var padded = start + (((afterNull - start) + 3) & ~3);
        if (padded > end)
            return false;

        idx = padded;
        return true;
    }

    private static bool TryReadBlob(byte[] data, int end, ref int idx, out byte[] value)
    {
        value = null;
        if (!TryReadIntBE(data, end, ref idx, out var len))
            return false;
        if (len < 0 || idx + len > end)
            return false;

        value = new byte[len];
        Buffer.BlockCopy(data, idx, value, 0, len);
        idx += len;
        var padded = idx + ((4 - (len % 4)) % 4);
        if (padded > end)
            return false;

        idx = padded;
        return true;
    }

    private static bool TryReadIntBE(byte[] data, int end, ref int idx, out int value)
    {
        value = 0;
        if (idx < 0 || idx + 4 > end)
            return false;

        value = (data[idx] << 24) | (data[idx + 1] << 16) | (data[idx + 2] << 8) | data[idx + 3];
        idx += 4;
        return true;
    }

    private static bool TryReadFloatBE(byte[] data, int end, ref int idx, out float value)
    {
        value = 0f;
        if (idx < 0 || idx + 4 > end)
            return false;

        if (BitConverter.IsLittleEndian)
        {
            // copy and reverse for big-endian network order
            byte[] tmp = new byte[4];
            tmp[0] = data[idx + 3];
            tmp[1] = data[idx + 2];
            tmp[2] = data[idx + 1];
            tmp[3] = data[idx + 0];
            idx += 4;
            value = BitConverter.ToSingle(tmp, 0);
            return true;
        }
        else
        {
            value = BitConverter.ToSingle(data, idx);
            idx += 4;
            return true;
        }
    }
}

public static class OSCArgReader
{
    public static int[] Ints(OSCMessage msg)
    {
        var list = new List<int>(msg.Args.Length);
        foreach (var a in msg.Args)
        {
            if (a is int i) list.Add(i);
            else if (a is float f) list.Add((int)f);
        }
        return list.ToArray();
    }
}

#endregion
