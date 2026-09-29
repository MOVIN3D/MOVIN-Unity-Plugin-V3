using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class import_check{
    public static void core(){
        var receiver = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("MOVIN.MOVINStreamReceiver"))
            .Single(t => t != null);
        if (!typeof(MonoBehaviour).IsAssignableFrom(receiver)){
            throw new InvalidOperationException("Receiver is not a MonoBehaviour");
        }
        if (Directory.Exists("Assets/MOVIN/Tests") || Directory.Exists("Assets/MOVIN/Character")){
            throw new InvalidOperationException("Core includes tests or samples");
        }
        if (Resources.Load<UnityEngine.UIElements.ThemeStyleSheet>("MOVIN/MOVINDefaultRuntimeTheme") == null){
            throw new InvalidOperationException("Missing runtime theme");
        }
        File.WriteAllText("core-import-ok.json", "{\"unity\":\"" + Application.unityVersion + "\",\"passed\":true}");
    }

    public static void legacy_scene(){
        save_receiver_scene(new[]{"VMCReceiver", "MOVIN.Core.MocapReceiver", "VMCReceiverMonitorUI", "VMCExampleLogger"});
    }

    public static void previous_release_scene(){
        save_receiver_scene(new[]{"MOVIN.MOVINStreamReceiver", "MOVIN.MocapReceiver", "MOVIN.MotionStreamMonitorUI", "MOVIN.MotionStreamExampleLogger"});
    }

    static void save_receiver_scene(string[] types){
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach (var name in types){
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).Single(t => t != null);
            var obj = new GameObject(name);
            obj.SetActive(false);
            var component = obj.AddComponent(type);
            var serialized = new SerializedObject(component);
            var port = serialized.FindProperty("listenPort");
            if (port != null){
                port.intValue = 12345;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        if (!EditorSceneManager.SaveScene(scene, "Assets/legacy-user-scene.unity")){
            throw new IOException("Could not save the legacy user scene");
        }
    }
}
