using System;
using UnityEngine;

public partial class VMCReceiver
{
    private void DispatchVMC(OSCMessage msg)
    {
        if (verboseLogging)
        {
            Debug.Log($"OSC {msg.Address} {msg.Types} [{string.Join(", ", msg.Args)}]");
        }

        if (TryHandlePrivateControlMessage(msg))
            return;

        switch (msg.Address)
        {
            case "/VMC/Ext/OK":
                // V2.0: (int loaded)
                // V2.5: (int loaded) (int calibState) (int calibMode)
                // V2.7: (int loaded) (int calibState) (int calibMode) (int trackingStatus)
                var ok = OSCArgReader.Ints(msg);
                int loaded = ok.Length > 0 ? ok[0] : 0;
                int calibState = ok.Length > 1 ? ok[1] : -1;
                int calibMode = ok.Length > 2 ? ok[2] : -1;
                int tracking = ok.Length > 3 ? ok[3] : -1;
                OnOk?.Invoke(loaded, calibState, calibMode, tracking);
                break;

            case "/VMC/Ext/T":
                if (msg.Args.Length >= 1 && msg.Args[0] is float t) OnTime?.Invoke(t);
                break;

            case "/VMC/Ext/Root/Pos":
                // MOVIN extended VMC: frameIdx, name, pos(xyz), rot(xyzw), scale(xyz)
                // Legacy VMC: name, pos(xyz), rot(xyzw), optional scale(xyz), offset(xyz)
                var hasRootFrame = TryReadFrameIndex(msg, out var rootFrameIdx, out var rootOffset);
                if (msg.Args.Length >= rootOffset + 8 && msg.Args[rootOffset] is string rootName)
                {
                    var p = new Vector3((float)msg.Args[rootOffset + 1], (float)msg.Args[rootOffset + 2], (float)msg.Args[rootOffset + 3]);
                    var q = new Quaternion((float)msg.Args[rootOffset + 4], (float)msg.Args[rootOffset + 5], (float)msg.Args[rootOffset + 6], (float)msg.Args[rootOffset + 7]);
                    Vector3? s = null, o = null;
                    if (msg.Args.Length >= rootOffset + 11)
                        s = new Vector3((float)msg.Args[rootOffset + 8], (float)msg.Args[rootOffset + 9], (float)msg.Args[rootOffset + 10]);
                    if (msg.Args.Length >= rootOffset + 14)
                        o = new Vector3((float)msg.Args[rootOffset + 11], (float)msg.Args[rootOffset + 12], (float)msg.Args[rootOffset + 13]);

                    MarkPoseMessage(rootName, hasRootFrame, rootFrameIdx);

                    var privatePoseScope = EnterPrivatePoseFrame(hasRootFrame, rootFrameIdx);
                    var previousDispatchFrame = _currentDispatchFrame;
                    _currentDispatchFrame = hasRootFrame
                        ? WireFrameToFrame(rootFrameIdx)
                        : int.MinValue;
                    try
                    {
                        if (passthroughUnityCoordinates)
                        {
                            OnRootPose?.Invoke(rootName, p, q, s, o);
                        }
                        else
                        {
                            OnRootPose?.Invoke(rootName, ConvertCoords(p), ConvertRot(q), s, o);
                        }
                    }
                    finally
                    {
                        ExitPrivatePoseFrame(privatePoseScope);
                        _currentDispatchFrame = previousDispatchFrame;
                    }
                }
                break;

            case "/VMC/Ext/Bone/Pos":
                var hasBoneFrame = TryReadFrameIndex(msg, out var boneFrameIdx, out var boneOffset);
                if (msg.Args.Length >= boneOffset + 8 && msg.Args[boneOffset] is string boneName)
                {
                    var p = new Vector3((float)msg.Args[boneOffset + 1], (float)msg.Args[boneOffset + 2], (float)msg.Args[boneOffset + 3]);
                    var q = new Quaternion((float)msg.Args[boneOffset + 4], (float)msg.Args[boneOffset + 5], (float)msg.Args[boneOffset + 6], (float)msg.Args[boneOffset + 7]);

                    MarkPoseMessage(boneName, hasBoneFrame, boneFrameIdx);

                    if (!passthroughUnityCoordinates)
                    {
                        p = ConvertCoords(p);
                        q = ConvertRot(q);
                    }
                    BonePoses[boneName] = (p, q);

                    var privatePoseScope = EnterPrivatePoseFrame(hasBoneFrame, boneFrameIdx);
                    var previousDispatchFrame = _currentDispatchFrame;
                    _currentDispatchFrame = hasBoneFrame
                        ? WireFrameToFrame(boneFrameIdx)
                        : int.MinValue;
                    try
                    {
                        OnBonePose?.Invoke(boneName, p, q);
                    }
                    finally
                    {
                        ExitPrivatePoseFrame(privatePoseScope);
                        _currentDispatchFrame = previousDispatchFrame;
                    }
                }
                break;

            case "/VMC/Ext/Blend/Val":
                if (msg.Args.Length >= 2 && msg.Args[0] is string bsName && msg.Args[1] is float val)
                {
                    BlendshapeValues[bsName] = val;
                    OnBlendShapeValue?.Invoke(bsName, val);
                }
                break;

            case "/VMC/Ext/Blend/Apply":
                OnBlendShapeApply?.Invoke();
                break;

            case "/VMC/Ext/Cam":
                if (msg.Args.Length >= 9 && msg.Args[0] is string camName)
                {
                    var p = new Vector3((float)msg.Args[1], (float)msg.Args[2], (float)msg.Args[3]);
                    var q = new Quaternion((float)msg.Args[4], (float)msg.Args[5], (float)msg.Args[6], (float)msg.Args[7]);
                    float fov = (float)msg.Args[8];
                    if (!passthroughUnityCoordinates) { p = ConvertCoords(p); q = ConvertRot(q); }
                    OnCamera?.Invoke(camName, p, q, fov);
                }
                break;

            case "/VMC/Ext/Hmd/Pos":
            case "/VMC/Ext/Hmd/Pos/Local":
                DispatchDevice(msg, OnHmdPos);
                break;
            case "/VMC/Ext/Con/Pos":
            case "/VMC/Ext/Con/Pos/Local":
                DispatchDevice(msg, OnControllerPos);
                break;
            case "/VMC/Ext/Tra/Pos":
            case "/VMC/Ext/Tra/Pos/Local":
                DispatchDevice(msg, OnTrackerPos);
                break;

            default:
                // ignore other addresses per spec
                break;
        }
    }

