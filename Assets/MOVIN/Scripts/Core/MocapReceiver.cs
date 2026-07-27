using System.Collections.Generic;
using UnityEngine;

namespace MOVIN.Core
{
    public class MocapReceiver : VMCReceiver
    {
        private const string BoneObjectSuffix = "BoneObject";
        private const float BoneScaleEpsilon = 1e-6f;

        [Header("Bone Search")]
        [SerializeField] string rootBoneName;

        [Header("Bone Visualization")]
        [Tooltip("Scale '<bone>BoneObject' helper meshes so drawn bones match the streamed bone lengths. Rigs without helper objects, such as a plain FBX with one skinned mesh, are left untouched.")]
        [SerializeField] bool scaleBoneObjects = true;

        private Dictionary<string, Transform> name2Transform;
        private HashSet<string> warnedMissingBones;
        private bool streamedRootResolved;
        private Dictionary<string, Vector3> bindLocalPositions;
        private HashSet<string> streamedBoneNames;
        private List<BoneObjectScale> boneObjectScales;
        private bool boneObjectScalesBuilt;

        private struct BoneObjectScale
        {
            public Transform BoneObject;
            public string XSourceBone;
            // Null marks a uniform scale driven by XSourceBone alone.
            public string YSourceBone;
        }

        protected override void OnEnable()
        {
            CaptureBindLocalPositions();
            Build(rootBoneName);
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            name2Transform = null;
            warnedMissingBones = null;
            streamedRootResolved = false;
            streamedBoneNames = null;
            boneObjectScales = null;
            boneObjectScalesBuilt = false;
            // bindLocalPositions is kept: it holds the authored rest pose, which cannot be
            // recaptured once streamed poses have overwritten the skeleton.
        }

        private void CaptureBindLocalPositions()
        {
            if (bindLocalPositions != null)
                return;

            bindLocalPositions = new Dictionary<string, Vector3>();

            void Capture(Transform trs)
            {
                bindLocalPositions[trs.name] = trs.localPosition;

                for (int i = 0; i < trs.childCount; ++i)
                    Capture(trs.GetChild(i));
            }

            Capture(transform);
        }

        private static Transform SearchArmature(Transform root, string armatureBoneName)
        {
            if (root.name == armatureBoneName)
                return root;

            for (int i = 0; i < root.childCount; ++i)
            {
                var armature = SearchArmature(root.GetChild(i), armatureBoneName);

                if (armature != null)
                    return armature;
            }

            return null;
        }

        /// <summary>
        /// Guesses the skeleton root by walking the single-child wrapper chain below
        /// <paramref name="root"/> and stopping at the node that first branches. The node just
        /// above the branch point is the skeleton root (for example 'RootBone' above 'Hips').
        /// Renderers are deliberately ignored: rigs that draw their bones as meshes carry a
        /// Renderer on every joint, so renderer placement says nothing about where bones start.
        /// </summary>
        private static Transform DetectSkeletonRoot(Transform root)
        {
            var node = root;
            while (node.childCount == 1)
                node = node.GetChild(0);

            // Never climb above the receiver, which would map the receiver's own siblings.
            return node != root && node.parent != null ? node.parent : node;
        }

        private bool Build(string armatureBoneName = "")
        {
            var armature = string.IsNullOrWhiteSpace(armatureBoneName)
                ? DetectSkeletonRoot(transform)
                : SearchArmature(transform, armatureBoneName);

            if (!armature)
            {
                name2Transform = new Dictionary<string, Transform>();
                Debug.LogWarning($"MocapReceiver could not find an armature under '{name}'. Check Root Bone Name or the character hierarchy.", this);
                return false;
            }

            return BuildFrom(armature);
        }

