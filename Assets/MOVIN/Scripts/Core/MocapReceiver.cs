using System.Collections.Generic;
using UnityEngine;

namespace MOVIN.Core
{
    public class MocapReceiver : VMCReceiver
    {
        [Header("Bone Search")]
        [SerializeField] string rootBoneName;

        private Dictionary<string, Transform> name2Transform;

        protected override void OnEnable()
        {
            Build(rootBoneName);
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            name2Transform = null;
        }

        private Transform SearchArmature(Transform root, string armatureBoneName = "")
        {
            bool IsArmature(Transform trs)
            {
                if (!string.IsNullOrWhiteSpace(armatureBoneName))
                    return trs.name == armatureBoneName;

                if (trs.GetComponent<Renderer>())
                    return false;

                if (trs.parent == null)
                    return false;

                var parent = trs.parent;

                for (int i = 0; i < parent.childCount; ++i)
                {
                    var child = parent.GetChild(i);
                    var renderer = child.GetComponent<Renderer>();
                    if (renderer != null)
                        return true;
                }

                return false;
            }

            if (IsArmature(root))
                return root;

            for (int i = 0; i < root.childCount; ++i)
            {
                var each = root.GetChild(i);
                var armature = SearchArmature(each, armatureBoneName);

                if (armature != null)
                    return armature;
            }

            return null;
        }

        private bool Build(string armatureBoneName = "")
        {
            name2Transform = new Dictionary<string, Transform>();
            var armature = SearchArmature(transform, armatureBoneName);

            if (!armature)
            {
                Debug.LogWarning($"MocapReceiver could not find an armature under '{name}'. Check Root Bone Name or the character hierarchy.", this);
                return false;
            }

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

        protected override void ApplyFramePose(VMCFramePose frame)
        {
            if (frame.HasRoot)
                ApplyPose(frame.RootName, frame.RootPosition, frame.RootRotation, frame.RootScale, true);

            foreach (var bone in frame.Bones)
                ApplyPose(bone.Name, bone.Position, bone.Rotation, null, false);

            base.ApplyFramePose(frame);
        }

        private void ApplyPose(string boneName, Vector3 localPos, Quaternion localOrientation, Vector3? localScale, bool includeScale)
        {
            if (name2Transform == null || !name2Transform.TryGetValue(boneName, out var boneTransform) || !boneTransform)
                return;

            boneTransform.SetLocalPositionAndRotation(localPos, localOrientation);

            if (localScale.HasValue)
                boneTransform.localScale = localScale.Value;

            CapturePrivateAppliedPose(boneName, boneTransform, includeScale);
        }
    }
}
