# MOVIN Unity Plugin V3

MOVIN Unity Plugin V3 is a Unity sample project and import package for receiving MOVIN/VMC motion capture data over OSC/UDP, previewing streamed characters, and checking stream health in Unity.

## What Is Included

- Complete Unity sample project
- Reusable receiver scripts under `Assets/MOVIN`
- MOVINman V3 and Mixamo sample characters
- Sample scenes for quick smoke testing
- Runtime receiver monitor UI
- Optional stream diagnostics and validation logging
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
6. Check the on-screen `MOVIN Receiver` monitor for packet rate, applied frames, dropped frames, latency, and validation state.

## Sample Scenes

- `Assets/MOVIN/Scenes/Sample_MOVINman.unity`
  MOVINman V3 sample using `Assets/MOVIN/Character/MOVINman/MOVINman_V3.fbx`
- `Assets/MOVIN/Scenes/Sample_Ch14.unity`
  Mixamo Ch14 sample
- `Assets/MOVIN/Scenes/Sample_Ch29.unity`
  Mixamo Ch29 sample

## Using Your Own Character

For best results, use the same `.fbx` character model in MOVIN Studio and Unity. At minimum, the Unity character should have the same bone naming hierarchy as the streamed data.

1. Import your character model into Unity.
2. Place the character in the scene.
3. Add `MOVIN.Core.MocapReceiver` to the character root GameObject.
4. Keep `Listen Port` at `11235`, or set it to the port used by your sender.
5. Leave `Root Bone Name` empty for automatic armature detection. Set it only if the receiver cannot find the correct armature.
6. Optionally add `VMCReceiverMonitorUI` to the same GameObject to show runtime diagnostics.

## Core Components

- `VMCReceiver`
  Lightweight OSC/UDP VMC receiver. It listens on port `11235` by default, parses VMC messages, buffers motion frames, and dispatches data on Unity's main thread.
- `MOVIN.Core.MocapReceiver`
  Applies streamed root and bone poses to a Unity character hierarchy.
- `VMCReceiverMonitorUI`
  Runtime monitor for socket FPS, input FPS, applied frame FPS, frame drops, playback latency, queue size, processing errors, and validation state.

## Supported VMC Messages

Common supported messages include:

- `/VMC/Ext/Root/Pos`
- `/VMC/Ext/Bone/Pos`
- `/VMC/Ext/Blend/Val`
- `/VMC/Ext/Blend/Apply`
- `/VMC/Ext/Cam`

The receiver also exposes events for HMD, controller, tracker, camera, blendshape, root, and bone data.

## Frame Buffering and Drops

MOVIN motion frames are buffered by frame index on the socket receive thread and applied from Unity's main thread in `Update()`.

- `Socket FPS` is the incoming UDP packet rate.
- `Input FPS` is the unique incoming motion frame rate.
- `Applied Frame FPS` is the unique motion frame rate applied to the avatar.
- `maxBufferedFramesBeforeDrop` controls how many completed frames can wait before the receiver jumps to the latest completed frame.
- The default drop threshold is `6`, which is about `0.1` seconds of buffered motion at a 60 FPS sender.
- Dropped frames are shown in the monitor so slow rendering is visible instead of silently increasing latency.

## Stream Diagnostics and Validation

Validation logging is enabled by default. MOVIN Studio can control a validation session with:

- `/MOVIN/StreamValidation/Begin`
  Arguments: `sessionId`, `target`, reserved value, output directory. `target` must be `Unity`.
- `/MOVIN/StreamValidation/End`
  Arguments: `sessionId`, `target`.

When a session is active, Unity writes:

- `<sessionId>_Plugin`
  Raw UDP datagrams as base64 lines.
- `<sessionId>_PluginApplied`
  Applied transform poses rounded to six decimal places.

When `validationLogDirectory` is empty, the default directory is:

```text
Documents/MOVIN Studio/StreamValidation/Unity
```

## Troubleshooting

- No packets are shown in the monitor:
  Check the sender destination IP, UDP port `11235`, firewall rules, and whether another app is already using the same port.
- Packets arrive but the character does not move:
  Confirm that `MocapReceiver` is on the character root, the streamed bone names match the Unity hierarchy, and `Root Bone Name` is set only when needed.
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
    Scenes/
    Scripts/
    Tests/
Packages/
ProjectSettings/
MOVIN-Unity-Plugin-V3.unitypackage
```

## License

Copyright 2026 MOVIN. All Rights Reserved.