    private static bool TryReadFrameIndex(OSCMessage msg, out int frameIdx, out int argOffset)
    {
        if (msg.Args.Length > 0)
        {
            if (msg.Args[0] is int i)
            {
                frameIdx = i;
                argOffset = 1;
                return true;
            }

            if (msg.Args[0] is float f)
            {
                frameIdx = Mathf.RoundToInt(f);
                if (Mathf.Approximately(f, frameIdx))
                {
                    argOffset = 1;
                    return true;
                }
            }
        }

        frameIdx = 0;
        argOffset = 0;
        return false;
    }

    /// <summary>
    /// Converts a wire frame index to a logical frame number. MOVIN encodes validation
    /// packets as negative indices (-1 => frame 0, -2 => frame 1, ...); non-negative
    /// indices pass through unchanged.
    /// </summary>
    private static int WireFrameToFrame(int wireFrame)
    {
        return wireFrame < 0 ? -wireFrame - 1 : wireFrame;
    }

    private void DispatchDevice(OSCMessage msg, Action<string, Vector3, Quaternion> cb)
    {
        if (msg.Args.Length >= 8 && msg.Args[0] is string serial)
        {
            var p = new Vector3((float)msg.Args[1], (float)msg.Args[2], (float)msg.Args[3]);
            var q = new Quaternion((float)msg.Args[4], (float)msg.Args[5], (float)msg.Args[6], (float)msg.Args[7]);
            if (!passthroughUnityCoordinates) { p = ConvertCoords(p); q = ConvertRot(q); }
            cb?.Invoke(serial, p, q);
        }
    }

    private static Vector3 ConvertCoords(Vector3 v)
    {
        // Placeholder: if you need to convert between coordinate systems, do it here.
        return v;
    }

    private static Quaternion ConvertRot(Quaternion q)
    {
        // Placeholder for rotation conversion if needed.
        return q;
    }
}
