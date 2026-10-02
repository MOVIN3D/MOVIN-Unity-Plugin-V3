# MOVIN Unity Plugin v3.3.0

Receive motion and point clouds from MOVIN Studio, animate a Unity character, and
view connection, model matching and received FPS in Studio.

## Requirements

| MOVIN Studio | Plugin download |
|---|---|
| v3.3.0 or later | [v3.3.0](https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3/releases/tag/v3.3.0) |
| v3.0.0–v3.2.0 | [v3.0.0](https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3/releases/tag/v3.0.0) |

- Unity Editor: **6000.0.43f1** or **6000.4.10f1**.
- Core has no URP dependency. Sample scenes require URP **17.0.4** with Unity
  6000.0.43f1, or **17.4.0** with Unity 6000.4.10f1.
- Use the same character model in Studio and Unity, with unique bone names.

## Installation

Download the packages from the [latest release](https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3/releases/latest).

| Package | Use |
|---|---|
| **Core** | Required receiver components and monitor UI |
| **Samples** | Optional MOVINman, Ch14 and Ch29 characters and URP scenes |

1. Import **Core** using **Assets > Import Package > Custom Package**.
2. To use the sample scenes, install URP and import **Samples** after Core.

Import only the `.unitypackage` files. Installation through a Git URL in Unity
Package Manager is not supported.

## Quick Start

1. Open `Assets/MOVIN/Scenes/Sample_MOVINman.unity` and enter **Play Mode**.
2. In Studio, select **Unity** and the **MOVINMan** source.
3. Set the destination to the Unity computer's IPv4 address, or `127.0.0.1` when
   both apps run on the same computer. Use port **11235** in both apps.
4. Click **Start Streaming**. Enable **Hand** in Studio when you want finger motion.

The sample monitor shows incoming data and applied motion. Studio shows connection
status, model matching and received FPS.

## Using Your Own Character

1. Load the same character model in Studio and Unity.
2. Add **MOVIN.MocapReceiver** to the character's root GameObject.
3. Set **Listen Port** to the Studio destination port and **Stream Target** to `Unity`.
4. Leave **Root Bone Name** empty for automatic detection. If only part of the
   skeleton moves, set it to the top bone of the intended skeleton.
5. Set **Character Name** to the model name shown in Studio. If empty, the receiver
   uses its GameObject name, excluding a trailing `(Clone)`.
6. Enter Play Mode and start streaming. Add **MOVIN.MotionStreamMonitorUI** if you
   want the receiver monitor on your own character.

The receiver applies bone positions and rotations from the streamed model.
A different model or duplicate bone names can prevent correct motion application.

To receive point clouds, enable **Pointcloud** in Studio. The receiver exposes the
points through `OnPointCloud`; displaying them requires a renderer in your project.
See [C# integration](docs/integration.md) for the event and component APIs.

## Update an Existing Installation

1. Back up your project and close Unity.
2. Move `Assets/MOVIN/Scripts/Core` and `Assets/MOVIN/Scripts/Core.meta` outside
   `Assets`. If present, also move `Assets/MOVIN/Tests` and its `.meta` file.
   Keep your scenes, characters and prefabs in place.
3. Reopen Unity and import the current **Core** package with all files selected
   before opening or saving your scenes.
4. Restore any custom changes carefully. Do not copy obsolete receiver scripts
   or `MOVINStreamReceiver.Validation.cs` and its `.meta` file back into Assets.
5. Update **Samples** only if needed; back up customized sample assets first.

Importing over an old installation does not remove obsolete files. If your own C#
scripts use the v3.0.0 APIs, follow the [API migration notes](docs/integration.md#breaking-changes).

## Troubleshooting

| Problem | Check |
|---|---|
| No connection or motion | Enter Play Mode, enable the receiver, and check the destination IP and matching UDP port. Close any other app using that port. |
| Packets arrive but the model does not move | Check the Studio/plugin version pair, `Stream Target`, model and bone names. |
| `MISMATCH` in Studio | Use the same model on both sides and check `Character Name` and the selected skeleton root. |
| Only some bones move | Check Unity Console for unmatched names and confirm `Root Bone Name` includes the whole skeleton. |
| Low received FPS | Check Studio's sending rate and network connection. |
| Motion is delayed despite normal received FPS | Check Unity's frame rate and the monitor's applied FPS and dropped frames. |

On separate computers, allow the receiver's UDP port (**11235** by default) into
Unity and status replies on UDP **39581** into Studio. A working status connection
does not by itself mean motion is being received or applied.

## License

Copyright 2026 MOVIN. All Rights Reserved.
