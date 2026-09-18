using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using MOVIN.OSC;

namespace MOVIN
{
    /// <summary>
    /// UDP receiver for the MOVIN Studio motion stream (OSC 1.0 encoded, no external packages).
    /// - Listens on UDP (default 11235) and parses OSC messages and bundles.
    /// - Consumes /MOVIN/&lt;target&gt;/Root and /MOVIN/&lt;target&gt;/Bone, where the target segment
    ///   defaults to "Unity", plus the legacy /VMC/Ext/Root/Pos and /VMC/Ext/Bone/Pos addresses
    ///   that older MOVIN Studio versions send. Every motion message starts with an int frame
    ///   index; standard VMC messages carry none and are ignored, so this is not a VMC receiver.
    /// - Thread-safe: the network thread buffers motion frames by frame index and the Unity main
    ///   thread applies one completed frame per Update().
    /// </summary>
    public partial class MotionStreamReceiver : MonoBehaviour
    {
        private const int TargetFrameRate = 120;

        [Header("Network")]
        [Tooltip("UDP port to listen on.")]
        public int listenPort = 11235;

        [Tooltip("Optional: bind to a specific local IP (blank for Any).")]
        public string bindAddress = "";

        [Tooltip("Target segment of the stream addresses, /MOVIN/<target>/Root and /MOVIN/<target>/Bone. Must match the target selected in MOVIN Studio. Applied when the receiver starts.")]
        public string streamTarget = DefaultStreamTarget;

        [Tooltip("Log incoming OSC addresses for debugging.")]
        public bool verboseLogging = false;

        [Tooltip("Keep Unity running when the Editor or Player loses focus so streamed poses continue to be processed.")]
        public bool forceRunInBackground = true;

        [Header("Playback")]
        [Tooltip("Drop to the latest completed motion frame when this many completed frames are waiting. 6 frames is about 0.1 seconds at a 60 FPS sender.")]
        [Min(1)]
        public int maxBufferedFramesBeforeDrop = 6;

        private Thread _thread;
        private UdpClient _udp;
        private IPEndPoint _remoteAny;
        private volatile bool _running;
        private string _rootAddress;
        private string _boneAddress;
        private readonly object _frameLock = new object();
        private readonly Dictionary<int, FramePose> _frameBuffer = new Dictionary<int, FramePose>();
        private readonly List<int> _staleFrameScratch = new List<int>();
        private int _currentBufferedFrame = int.MinValue;
        private int _latestCompleteFrame = int.MinValue;
        private int _lastAppliedBufferedFrame = int.MinValue;
        private long _currentBufferedFrameTicks;
        private long _packetSequence;
        private static readonly object FrameRatePolicyLock = new object();
        private static int _frameRatePolicyRefCount;
        private static int _sharedPreviousTargetFrameRate;
        private static int _sharedPreviousVSyncCount;
        private bool _frameRatePolicyOverridden;
        private bool _previousRunInBackground;
        private bool _runInBackgroundOverridden;
        private long _mainThreadFrames;
        private long _packetsReceived;
        private long _messagesReceived;
        private long _messagesDispatched;
        private long _processingErrors;
        private long _inputFramesReceived;
        private long _appliedFramesReceived;
        private long _droppedFrameCount;
        private long _lastPacketUtcTicks;
        private long _lastDispatchUtcTicks;
        private readonly object _monitorLock = new object();
        private string _lastOscAddress = "";
        private string _lastPoseName = "";
        private int _lastWireFrame = int.MinValue;
        private int _lastFrame = int.MinValue;
        private int _lastInputFrameForMonitor = int.MinValue;
        private int _lastAppliedFrameForMonitor = int.MinValue;
        private int _lastDroppedFrameStart = int.MinValue;
        private int _lastDroppedFrameEnd = int.MinValue;
        private double _lastPlaybackLatencyMs = -1.0;
        private int _currentDispatchFrame = int.MinValue;

        // Messages the receive thread did not consume, processed on the main thread.
        private readonly ConcurrentQueue<OSCMessage> _queue = new ConcurrentQueue<OSCMessage>();

        // --- Events you can subscribe to. Raised on the main thread when a buffered frame is applied. ---
        public event Action<string, Vector3, Quaternion, Vector3?> OnRootPose; // name, local pos, local rot, (opt) local scale
        public event Action<string, Vector3, Quaternion> OnBonePose; // bone name as streamed by MOVIN Studio, local pos, local rot

        // Last-known bone poses keyed by streamed bone name.
        public readonly Dictionary<string, (Vector3 pos, Quaternion rot)> BonePoses = new();

        private readonly struct PrivatePoseScope
        {
            public PrivatePoseScope(int previousWireFrame)
            {
                PreviousWireFrame = previousWireFrame;
            }

            public int PreviousWireFrame { get; }
        }
    }
}
