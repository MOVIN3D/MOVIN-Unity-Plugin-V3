# MOVIN Unity Plugin V3 (MOVIN Studio v3.0.0+)

MOVIN Unity Plugin V3 is a Unity sample project and import package for receiving the MOVIN Studio motion stream over OSC/UDP, previewing streamed characters, and checking stream health in Unity.

## What Is Included

- Complete Unity sample project
- Reusable receiver scripts under `Assets/MOVIN`, all inside the `MOVIN` namespace
- MOVINman V3 and Mixamo sample characters
- Sample scenes for quick smoke testing
- Runtime receiver monitor UI
- `MOVIN-Unity-Plugin-V3.unitypackage` for importing the plugin into another Unity project

## Requirements

- Unity `6000.4.10f1`
- Universal Render Pipeline (URP) `17.4.0`
- Input System `1.19.0`
- A MOVIN Studio build that streams on the `/MOVIN/<target>/...` addresses. Older builds that still stream on `/VMC/...` are not supported by this plugin version.
- Git LFS when cloning this repository, because the Unity package is stored as an LFS object

## Installation

### Option 1. Open the Sample Project

1. Install Git LFS.

   ```bash
   git lfs install
   ```

2. Clone this repository.

   ```bash
   git clone https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3.git
   ```

3. Open the cloned folder from Unity Hub with Unity `6000.4.10f1`.

### Option 2. Import the Unity Package

1. Open your target Unity project.
2. Select `Assets > Import Package > Custom Package...`.
3. Choose `MOVIN-Unity-Plugin-V3.unitypackage`.
4. Import the package contents.
5. Add the receiver component and character assets you need to your scene.

## Quick Start

1. Open `Assets/MOVIN/Scenes/Sample_MOVINman.unity`.
2. Enter Play Mode.
3. In MOVIN Studio, set the Unity machine as the stream destination.
4. Set the destination port to `11235`, the port the receiver listens on by default.
5. If packets do not arrive, allow inbound UDP traffic for Unity on port `11235` in the firewall.
6. Check the on-screen `MOVIN Receiver` monitor for packet rate, applied frames, dropped frames, and latency.

## Sample Scenes

- `Assets/MOVIN/Scenes/Sample_MOVINman.unity`
  MOVINman V3 sample using `Assets/MOVIN/Character/MOVINman/MOVINman_V3.prefab`
- `Assets/MOVIN/Scenes/Sample_Ch14.unity`
  Mixamo Ch14 sample
- `Assets/MOVIN/Scenes/Sample_Ch29.unity`
  Mixamo Ch29 sample

## Using Your Own Character

Use the same character model in MOVIN Studio and Unity. This is required rather than a preference, because the receiver applies streamed bone positions as well as rotations: the skeleton takes both its bone names and its proportions from the model loaded in MOVIN Studio. A different model ends up driven by foreign bone lengths, and any bone whose name does not match is left behind entirely.

1. Import your character model into Unity.
2. Place the character in the scene.
3. Add `MOVIN.MocapReceiver` to the character root GameObject.
4. Keep `Listen Port` at `11235`, or set it to the port used by your sender.
5. Keep `Stream Target` at `Unity` unless MOVIN Studio is streaming to a different target. The receiver listens on `/MOVIN/<Stream Target>/Root` and `/MOVIN/<Stream Target>/Bone`, and the value is applied when the receiver starts.
6. Leave `Root Bone Name` empty. The receiver detects the skeleton root, and if that guess falls short it adopts the root name the sender includes in every root pose. Set it only to pin a specific root, such as driving the upper body alone.
7. Optionally add `MOVIN.MotionStreamMonitorUI` to the same GameObject to show runtime diagnostics.