        private bool BuildFrom(Transform armature)
        {
            name2Transform = new Dictionary<string, Transform>();
            warnedMissingBones = null;
            // The mapped subtree changed, so any cached scale plan points at the wrong bones.
            boneObjectScales = null;
            boneObjectScalesBuilt = false;

            void Construct(Transform trs)
            {
                name2Transform[trs.name] = trs;

                for (int i = 0; i < trs.childCount; ++i)
                    Construct(trs.GetChild(i));
            }

            Construct(armature);
            if (name2Transform.Count <= 1)
            {
                Debug.LogWarning($"MocapReceiver found armature '{armature.name}' but mapped no child bones.", this);
                return false;
            }

            return true;
        }

        /// <summary>
        /// The sender names its own skeleton root in every root pose, which is more reliable than
        /// any local guess. When Root Bone Name is left blank and that root sits outside the
        /// mapped subtree, remap from it so no branch (typically the legs) is silently skipped.
        /// </summary>
        private void TryAdoptStreamedSkeletonRoot(string streamedRootName)
        {
            if (streamedRootResolved)
                return;

            // An explicit Root Bone Name is a deliberate choice, so leave it alone.
            if (!string.IsNullOrWhiteSpace(rootBoneName) || string.IsNullOrWhiteSpace(streamedRootName))
            {
                streamedRootResolved = true;
                return;
            }

            if (name2Transform != null && name2Transform.ContainsKey(streamedRootName))
            {
                streamedRootResolved = true;
                return;
            }

            var streamedRoot = SearchArmature(transform, streamedRootName);
            streamedRootResolved = true;

            if (!streamedRoot)
                return;

            var previousBoneCount = name2Transform?.Count ?? 0;
            if (BuildFrom(streamedRoot))
            {
                Debug.Log($"MocapReceiver remapped bones from streamed skeleton root '{streamedRootName}' ({previousBoneCount} -> {name2Transform.Count} bones).", this);
            }
        }

        private void WarnMissingBoneOnce(string boneName)
        {
            warnedMissingBones ??= new HashSet<string>();
            if (!warnedMissingBones.Add(boneName))
                return;

            Debug.LogWarning($"MocapReceiver received bone '{boneName}' but found no matching Transform under '{name}', so it is not animated. Check Root Bone Name or the character hierarchy.", this);
        }

        /// <summary>
        /// Records every bone name seen on the wire and reports whether the set grew. Helper objects
        /// are never streamed, so this set is exactly the skeleton the sender drives.
        /// </summary>
        private bool TrackStreamedBones(VMCFramePose frame)
        {
            streamedBoneNames ??= new HashSet<string>();

            var added = false;
            if (frame.HasRoot)
                added |= streamedBoneNames.Add(frame.RootName);

            foreach (var bone in frame.Bones)
                added |= streamedBoneNames.Add(bone.Name);

            return added;
        }

        /// <summary>
        /// Scales each '&lt;bone&gt;BoneObject' helper by how far the streamed bone length drifted from
        /// the authored rest pose, so a rig that draws its bones as meshes keeps the drawn segment
        /// touching the next joint. A '&lt;bone&gt;Bone' mesh spans the bone to its child, so the child's
        /// offset normally drives the scale. Rigs with no helper objects produce an empty plan and
        /// are skipped from then on.
        /// </summary>
        private void BuildBoneObjectScales()
        {
            boneObjectScalesBuilt = true;
            boneObjectScales = null;

            if (!scaleBoneObjects || name2Transform == null || streamedBoneNames == null)
                return;

            foreach (var boneName in streamedBoneNames)
            {
                if (!name2Transform.TryGetValue(boneName, out var joint) || !joint)
                    continue;

                var boneObject = joint.Find(boneName + BoneObjectSuffix);
                if (!boneObject)
                    continue;

                if (!TryGetScaleSources(boneName, joint, streamedBoneNames, out var entry))
                    continue;

                entry.BoneObject = boneObject;
                (boneObjectScales ??= new List<BoneObjectScale>()).Add(entry);
            }
        }

