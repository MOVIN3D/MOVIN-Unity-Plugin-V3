# Releasing the Unity plugin

`release` is the stable/default branch. Work goes through a feature PR and the required
`Package validation` check. Released versions are immutable by policy: never retarget a published
tag or replace its files. Fixes get a new patch version. Keep older releases available.

`release.json` owns the version and export paths. Update it, README, Assets/Readme.asset and
CHANGELOG together. A plugin patch (for example 3.3.1) does not require Studio 3.3.1; record the
supported Studio range separately. List verified Unity versions rather than promising every
intermediate or future editor version works.

## Automated package generation

Python 3.10+ is sufficient; no third-party Python libraries or Unity license is needed to generate
archives. `tools/package.py` writes Unity's GUID/pathname/asset/asset.meta archive entries directly,
preserving source bytes and GUIDs. Fixed gzip/tar metadata makes repeated exports reproducible.
Unity import and execution are verified separately with a licensed local Editor.

```powershell
python -m unittest discover -s tools -p 'test_*.py' -v
python tools/package.py check
python tools/package.py build --output dist
python tools/package.py verify --output dist
```

Core includes scripts and UI theme resources. Samples includes characters, scenes and the scene
volume profile. Test scripts, project settings and development tooling are never exported. Sample
scenes still require URP; importing Samples does not replace a user's render-pipeline settings.

The generated manifest records commit SHA, dirty state, source asset hashes, GUIDs and package
hashes. Only artifacts with `dirty: false` from the intended release commit may be published.
Do not check `.unitypackage` files back into Git. The historical LFS files need no history rewrite.

## Unity verification

Run `tools/verify-unity.ps1` in a fresh workspace for each editor and installation mode. It creates
isolated projects and never launches against an already open user project. Logs and NUnit XML
remain in that workspace. `core` checks installation without URP or the test framework; `fresh`
imports Core and Samples then runs the receiver and asset-reference tests; `upgrade` imports the
3.0.0 package first, creates a scene using the old components, then imports the new packages and
checks that the saved scene and script GUIDs survive.

```powershell
./tools/verify-unity.ps1 -unity 'C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor/Unity.exe' -workspace 'D:/Verification/movin-3.3.0-core' -artifacts ./dist -mode core -legacy ''
./tools/verify-unity.ps1 -unity 'C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor/Unity.exe' -workspace 'D:/Verification/movin-3.3.0-fresh' -artifacts ./dist -mode fresh -legacy ''
./tools/verify-unity.ps1 -unity 'C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor/Unity.exe' -workspace 'D:/Verification/movin-3.3.0-upgrade' -artifacts ./dist -mode upgrade -legacy 'D:/Downloads/MOVIN-Unity-Plugin-V3-v3.0.0.unitypackage'
```

Repeat for the other editor listed in release.json. Also smoke-test motion, point clouds and
Studio status together. Preserve the test reports as release assets. Core/sample import tests do
not establish support for every render pipeline, platform, firewall or custom user script.

## GitHub workflow and publication

The Distribution workflow builds and verifies packages for PRs, release-branch pushes and version
tags, and stores the files as workflow artifacts. It intentionally has read-only repository access
and never publishes a release. Its green check means **package validation**, not a Unity test run.
Unity test execution currently happens locally; adding hosted Unity tests requires separately
configured Unity licensing or an appropriate runner. No credentials are committed to the repo.

1. Commit the release changes, open a PR to `release`, and pass Package validation.
2. Download the CI artifacts and verify their contents; perform Unity verification on the files
   that will actually ship. Merge the PR after all required checks pass.
3. Build/download packages for that exact merge commit and verify the clean commit in the
   manifest. If content changed during merge, rerun the affected Unity verification.
4. Create `vX.Y.Z` on that commit. Create a draft GitHub Release containing Core, Samples,
   release-manifest.json, SHA256SUMS.txt and verification reports. State the Studio requirement
   and link the upgrade instructions. Never upload raw logs that contain machine credentials.
5. Review the draft's files and publish it with the agreed Studio rollout. For an explicitly
   approved independent plugin rollout, state the required Studio version prominently.

The release branch requires PRs and Package validation. Existing v3.0.0 remains the download for
Studio 3.0.0–3.2.0. UPM distribution is deferred; the repository URL is not a Package Manager URL.
