# MOVIN Unity Plugin V3

MOVIN Unity Plugin V3 is a Unity sample project and import package for receiving MOVIN/VMC motion capture data over OSC/UDP, previewing streamed characters, and checking stream health in Unity.

## What Is Included

- Complete Unity sample project
- Reusable receiver scripts under `Assets/MOVIN`
- MOVINman V3 and Mixamo sample characters
- Sample scenes for quick smoke testing
- Runtime receiver monitor UI
- `MOVIN-Unity-Plugin-V3.unitypackage` for importing the plugin into another Unity project

## Requirements

- Unity `6000.4.10f1`
- Universal Render Pipeline (URP) `17.4.0`
- Input System `1.19.0`
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
3. In MOVIN Studio or another VMC sender, set the Unity machine as the destination.
4. Send VMC/OSC UDP data to port `11235`.
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
3. Add `MOVIN.Core.MocapReceiver` to the character root GameObject.
4. Keep `Listen Port` at `11235`, or set it to the port used by your sender.
5. Leave `Root Bone Name` empty. The receiver detects the skeleton root, and if that guess falls short it adopts the root name the sender includes in every root pose. Set it only to pin a specific root, such as driving the upper body alone.
6. Optionally add `VMCReceiverMonitorUI` to the same GameObject to show runtime diagnostics.

`Scale Bone Objects` is on by default and only affects rigs that draw their bones as meshes, where every joint owns a `<bone>BoneObject` child. On such a rig those helper meshes are scaled so the drawn bones keep reaching the next joint when the streamed bone lengths differ from the character's rest pose. A plain `.fbx` with a joint hierarchy and one skinned mesh has no helper objects, so the option does nothing.

## Core Components

- `VMCReceiver`
  Lightweight OSC/UDP VMC receiver. It listens on port `11235` by default, parses VMC messages, buffers motion frames, and dispatches data on Unity's main thread.
- `MOVIN.Core.MocapReceiver`
  Applies streamed root and bone poses to a Unity character hierarchy, and scales `<bone>BoneObject` helper meshes on rigs that draw their bones as meshes.
- `VMCReceiverMonitorUI`
  Runtime monitor for socket FPS, input FPS, applied frame FPS, frame drops, playback latency, queue size, processing errors, and validation state.

## Supported VMC Messages

MOVIN motion arrives on two addresses, and these are the only ones the plugin applies to a character:

- `/VMC/Ext/Root/Pos`
- `/VMC/Ext/Bone/Pos`

The receiver also parses the rest of the common VMC surface, including `/VMC/Ext/Blend/Val`, `/VMC/Ext/Blend/Apply`, `/VMC/Ext/Cam`, and the HMD, controller, and tracker addresses. Those are exposed as C# events for your own code to handle. The plugin does not consume them and ships no blendshape or camera handling of its own.

## Frame Buffering and Drops

MOVIN motion frames are buffered by frame index on the socket receive thread and applied from Unity's main thread in `Update()`.

- `Socket FPS` is the incoming UDP packet rate.
- `Input FPS` is the unique incoming motion frame rate.
- `Applied Frame FPS` is the unique motion frame rate applied to the avatar.
- `maxBufferedFramesBeforeDrop` controls how many completed frames can wait before the receiver jumps to the latest completed frame.
- The default drop threshold is `6`, which is about `0.1` seconds of buffered motion at a 60 FPS sender.
- Dropped frames are shown in the monitor so slow rendering is visible instead of silently increasing latency.

## Stream Validation

The `Validation Logging` fields on the receiver, and the `Validation` row in the monitor, belong to a diagnostic MOVIN uses when tracing a stream problem. MOVIN Studio starts and stops the session, so leave these at their defaults and ignore them during normal use. Nothing is written unless MOVIN Studio asks for it.

If MOVIN support requests logs, they are written under:

```text
Documents/MOVIN Studio/StreamValidation/Unity
```

## Troubleshooting

- No packets are shown in the monitor:
  Check the sender destination IP, UDP port `11235`, firewall rules, and whether another app is already using the same port.
- Packets arrive but the character does not move:
  Confirm that `MocapReceiver` is on the character root, the streamed bone names match the Unity hierarchy, and `Root Bone Name` is set only when needed.
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