`Scale Bone Objects` is on by default and only affects rigs that draw their bones as meshes, where every joint owns a `<bone>BoneObject` child. On such a rig those helper meshes are scaled so the drawn bones keep reaching the next joint when the streamed bone lengths differ from the character's rest pose. A plain `.fbx` with a joint hierarchy and one skinned mesh has no helper objects, so the option does nothing.

## Core Components

Every runtime script lives in the `MOVIN` namespace, and the low-level OSC parser lives in `MOVIN.OSC`. Nothing is declared in the global namespace, so the plugin coexists with other OSC libraries such as extOSC: a file that only needs the receiver adds `using MOVIN;`, and a short `OSCMessage` reference in that file still resolves to the other library unless `using MOVIN.OSC;` is added as well.

- `MOVIN.MotionStreamReceiver`
  UDP receiver for the MOVIN Studio motion stream. It listens on port `11235` by default, parses OSC packets, buffers motion frames by frame index, and applies one completed frame per `Update()` on Unity's main thread. `OnRootPose` and `OnBonePose` events fire for every applied pose.
- `MOVIN.MocapReceiver`
  Extends `MotionStreamReceiver`. Applies streamed root and bone poses to a Unity character hierarchy, and scales `<bone>BoneObject` helper meshes on rigs that draw their bones as meshes.
- `MOVIN.MotionStreamMonitorUI`
  Runtime monitor for socket FPS, input FPS, applied frame FPS, frame drops, playback latency, queue size, processing errors, and validation state.
- `MOVIN.MotionStreamExampleLogger`
  Minimal example that subscribes to the receiver events and logs a few of them.
- `MOVIN.OSC.OSCMessage`, `MOVIN.OSC.OSCParser`
  Dependency-free OSC 1.0 message and bundle parsing used by the receiver. Only needed if you parse OSC packets yourself.

## Stream Protocol

MOVIN Studio streams motion as OSC 1.0 messages over UDP, one message per bone per frame, without bundles. The addresses live in the `MOVIN` namespace and carry the stream target selected in MOVIN Studio:

- `/MOVIN/<target>/Root` with `(int frame, string boneName, float px, py, pz, float qx, qy, qz, qw, float sx, sy, sz)`
- `/MOVIN/<target>/Bone` with `(int frame, string boneName, float px, py, pz, float qx, qy, qz, qw)`

The default target is `Unity`, so a stock receiver listens on `/MOVIN/Unity/Root` and `/MOVIN/Unity/Bone`. Change `Stream Target` on the receiver when MOVIN Studio streams to another target.

- `frame` is a monotonically increasing frame index that groups the messages of one motion frame so they can be buffered and applied together. During a stream validation session it is negative, encoded as `-(index + 1)`.
- `Root` carries exactly one bone per frame, the first bone of the streamed rig, with its local scale. Every other bone arrives on `Bone`.
- Positions and rotations are local transforms in Unity coordinates and meters, with no axis or unit conversion. Bone names are the transform names of the model loaded in MOVIN Studio; match bones by name, not by arrival order.
- Wrist and finger bones are omitted when hand streaming is off in MOVIN Studio.
- `/MOVIN/StreamValidation/Begin` and `/MOVIN/StreamValidation/End` are control messages used by the diagnostic described under Stream Validation.

**The stream is not VMC.** Earlier releases borrowed the `/VMC/Ext/Root/Pos` and `/VMC/Ext/Bone/Pos` address names for the same payload. This plugin handles no `/VMC/...` address at all, so a MOVIN Studio build that still streams on them has to be updated before it can drive this receiver. Do not point a VMC application at this receiver or MOVIN Studio at a VMC receiver.

## Frame Buffering and Drops

MOVIN motion frames are buffered by frame index on the socket receive thread and applied from Unity's main thread in `Update()`.

