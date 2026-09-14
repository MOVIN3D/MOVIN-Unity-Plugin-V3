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
    /// - The stream borrows VMC address names such as /VMC/Ext/Root/Pos and /VMC/Ext/Bone/Pos, but
    ///   MOVIN prepends an int frame index, streams the model's own bone names, and applies bone
    ///   positions as well as rotations, so it is not interoperable with standard VMC senders or
    ///   receivers. Messages without a frame index are still parsed and dispatched immediately.
    /// - Thread-safe: the network thread buffers motion frames and enqueues other messages; the
    ///   Unity main thread applies them in Update().
    ///
    /// Address naming reference: https://protocol.vmc.info/english.html
    /// </summary>
    public partial class MotionStreamReceiver : MonoBehaviour
    {
        private const int TargetFrameRate = 120;

        [Header("Network")]
        [Tooltip("UDP port to listen on.")]
        public int listenPort = 11235;

        [Tooltip("Optional: bind to a specific local IP (blank for Any).")]
        public string bindAddress = "";

        [Tooltip("Log incoming OSC addresses for debugging.")]
        public bool verboseLogging = false;

        [Tooltip("Keep Unity running when the Editor or Player loses focus so streamed poses continue to be processed.")]
        public bool forceRunInBackground = true;

        [Header("Playback")]
        [Tooltip("Drop to the latest completed motion frame when this many completed frames are waiting. 6 frames is about 0.1 seconds at a 60 FPS sender.")]
        [Min(1)]
        public int maxBufferedFramesBeforeDrop = 6;

        [Header("Coordinate Conversion")]
        [Tooltip("If your avatar/world uses a right-handed coordinate system, you may need to adapt here. The MOVIN stream and Unity are both left-handed (Y up), so typically no change.")]
        public bool passthroughUnityCoordinates = true;

        private Thread _thread;
        private UdpClient _udp;
        private IPEndPoint _remoteAny;
        private volatile bool _running;
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

        // Message queue processed on main thread
        private readonly ConcurrentQueue<OSCMessage> _queue = new ConcurrentQueue<OSCMessage>();

        // --- Events you can subscribe to ---
        public event Action<int, int, int, int> OnOk; // loaded, calibState, calibMode, trackingStatus (some are optional per version)
        public event Action<float> OnTime;
        public event Action<string, Vector3, Quaternion, Vector3?, Vector3?> OnRootPose; // name, pos, rot, (opt)scale, (opt)offset
        public event Action<string, Vector3, Quaternion> OnBonePose; // bone name as streamed by MOVIN Studio
        public event Action<string, float> OnBlendShapeValue; // name, value
        public event Action OnBlendShapeApply;
        public event Action<string, Vector3, Quaternion, float> OnCamera; // name, pos, rot, fov
        public event Action<string, Vector3, Quaternion> OnHmdPos;
        public event Action<string, Vector3, Quaternion> OnControllerPos;
        public event Action<string, Vector3, Quaternion> OnTrackerPos;

        // Optional: public getters for last-known states
        public readonly Dictionary<string, (Vector3 pos, Quaternion rot)> BonePoses = new();
        public readonly Dictionary<string, float> BlendshapeValues = new();

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
