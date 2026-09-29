using System.IO;
using System.Linq;
using MOVIN;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class package_import_tests{
#if !MOVIN_STREAM_VALIDATION
    [Test]
    public void customer_core_has_no_validation_features(){
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        Assert.That(File.Exists("Assets/MOVIN/Scripts/Core/MOVINStreamReceiver.Validation.cs"), Is.False);
        foreach (var name in new[]{"validationLogging", "validationLogDirectory", "validationSessionId", "_validationWriter", "_queue"}){
            Assert.That(typeof(MOVINStreamReceiver).GetField(name, flags), Is.Null, name);
        }
        Assert.That(typeof(MOVINStreamReceiver).GetMethod("GetPrivateDiagnosticsSnapshot", flags), Is.Null);
        foreach (var name in new[]{"_validation", "_session", "_logPath", "_queue"}){
            Assert.That(typeof(MotionStreamMonitorUI).GetField(name, flags), Is.Null, name);
        }
    }
#endif
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
            Assert.That(objects.SelectMany(o => o.GetComponents<MOVINStreamReceiver>()).Count(), Is.EqualTo(3));
            Assert.That(objects.SelectMany(o => o.GetComponents<MocapReceiver>()).Count(), Is.EqualTo(1));
            Assert.That(objects.SelectMany(o => o.GetComponents<MotionStreamMonitorUI>()).Count(), Is.EqualTo(1));
            Assert.That(objects.SelectMany(o => o.GetComponents<MotionStreamExampleLogger>()).Count(), Is.EqualTo(1));
            foreach (var receiver in objects.SelectMany(o => o.GetComponents<MOVINStreamReceiver>())){
                var expected_port = receiver.GetComponent<MotionStreamExampleLogger>() != null ? 11235 : 12345;
                Assert.That(receiver.listenPort, Is.EqualTo(expected_port));
            }
            var logger = objects.SelectMany(o => o.GetComponents<MotionStreamExampleLogger>()).Single();
            Assert.That(logger.receiver, Is.SameAs(logger.GetComponent<MOVINStreamReceiver>()));
            Assert.That(AssetDatabase.FindAssets("VMCReceiver t:MonoScript"), Is.Empty);
        }
        else{
            Assert.Ignore("Fresh install; no legacy scene exists.");
        }
    }
}