        private bool TryGetScaleSources(string boneName, Transform joint, HashSet<string> streamedBones, out BoneObjectScale entry)
        {
            entry = default;

            // Hips and Spine3 carry offsets that are not segment lengths, so they borrow axes from
            // neighbours whose offsets are axis aligned with the drawn mesh. Spine3 takes its height
            // from the shoulder rather than the neck because the sender preserves total neck length
            // while redistributing it between Neck and Neck1, which makes either neck ratio spike.
            if (boneName == "Hips" && TryGetAxisSources("LeftUpLeg", "Spine", streamedBones, out entry))
                return true;

            if (boneName == "Spine3" && TryGetAxisSources("LeftArm", "LeftShoulder", streamedBones, out entry))
                return true;

            string singleChild = null;
            var childCount = 0;
            for (int i = 0; i < joint.childCount; ++i)
            {
                var child = joint.GetChild(i);
                if (!streamedBones.Contains(child.name))
                    continue;

                childCount++;
                singleChild = child.name;
            }

            // A hub such as a palm has an offset that measures the parent segment instead of any of
            // its own, so scaling by it would stretch the mesh by an unrelated length.
            if (childCount >= 2)
                return false;

            entry.XSourceBone = childCount == 1 ? singleChild : boneName;
            return true;
        }

        private bool TryGetAxisSources(string xSourceBone, string ySourceBone, HashSet<string> streamedBones, out BoneObjectScale entry)
        {
            entry = default;
            if (!streamedBones.Contains(xSourceBone) || !streamedBones.Contains(ySourceBone))
                return false;

            entry.XSourceBone = xSourceBone;
            entry.YSourceBone = ySourceBone;
            return true;
        }

        private void ApplyBoneObjectScales()
        {
            if (boneObjectScales == null)
                return;

            foreach (var entry in boneObjectScales)
            {
                if (!entry.BoneObject)
                    continue;

                entry.BoneObject.localScale = entry.YSourceBone == null
                    ? Vector3.one * BoneRatio(entry.XSourceBone)
                    : new Vector3(BoneRatio(entry.XSourceBone), BoneRatio(entry.YSourceBone), 1f);
            }
        }

        /// <summary>
        /// Streamed local bone length over the authored rest length, falling back to 1 when the rest
        /// length is degenerate, as it is for a root whose offset is world translation.
        /// </summary>
        private float BoneRatio(string boneName)
        {
            if (bindLocalPositions == null
                || !bindLocalPositions.TryGetValue(boneName, out var bindPosition)
                || !name2Transform.TryGetValue(boneName, out var boneTransform)
                || !boneTransform)
            {
                return 1f;
            }

            var bindLength = bindPosition.magnitude;
            if (bindLength < BoneScaleEpsilon)
                return 1f;

            return boneTransform.localPosition.magnitude / bindLength;
        }

        protected override void ApplyFramePose(VMCFramePose frame)
        {
            if (frame.HasRoot)
            {
                TryAdoptStreamedSkeletonRoot(frame.RootName);
                ApplyPose(frame.RootName, frame.RootPosition, frame.RootRotation, frame.RootScale, true);
            }

            foreach (var bone in frame.Bones)
                ApplyPose(bone.Name, bone.Position, bone.Rotation, null, false);

            // A dropped packet can leave a frame short of bones, which would undercount a joint's
            // skeletal children, so the plan is rebuilt whenever a new bone name shows up.
            if (TrackStreamedBones(frame) || !boneObjectScalesBuilt)
                BuildBoneObjectScales();

            ApplyBoneObjectScales();

            base.ApplyFramePose(frame);
        }

        private void ApplyPose(string boneName, Vector3 localPos, Quaternion localOrientation, Vector3? localScale, bool includeScale)
        {
            if (name2Transform == null || !name2Transform.TryGetValue(boneName, out var boneTransform) || !boneTransform)
            {
                WarnMissingBoneOnce(boneName);
                return;
            }

            boneTransform.SetLocalPositionAndRotation(localPos, localOrientation);

            if (localScale.HasValue)
                boneTransform.localScale = localScale.Value;

            CapturePrivateAppliedPose(boneName, boneTransform, includeScale);
        }
    }
}
