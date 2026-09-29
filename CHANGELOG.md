# Changelog

## [3.3.0] - 2026-09-29

Requires MOVIN Studio 3.3.0 or later. Studio 3.0.0 through 3.2.0 use plugin 3.0.0.

### Added

- Complete point cloud reception through `OnPointCloud`, sharing the motion UDP port.
- Studio status replies with character identity, complete bone-name signatures and received FPS.
- Separate Core and Samples packages, source/asset SHA-256 manifests, automated package checks,
  and reproducible local Unity import, upgrade and regression verification.

### Fixed

- Bounded motion buffering, restarted-frame recovery, malformed OSC validation and socket lifecycle.
- Skeleton-root changes, including Ch14 container/mesh exclusion and unknown previous roots.
- Duplicate bone-name reporting and MOVINMan drawing-helper exclusion from model comparisons.

### Breaking changes

- Motion addresses are now `/MOVIN/Unity/Root` and `/MOVIN/Unity/Bone`. Legacy `/VMC` addresses
  are not accepted. This is MOVIN's protocol, not VMC.
- Runtime types use the `MOVIN` namespace; OSC parsing uses `MOVIN.OSC`.
- `VMCReceiver` is now `MOVIN.MOVINStreamReceiver`; monitor/logger types also have new names.
  See README for the complete migration table. Existing script GUIDs are preserved.
- Removed VMC-only events and unused options. Custom C# integrations must update their type/event
  references; asset import cannot rewrite user scripts.

### Distribution

- Core contains runtime scripts and the monitor theme; no sample models or NUnit tests.
- Samples requires Core and URP. It contains all three sample characters/scenes and their assets.
- Generated packages live in GitHub Releases. The previous checked-in package remains available
  in historical tags, but is no longer maintained in the source tree.

## [3.0.0] - 2026-09-23

- Original release for MOVIN Studio 3.0.0 through 3.2.0 using legacy stream addresses.

[3.3.0]: https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3/releases/tag/v3.3.0
[3.0.0]: https://github.com/MOVIN3D/MOVIN-Unity-Plugin-V3/releases/tag/v3.0.0