- `Socket FPS` is the incoming UDP packet rate.
- `Input FPS` is the unique incoming motion frame rate.
- `Applied Frame FPS` is the unique motion frame rate applied to the avatar.
- `maxBufferedFramesBeforeDrop` controls how many completed frames can wait before the receiver jumps to the latest completed frame.
- The default drop threshold is `6`, which is about `0.1` seconds of buffered motion at a 60 FPS sender.
- Dropped frames are shown in the monitor so slow rendering is visible instead of silently increasing latency.

## Stream Validation

The `Validation Logging` fields on the receiver, and the `Validation` row in the monitor, belong to a diagnostic MOVIN uses when tracing a stream problem. MOVIN Studio starts and stops the session, so leave these at their defaults and ignore them during normal use. Nothing is written unless MOVIN Studio asks for it. The session target must match the receiver's `Stream Target`.

If MOVIN support requests logs, they are written under:

```text
Documents/MOVIN Studio/StreamValidation/Unity
```

## Breaking Changes

Every script now sits in a namespace, the receiver family no longer carries `VMC` in its name, and the stream moved to `/MOVIN/<target>/...` addresses. Prefabs and scenes reference scripts by GUID, so existing scenes and prefabs keep working after updating. Code that referenced the old names needs `using MOVIN;` and the renames below.

| Before | After |
|---|---|
| `VMCReceiver` | `MOVIN.MotionStreamReceiver` |
| `MOVIN.Core.MocapReceiver` | `MOVIN.MocapReceiver` |
| `VMCReceiverMonitorUI` | `MOVIN.MotionStreamMonitorUI` |
| `VMCExampleLogger` | `MOVIN.MotionStreamExampleLogger` |
| `OSCMessage`, `OSCParser` | `MOVIN.OSC.OSCMessage`, `MOVIN.OSC.OSCParser` |
| `/VMC/Ext/Root/Pos`, `/VMC/Ext/Bone/Pos` | `/MOVIN/Unity/Root`, `/MOVIN/Unity/Bone` (the `/VMC` addresses are no longer accepted) |

Removed: the VMC-only events `OnOk`, `OnTime`, `OnBlendShapeValue`, `OnBlendShapeApply`, `OnCamera`, `OnHmdPos`, `OnControllerPos`, `OnTrackerPos`, the `BlendshapeValues` dictionary, `OSCArgReader`, the unused `passthroughUnityCoordinates` option, and the root pose offset argument. `OnRootPose` now has the signature `(string name, Vector3 position, Quaternion rotation, Vector3? scale)`.

The default port, the serialized receiver fields, and the frame buffering behavior are unchanged.

## Troubleshooting

- No packets are shown in the monitor:
  Check the sender destination IP, UDP port `11235`, firewall rules, and whether another app is already using the same port.
- Packets arrive but the character does not move:
  Confirm that `MocapReceiver` is on the character root, that `Stream Target` matches the target MOVIN Studio streams to, that the streamed bone names match the Unity hierarchy, and that `Root Bone Name` is set only when needed. Enable `Verbose Logging` to see every incoming address in the Console. Addresses starting with `/VMC/` mean MOVIN Studio is older than this plugin and needs updating.
- Only part of the character moves, such as the upper body:
  The mapped bones stop short of the rest of the skeleton. `MocapReceiver` logs a warning naming every streamed bone it could not match, so check the Console and then set `Root Bone Name` to the top of your skeleton, for example `RootBone` or `Hips`.
- Motion is delayed or frames are dropped:
  Check Unity performance, monitor `Pose Buffer` and `Dropped`, and tune `maxBufferedFramesBeforeDrop` if needed.
- The Unity package is missing or tiny after cloning:
  Run `git lfs install` and `git lfs pull`.

## Project Structure

```text
Assets/
  MOVIN/
    Character/
      MOVINman/
      Ch14_mixamo/
      Ch29_mixamo/
    Resources/
    Scenes/
    Scripts/
    Tests/
Packages/
ProjectSettings/
MOVIN-Unity-Plugin-V3.unitypackage
```

## License

Copyright 2026 MOVIN. All Rights Reserved.
