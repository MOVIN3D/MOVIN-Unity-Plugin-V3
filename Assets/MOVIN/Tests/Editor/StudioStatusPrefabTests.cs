using System;
using System.Linq;
using System.Reflection;
using MOVIN.OSC;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MOVIN.Tests
{
    public class StudioStatusPrefabTests
    {
        [TestCase(null)]
        [TestCase("RootBone")]
        [TestCase("mixamorig:Spine")]
        public void Ch14StatusUsesTheStreamedSkeletonRootWithoutContainerOrMesh(string previousRoot)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var parent = new GameObject("Ch14 status regression test");
            parent.SetActive(false);
            try
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MOVIN/Character/Ch14_mixamo/Ch14_nonPBR.fbx");
                Assert.That(model, Is.Not.Null);
                var instance = UnityEngine.Object.Instantiate(model, parent.transform);
                var receiver = instance.AddComponent<MocapReceiver>();
                var root = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "mixamorig:Hips");
                var spine = root.Find("mixamorig:Spine");
                var mesh = instance.GetComponentInChildren<SkinnedMeshRenderer>(true).transform;
                var containerPosition = instance.transform.localPosition;
                var meshPosition = mesh.localPosition;
                var expected = root.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
                Assert.That(expected.Length, Is.EqualTo(57));
                Assert.That(typeof(MocapReceiver).GetMethod("Build", flags).Invoke(receiver, new object[] { "" }), Is.True);
                var identityMethod = typeof(MocapReceiver).GetMethod("GetStatusCharacter", flags);
                var initial = (ValueTuple<string, string[]>)identityMethod.Invoke(receiver, null);
                Assert.That(initial.Item2, Does.Contain(instance.name).And.Contain(mesh.name));

                var buffer = typeof(MOVINStreamReceiver).GetMethod("TryBufferMotionMessage", flags);
                if (previousRoot != null)
                {
                    buffer.Invoke(receiver, new object[] { new OSCMessage {
                        Address = "/MOVIN/Unity/Root",
                        Args = new object[] { 9, previousRoot, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f }
                    } });
                    typeof(MOVINStreamReceiver).GetField("_currentBufferedFrameTicks", flags).SetValue(receiver, DateTime.UtcNow.AddSeconds(-1).Ticks);
                    if (previousRoot == "RootBone")
                    {
                        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("MocapReceiver received bone 'RootBone' but found no matching Transform"));
                    }
                    typeof(MOVINStreamReceiver).GetMethod("ApplyBufferedFrameIfAvailable", flags).Invoke(receiver, null);
                }
                buffer.Invoke(receiver, new object[] { new OSCMessage {
                    Address = "/MOVIN/Unity/Root",
                    Args = new object[] { 10, root.name, 1f, 2f, 3f, 0f, 0f, 0f, 1f, 1f, 1f, 1f }
                } });
                buffer.Invoke(receiver, new object[] { new OSCMessage {
                    Address = "/MOVIN/Unity/Bone",
                    Args = new object[] { 10, spine.name, 0f, .2f, 0f, 0f, 0f, 0f, 1f }
                } });
                typeof(MOVINStreamReceiver).GetField("_currentBufferedFrameTicks", flags).SetValue(receiver, DateTime.UtcNow.AddSeconds(-1).Ticks);
                typeof(MOVINStreamReceiver).GetMethod("ApplyBufferedFrameIfAvailable", flags).Invoke(receiver, null);

                var identity = (ValueTuple<string, string[]>)identityMethod.Invoke(receiver, null);
                Assert.That(identity.Item1, Is.EqualTo("Ch14_nonPBR"));
                Assert.That(identity.Item2.Length, Is.EqualTo(57));
                Assert.That(identity.Item2, Is.EquivalentTo(expected));
                Assert.That(identity.Item2, Does.Not.Contain(instance.name).And.Not.Contain(mesh.name));
                Assert.That(root.localPosition, Is.EqualTo(new Vector3(1, 2, 3)));
                Assert.That(spine.localPosition, Is.EqualTo(new Vector3(0, .2f, 0)));
                Assert.That(instance.transform.localPosition, Is.EqualTo(containerPosition));
                Assert.That(mesh.localPosition, Is.EqualTo(meshPosition));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test] public void MOVINManReportsItsCharacterNameAnd55BonesWithoutDrawingHelpers()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var parent = new GameObject("Character status prefab test");
            parent.SetActive(false);
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MOVIN/Character/MOVINman/MOVINman_V3.prefab");
                Assert.That(prefab, Is.Not.Null);
                var instance = UnityEngine.Object.Instantiate(prefab, parent.transform);
                var receiver = instance.GetComponent<MocapReceiver>();
                Assert.That(typeof(MocapReceiver).GetMethod("Build", flags).Invoke(receiver, new object[] { "" }), Is.True);
                var identity = (ValueTuple<string, string[]>)typeof(MocapReceiver).GetMethod("GetStatusCharacter", flags).Invoke(receiver, null);
                Assert.That(identity.Item1, Is.EqualTo("MOVINMan"));
                Assert.That(identity.Item2.Length, Is.EqualTo(55));
                Assert.That(identity.Item2, Does.Contain("RootBone").And.Contain("Hips").And.Contain("LeftHandThumb1"));
                Assert.That(identity.Item2, Does.Not.Contain("HipsBoneObject").And.Not.Contain("HipsBone").And.Not.Contain("HipsJoint"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }
    }
}
