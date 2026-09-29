# MOVIN Unity Plugin v3.3.0

Receive MOVIN Studio motion and point clouds over OSC/UDP, animate Unity characters, and report
receiver status, character compatibility and received FPS back to Studio. Requires MOVIN Studio
`v3.3.0` or later.

**v3.3.0 was refreshed on 2026-09-29.** The current Core package excludes internal Stream Validation
code, settings, log capture and monitor rows. If you installed the initial v3.3.0 package, download
the current files and follow [Update an Existing Installation](#update-an-existing-installation).
The version number is unchanged; use the current release checksums to identify the package.

## Which Version Do I Need?

Pick the plugin by the MOVIN Studio version you run. The two streams are not compatible, so a mismatched pair connects but the character does not move.

| MOVIN Studio | Plugin | Download |
|---|---|---|
| `v3.3.0` or later | `v3.3.0` or later | [Latest release](https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3/releases/latest) |
| `v3.0.0` to `v3.2.0` | `v3.0.0` | [v3.0.0](https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3/releases/tag/v3.0.0) |

A plugin version names the lowest MOVIN Studio version it supports. A patch release such as `v3.3.1` is a plugin-only fix for the same MOVIN Studio range and does not require MOVIN Studio `v3.3.1`.

## What Is Included

- **Core**: reusable motion/point-cloud receivers, Studio status replies and runtime monitor UI
- **Samples**: MOVINman V3 and Mixamo characters with three URP sample scenes

The repository also contains the complete sample project, regression tests and development tools.
Tests, release tooling and internal Stream Validation are excluded from the user packages.

## Requirements

| Component | Requirements |
|---|---|
| Core | Verified with Unity `6000.0.43f1` and `6000.4.10f1`; no URP, Input System or Test Framework dependency |
| Samples | Core plus URP; verified with URP `17.0.4` / Unity `6000.0.43f1` and URP `17.4.0` / Unity `6000.4.10f1` |
| Repository sample project | Unity `6000.4.10f1`, URP `17.4.0`; project dependencies install through Package Manager |
| MOVIN Studio | `v3.3.0` or later; Studio `v3.0.0`–`v3.2.0` uses plugin [v3.0.0](https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3/releases/tag/v3.0.0) |

These are tested Editor versions, not a promise of compatibility with every Unity version or build target.

## Installation

### Import into Your Project

Download from [v3.3.0](https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3/releases/tag/v3.3.0):

| File | Purpose |
|---|---|
| [MOVIN-Unity-Plugin-Core-v3.3.0.unitypackage](https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3/releases/download/v3.3.0/MOVIN-Unity-Plugin-Core-v3.3.0.unitypackage) | Required receiver scripts and monitor theme; import first |
| [MOVIN-Unity-Plugin-Samples-v3.3.0.unitypackage](https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3/releases/download/v3.3.0/MOVIN-Unity-Plugin-Samples-v3.3.0.unitypackage) | Optional sample characters and scenes; requires Core and URP |
| `release-manifest.json`, `SHA256SUMS.txt` | Exported asset GUIDs, source commit and file checksums |
| `verification.json`, `unity-verification.zip` | Installation and regression test results for these package hashes |

Only the two `.unitypackage` files are imported into Unity. The manifest, checksums and test reports
are release records; keep them outside `Assets`.

1. Import `MOVIN-Unity-Plugin-Core-v3.3.0.unitypackage` through `Assets > Import Package > Custom Package...`.
2. For your own character, add `MOVIN.MocapReceiver`; sample assets are optional.
3. To use the sample characters/scenes, install URP and import `MOVIN-Unity-Plugin-Samples-v3.3.0.unitypackage` after Core. Use a URP project or configure its render pipeline first.

Do not import the old all-in-one package after the new Core package. Download generated packages
from GitHub Releases. Unity Package Manager installation via a Git URL is not supported.

### Open the Sample Project

```bash
git clone --branch v3.3.0 https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3.git
```

Open the folder from Unity Hub with Unity `6000.4.10f1`. For development, check out `release` or a
feature branch instead. Git LFS is only needed when checking out historical versions containing the
old package binary; the current source project does not require it.

### Update an Existing Installation

These steps apply to v3.0.0, namespace previews and the initial v3.3.0 package:

1. Back up or commit your Unity project, then close Unity.
2. Move `Assets/MOVIN/Scripts/Core` **and `Assets/MOVIN/Scripts/Core.meta` outside `Assets`**.
   Also move `Assets/MOVIN/Tests` and `Assets/MOVIN/Tests.meta` if present. Keep these backups and
   any custom code. Leave your scenes, characters and prefabs in place.
3. Reopen Unity and import the current Core package with all files selected before opening or
   saving your scenes. Preserved script GUIDs retain the component links and serialized settings.
4. Merge custom code back using the new API names. Do not restore obsolete receiver scripts or
   `MOVINStreamReceiver.Validation.cs` and its `.meta` file from the backup.
5. Import Samples only if you want to update the examples. Back up customized sample assets
   outside their original package paths first, because importing Samples can overwrite them.

An overlay import cannot remove obsolete files. Old renamed scripts can cause duplicate GUIDs,
and old tests or the initial v3.3.0 Validation source can cause compilation errors. After updating,
`Assets/MOVIN/Scripts/Core/MOVINStreamReceiver.Validation.cs` must be absent.

Custom C# code using old type or event names must follow the [Breaking Changes](#breaking-changes)
table. Importing an asset package cannot rewrite your scripts. Use the new plugin together with
Studio 3.3.0+; retain plugin 3.0.0 for an older Studio installation.

## Quick Start

1. Open `Assets/MOVIN/Scenes/Sample_MOVINman.unity`.
2. Enter Play Mode.
3. In MOVIN Studio, select the Unity target and MOVINMan source, then set the Unity machine's IPv4 address as the destination (`127.0.0.1` when both apps run on the same computer).
4. Set the destination port to `11235`, enable Motion, and click Start Streaming.
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

## Frame Buffering and Drops

MOVIN motion frames are buffered by frame index on the socket receive thread and applied from Unity's main thread in `Update()`.

- `Socket FPS` is the incoming UDP packet rate.
- `Input FPS` counts newly buffered motion frames; reordered bones in the same frame are counted once.
- `Applied Frame FPS` is the unique motion frame rate applied to the avatar.
- `maxBufferedFramesBeforeDrop` controls how many completed frames can wait before the receiver jumps to the latest completed frame.
- The default drop threshold is `6`, which is about `0.1` seconds of buffered motion at a 60 FPS sender. The receive thread retains at most the threshold plus one frame, even while Unity is paused. The supported threshold range is `1` to `120`; each frame accepts up to `1024` distinct bone names.
- Dropped frames are shown in the monitor so slow rendering is visible instead of silently increasing latency.

A sender restart can reset its frame counter. After one second without an accepted motion frame, the receiver clears its frame ordering baseline and accepts the new stream. Ordinary late packets before that timeout remain ignored. The current protocol has no session identifier, so this is timeout-based recovery; a sufficiently delayed packet after an idle period cannot be distinguished from a new session.

A newer frame or 50 ms without a newer frame makes the current frame eligible for playback. UDP can still lose or reorder individual bone packets; this protocol does not declare the expected bone count or a frame-end marker. Monitor latency measures local buffering delay, not network end-to-end latency.

Malformed OSC messages, unsupported argument types, non-finite pose values, and zero-length quaternions are rejected before application. OSC bundle nesting is limited to 16 levels. Unknown addresses, including internal Stream Validation controls, are ignored on the receive thread in user packages.

## Breaking Changes

Plugin `v3.3.0` breaks compatibility with `v3.0.0`. Every script now sits in a namespace, the receiver family no longer carries `VMC` in its name, and the stream moved to `/MOVIN/<target>/...` addresses. Follow the upgrade steps above to preserve script GUIDs and existing scene/prefab references. Code that referenced the old names needs `using MOVIN;` and the renames below.

| Before | After |
|---|---|
| `VMCReceiver` | `MOVIN.MOVINStreamReceiver` |
| `MOVIN.Core.MocapReceiver` | `MOVIN.MocapReceiver` |
| `VMCReceiverMonitorUI` | `MOVIN.MotionStreamMonitorUI` |
| `VMCExampleLogger` | `MOVIN.MotionStreamExampleLogger` |
| `OSCMessage`, `OSCParser` | `MOVIN.OSC.OSCMessage`, `MOVIN.OSC.OSCParser` |
| `/VMC/Ext/Root/Pos`, `/VMC/Ext/Bone/Pos` | `/MOVIN/Unity/Root`, `/MOVIN/Unity/Bone` (the `/VMC` addresses are no longer accepted) |

Removed: the VMC-only events `OnOk`, `OnTime`, `OnBlendShapeValue`, `OnBlendShapeApply`, `OnCamera`, `OnHmdPos`, `OnControllerPos`, `OnTrackerPos`, the `BlendshapeValues` dictionary, `OSCArgReader`, the unused `passthroughUnityCoordinates` option, and the root pose offset argument. `OnRootPose` now has the signature `(string name, Vector3 position, Quaternion rotation, Vector3? scale)`.

The default port and existing serialized receiver fields are preserved. Frame storage is now bounded on receipt, and frame counters recover after the timeout described above.

## Troubleshooting

### Streaming status in MOVIN Studio

Studio queries the selected Unity receiver once per second, including before streaming starts.
The plugin replies from the Unity main thread to Studio's shared UDP port `39581`; no Studio IP
setting is needed in the plugin. Unity must be in Play mode and the receiver must be enabled.
Studio shows `not connected` after three seconds without a valid reply. Streaming continues if
status replies are unavailable, including with older plugins that do not implement this feature.

The streaming message window uses this layout:

```text
Unity: connected

Source: Ch14_nonPBR | 57 Bones
Target: Ch14_nonPBR | 57 Bones
Match: Name OK | Bone Count OK | Bone Names OK

Received FPS: Motion 60.0 fps | Pointcloud 60.0 fps
```

Unknown values are `-`. Any `MISMATCH` adds a red reminder below Match to check that Studio and
Unity use the same character model. Motion and point cloud FPS are green at 57 or above (95% of
60 FPS), red below 57. Stopped, disabled, expired or wrong-sender data cannot display an old FPS.
A plain `MOVINStreamReceiver` reports processing FPS but has no target character to compare.
Point cloud FPS confirms complete-cloud delivery on the main thread, not rendering.

For `MocapReceiver`, Studio also compares the sender and target **character names, full skeleton
bone counts, and complete sets of bone names** independently. Both counts include the full skeleton,
so disabling finger streaming does not produce a skeleton mismatch.
Target counts exclude MOVIN bone/ joint drawing helpers. These are name-set comparisons, not checks
of bind poses, hierarchy or mesh contents.

Set **Character Name** on `MocapReceiver` to the imported model name shown by Studio. If blank, it
uses the receiver GameObject's name with a trailing `(Clone)` removed. The MOVINMan prefab sets
this to `MOVINMan`. Renaming the scene object can therefore be independent of the model label.
Names are compared exactly, including case. The receiver reports its own skeleton independently
of the packets received; a lost bone packet cannot make an incompatible skeleton appear to match.

With automatic Root Bone Name, each change of streamed root name resolves the skeleton again,
including after a character whose root was not found. Repeated frames with the same root reuse the
mapping. This excludes avatar containers and sibling
meshes (for example, Ch14 has 57 skeleton bones, not 59 transforms including its container and mesh).
An explicitly configured Root Bone Name remains authoritative.

Status uses `/MOVIN/Unity/Status/Request` (request token and reply port) and
`/MOVIN/Unity/Status` (version 2). Replies echo a one-use request token, so delayed replies from a
previous destination cannot restore stale status. This does not change motion or point cloud packets.

### Common issues

- No packets are shown in the monitor:
  Check the sender destination IP, UDP port `11235`, firewall rules, and whether another app is already using the same port.
- Packets arrive but the character does not move:
  Confirm that `MocapReceiver` is on the character root, that `Stream Target` matches the target MOVIN Studio streams to, that the streamed bone names match the Unity hierarchy, and that `Root Bone Name` is set only when needed. Check the Studio version: Studio `v3.2.0` and earlier require plugin `v3.0.0` because they send legacy `/VMC/` addresses.
- Only part of the character moves, such as the upper body:
  The mapped bones stop short of the rest of the skeleton. `MocapReceiver` logs a warning naming every streamed bone it could not match, so check the Console and then set `Root Bone Name` to the top of your skeleton, for example `RootBone` or `Hips`.
- Motion is delayed or frames are dropped:
  Check Unity performance, monitor `Pose Buffer` and `Dropped`, and tune `maxBufferedFramesBeforeDrop` if needed.
- There is no `.unitypackage` in the checkout:
  Download Core and optional Samples from GitHub Releases. Historical tags containing the old binary require Git LFS when cloning.

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
tools/                 # package generation and isolated Unity verification
docs/                  # release maintenance and internal diagnostics
release.json           # version and export paths
CHANGELOG.md
```

## Development

Release maintenance is documented in [docs/releasing.md](docs/releasing.md). The attached release
reports cover Core-only imports, fresh Core/Samples installs, v3.0.0 upgrades and initial v3.3.0
refreshes in the two Editor versions listed above. Standalone Player/IL2CPP and cross-machine
firewall configurations were not tested in this release run.

Stream Validation is an internal development tool, disabled by default even in the repository's
Unity Editor project. To use it, work from the complete source checkout and follow
[docs/internal-validation.md](docs/internal-validation.md). Its `MOVIN_STREAM_VALIDATION` symbol
must not be enabled in a user package installation, which does not include the required source.
Motion, point clouds and Studio status/FPS work without this diagnostic.

## License

Copyright 2026 MOVIN. All Rights Reserved.
