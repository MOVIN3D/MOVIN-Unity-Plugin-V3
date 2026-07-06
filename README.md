# MOVIN Unity Plugin V3

This repository contains the MOVIN Unity plugin V3 project for receiving, previewing, and validating MOVIN/VMC data in Unity.

It includes:

- a complete Unity sample project
- reusable receiver scripts under `Assets/MOVIN`
- sample characters and scenes for smoke testing
- optional stream diagnostics and validation logging
- a Unity package artifact for importing `Assets/MOVIN` into another Unity project

## Branch And Release Model

- `main` is kept as the stable release line.
- `develop` is the current V3 integration branch.
- Distribution is based on this project directly. Do not maintain a second copy of the receiver code by hand.

## Environment

- Unity `6000.4.10f1`
- Universal Render Pipeline (URP) `17.4.0`
- Input System `1.19.0`
- Git LFS for pulling `MOVIN-Unity-Plugin-V3.unitypackage`

## Included Contents

- `Assets/MOVIN`
  Sample assets, characters, scenes, and scripts related to MOVIN
- `Assets/MOVIN/Scripts/Core/VMCReceiver.cs`
  A lightweight OSC/UDP-based VMC receiver for Unity. The receiver is split across partial class files for lifecycle, dispatch, buffering, monitoring, and diagnostics.
- `Assets/MOVIN/Scripts/Core/MocapReceiver.cs`
  A component that applies received bone poses to a Unity character transform hierarchy
- `Assets/MOVIN/Scripts/Core/VMCReceiverMonitorUI.cs`
  A runtime monitor for receive rate, main thread rate, buffering, drops, latency, and diagnostics state
- `Assets/MOVIN/Tests`
  EditMode coverage for frame parsing, buffering, drop behavior, and validation formatting
- `MOVIN-Unity-Plugin-V3.unitypackage`
  Optional package artifact for importing `Assets/MOVIN` into another Unity project

## Getting Started

### Option 1. Open the Sample Project

1. Open this repository folder from Unity Hub.
2. Use Unity Editor version `6000.4.10f1`.
3. Open one of the sample scenes under `Assets/MOVIN/Scenes`.
4. Enter Play Mode and connect your MOVIN or VMC data source.

Recommended smoke scenes:

- `Assets/MOVIN/Scenes/Sample_MOVINman.unity`
- `Assets/MOVIN/Scenes/Sample_Ch14.unity`
- `Assets/MOVIN/Scenes/Sample_Ch29.unity`

### Option 2. Import A Unity Package

1. Open your target Unity project.
2. Select `Assets > Import Package > Custom Package...`.
3. Choose `MOVIN-Unity-Plugin-V3.unitypackage`.
4. Import the package and place the required assets or scripts into your scene.
5. Make sure the same `.fbx` character model used in MOVIN Studio is also loaded in your Unity project.

## How It Works

`VMCReceiver` listens for VMC messages over UDP, using port `11235` by default.
While enabled, it keeps Unity running in the background by default, disables vSync, and sets `Application.targetFrameRate` to `120`. Those application settings are restored when the receiver stops.

Supported message examples include:

- `/VMC/Ext/Root/Pos`
- `/VMC/Ext/Bone/Pos`
- `/VMC/Ext/Blend/Val`
- `/VMC/Ext/Blend/Apply`
- `/VMC/Ext/Cam`

`MocapReceiver` maps incoming bone names to Unity transforms and applies the received local position and rotation values.

For best results, MOVIN Studio and the Unity project should use the same `.fbx` character model, or at minimum a character with the exact same bone naming hierarchy.

## Frame Buffering And Drops

MOVIN motion frames are buffered by frame index on the socket receive thread and applied from Unity's main thread in `Update()`.

- The socket thread blocks on UDP receive and records incoming datagrams as they arrive.
- `Socket FPS` in the monitor is the incoming packet rate, separate from Unity main thread FPS.
- The main thread applies at most one completed motion frame per `Update()`.
- `maxBufferedFramesBeforeDrop` controls how many completed frames can wait before the receiver jumps to the latest completed frame.
- The default drop threshold is `6`, which is about `0.1` seconds of buffered motion at a 60 FPS sender.
- Dropped frames are counted in the monitor so slow rendering is visible instead of silently increasing latency.

## Runtime Monitor

Add `VMCReceiverMonitorUI` to the same GameObject as a `VMCReceiver` or `MocapReceiver` to show runtime health information.

Key rows:

- `Socket FPS`: incoming UDP packet rate
- `Input FPS`: unique incoming motion frame rate
- `Applied Frame FPS`: unique motion frame rate applied to the avatar
- `Main Tick FPS`: Unity main thread update rate; below `90` is yellow and below `60` is red
- `Dropped`: skipped motion frames caused by buffer catch-up
- `Playback Latency`: time between the latest pose packet for a frame and the frame being applied
- `Pose Buffer`: pending completed motion frames
- `Main Msg Queue`: non-motion OSC messages waiting for main-thread dispatch
- `Processing Error`: receive-thread buffering/raw-log errors plus main-thread dispatch errors
- `Validation`: diagnostics logging state

## Stream Diagnostics And Validation

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

If no explicit begin packet arrives, the receiver can attach to a fresh `<sessionId>_App` marker file in the validation directory. Stale files and files with a mismatched validation header are ignored. When `validationLogDirectory` is empty, the default directory is `Documents/MOVIN Studio/StreamValidation/Unity`.

## Project Structure

```text
Assets/
  MOVIN/
    Character/
      MOVINman/
    Scenes/
    Scripts/
    Tests/
Packages/
ProjectSettings/
MOVIN-Unity-Plugin-V3.unitypackage
```

## Release Checklist

Before distributing:

1. Run the Unity `6000.4.10f1` compile check.
2. Run EditMode tests.
3. Open the sample scenes and smoke test receiver start/stop, monitor UI, pose application, and validation begin/end logging.
4. Run `git diff --check`.
5. Run `git lfs status`.
6. If publishing a package artifact, export `Assets/MOVIN` manually as `MOVIN-Unity-Plugin-V3.unitypackage` and import it into a clean Unity project for a smoke test.

## License

Copyright 2026 MOVIN. All Rights Reserved.
