using System.IO;
using System.Linq;
using MOVIN;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class package_import_tests{
    [Test]
    public void sample_scenes_have_no_missing_scripts(){
        foreach (var path in Directory.GetFiles("Assets/MOVIN/Scenes", "*.unity")){
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var objects = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject);
            foreach (var obj in objects){
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(obj), Is.Zero, path + ": " + obj.name);
            }
            Assert.That(objects.SelectMany(o => o.GetComponents<MocapReceiver>()).Any(), Is.True, path);
        }
    }

    [Test]
    public void scripts_and_theme_keep_original_guids(){
        Assert.That(AssetDatabase.AssetPathToGUID("Assets/MOVIN/Scripts/Core/MOVINStreamReceiver.cs"), Is.EqualTo("620c4cfe2e2cc364abd7b5b4c7603174"));
        Assert.That(AssetDatabase.AssetPathToGUID("Assets/MOVIN/Scripts/Core/MocapReceiver.cs"), Is.EqualTo("5390cdb2ad3f2a843b33049872e80a29"));
        Assert.That(Resources.Load<UnityEngine.UIElements.ThemeStyleSheet>("MOVIN/MOVINDefaultRuntimeTheme"), Is.Not.Null);
    }

    [Test]
    public void upgrade_preserves_saved_user_components_and_fields(){
        if (File.Exists("Assets/legacy-user-scene.unity")){
            var scene = EditorSceneManager.OpenScene("Assets/legacy-user-scene.unity", OpenSceneMode.Single);
            var objects = scene.GetRootGameObjects();
            foreach (var obj in objects){
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(obj), Is.Zero, obj.name);
            }
            Assert.That(objects.SelectMany(o => o.GetComponents<MOVINStreamReceiver>()).Count(), Is.EqualTo(2));
            Assert.That(objects.SelectMany(o => o.GetComponents<MocapReceiver>()).Count(), Is.EqualTo(1));
            Assert.That(objects.SelectMany(o => o.GetComponents<MotionStreamMonitorUI>()).Count(), Is.EqualTo(1));
            Assert.That(objects.SelectMany(o => o.GetComponents<MotionStreamExampleLogger>()).Count(), Is.EqualTo(1));
            foreach (var receiver in objects.SelectMany(o => o.GetComponents<MOVINStreamReceiver>())){
                Assert.That(receiver.listenPort, Is.EqualTo(12345));
            }
            Assert.That(AssetDatabase.FindAssets("VMCReceiver t:MonoScript"), Is.Empty);
        }
        else{
            Assert.Ignore("Fresh install; no legacy scene exists.");
        }
    }
}
