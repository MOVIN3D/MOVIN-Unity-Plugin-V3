# Unity C# Integration

For installation and model setup, see the [README](../README.md).

## Core Components

Every runtime script lives in the `MOVIN` namespace, and the low-level OSC parser lives in `MOVIN.OSC`. Nothing is declared in the global namespace, so the plugin coexists with other OSC libraries such as extOSC: a file that only needs the receiver adds `using MOVIN;`, and a short `OSCMessage` reference in that file still resolves to the other library unless `using MOVIN.OSC;` is added as well.

- `MOVIN.MOVINStreamReceiver`
  UDP receiver for MOVIN Studio motion and point cloud streams. It listens on port `11235` by default, parses OSC packets, buffers motion frames by frame index, and applies one completed frame per `Update()` on Unity's main thread. `OnRootPose` and `OnBonePose` events fire for every applied pose; `OnPointCloud` delivers complete point clouds.
- `MOVIN.MocapReceiver`
  Extends `MOVINStreamReceiver`. Applies streamed root and bone poses to a Unity character hierarchy, and scales `<bone>BoneObject` helper meshes on rigs that draw their bones as meshes.
- `MOVIN.MotionStreamMonitorUI`
  Runtime monitor for socket FPS, input FPS, applied frame FPS, frame drops, playback latency, processing errors and stream health.
- `MOVIN.MotionStreamExampleLogger`
  Minimal example that subscribes to the receiver events and logs a few of them.
- `MOVIN.OSC.OSCMessage`, `MOVIN.OSC.OSCParser`
  Dependency-free OSC 1.0 message and bundle parsing used by the receiver. Only needed if you parse OSC packets yourself.

## Stream Protocol

MOVIN Studio streams motion as OSC 1.0 messages over UDP, one message per bone per frame, without bundles. The addresses live in the `MOVIN` namespace and carry the stream target selected in MOVIN Studio:

- `/MOVIN/<target>/Root` with `(int frame, string boneName, float px, py, pz, float qx, qy, qz, qw, float sx, sy, sz)`
- `/MOVIN/<target>/Bone` with `(int frame, string boneName, float px, py, pz, float qx, qy, qz, qw)`

The default target is `Unity`, so a stock receiver listens on `/MOVIN/Unity/Root` and `/MOVIN/Unity/Bone`. Change `Stream Target` on the receiver when MOVIN Studio streams to another target.

- `frame` is a monotonically increasing frame index that groups the messages of one motion frame so they can be buffered and applied together.
- `Root` carries exactly one bone per frame, the first bone of the streamed rig, with its local scale. Every other bone arrives on `Bone`.
- Positions and rotations are local transforms in Unity coordinates and meters, with no axis or unit conversion. Bone names are the transform names of the model loaded in MOVIN Studio; match bones by name, not by arrival order.
- Finger bones are omitted when hand streaming is off in MOVIN Studio; wrists remain included.

**The stream is not VMC.** Earlier releases borrowed the `/VMC/Ext/Root/Pos` and `/VMC/Ext/Bone/Pos` address names for the same payload. MOVIN Studio `v3.2.0` and earlier stream on them. This plugin handles no `/VMC/...` address at all, so those Studio versions need plugin `v3.0.0` instead. Do not point a VMC application at this receiver or MOVIN Studio at a VMC receiver.

## Point Cloud Reception

Enable **Pointcloud** for the Unity target in MOVIN Studio. Motion and point cloud packets share the receiver's UDP port (`11235` by default); Unity does not need a second socket. `MOVINStreamReceiver` and `MocapReceiver` both expose `OnPointCloud`:

```csharp
receiver.OnPointCloud += (frame, points) => { /* consume the complete Vector3[] on Unity's main thread */ };
```

`/MOVIN/PointCloud` carries `(int frame, int totalPoints, int chunkIndex, int chunkCount, int pointsInChunk, float x, y, z, ...)`, with up to 100 points per chunk. Coordinates are already in Unity space; apply any scene placement separately. A zero-point frame delivers an empty array so consumers can clear their display.

The receiver accepts reordered chunks, ignores duplicates, and publishes only the newest complete frame. It retains at most three frames, limits each to 1,000,000 points, and discards old/incomplete frames as newer frames progress. After one second without an accepted point cloud chunk, it resets frame ordering so a restarted Studio can resume. The callback supplies data only; no renderer is created or enabled automatically.

Mapped bone/Transform names must be unique within the selected skeleton subtree. `MocapReceiver` reports duplicate names before building the map instead of silently animating only the last matching Transform.

## Breaking Changes

Plugin `v3.3.0` breaks compatibility with `v3.0.0`. Every script now sits in a namespace, the receiver family no longer carries `VMC` in its name, and the stream moved to `/MOVIN/<target>/...` addresses. Follow [Update an Existing Installation](../README.md#update-an-existing-installation) to preserve script GUIDs and existing scene/prefab references. Code that referenced the old names needs `using MOVIN;` and the renames below.

| Before | After |
|---|---|
| `VMCReceiver` | `MOVIN.MOVINStreamReceiver` |
| `MOVIN.Core.MocapReceiver` | `MOVIN.MocapReceiver` |
| `VMCReceiverMonitorUI` | `MOVIN.MotionStreamMonitorUI` |
| `VMCExampleLogger` | `MOVIN.MotionStreamExampleLogger` |
| `OSCMessage`, `OSCParser` | `MOVIN.OSC.OSCMessage`, `MOVIN.OSC.OSCParser` |
| `/VMC/Ext/Root/Pos`, `/VMC/Ext/Bone/Pos` | `/MOVIN/Unity/Root`, `/MOVIN/Unity/Bone` (the `/VMC` addresses are no longer accepted) |

Removed: the VMC-only events `OnOk`, `OnTime`, `OnBlendShapeValue`, `OnBlendShapeApply`, `OnCamera`, `OnHmdPos`, `OnControllerPos`, `OnTrackerPos`, the `BlendshapeValues` dictionary, `OSCArgReader`, the unused `passthroughUnityCoordinates` option, and the root pose offset argument. `OnRootPose` now has the signature `(string name, Vector3 position, Quaternion rotation, Vector3? scale)`.

The default port and existing serialized receiver fields are preserved.

