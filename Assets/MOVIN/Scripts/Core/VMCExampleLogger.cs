using System;
using UnityEngine;

/// <summary>
/// Simple example: logs a few VMC messages and shows how to subscribe.
/// Add this component alongside VMCReceiver in your scene.
/// </summary>
public class VMCExampleLogger : MonoBehaviour
{
    public VMCReceiver receiver;

    private Action<int, int, int, int> _okHandler;
    private Action<float> _timeHandler;
    private Action<string, Vector3, Quaternion, Vector3?, Vector3?> _rootPoseHandler;
    private Action<string, Vector3, Quaternion> _bonePoseHandler;
    private Action<string, float> _blendShapeValueHandler;
    private Action _blendShapeApplyHandler;

    private void Reset()
    {
        receiver = GetComponent<VMCReceiver>();
        if (!receiver)
            receiver = gameObject.AddComponent<VMCReceiver>();
    }

    private void OnEnable()
    {
        if (!receiver)
            receiver = GetComponent<VMCReceiver>();
        if (!receiver)
            return;

        _okHandler = (loaded, cstate, cmode, track) =>
            Debug.Log($"/VMC/Ext/OK loaded={loaded} calibState={cstate} calibMode={cmode} tracking={track}");
        _timeHandler = t => Debug.Log($"/VMC/Ext/T time={t}");
        _rootPoseHandler = (name, p, q, s, o) => Debug.Log($"Root {name} p={p} q={q} s={s} o={o}");
        _bonePoseHandler = (name, p, q) =>
        {
            if (name == "Hips" || name == "Head")
                Debug.Log($"Bone {name} p={p} q={q}");
        };
        _blendShapeValueHandler = (name, value) =>
        {
            if (name == "A" || name == "Joy")
                Debug.Log($"Blend {name}={value}");
        };
        _blendShapeApplyHandler = () => Debug.Log("Blend Apply");

        receiver.OnOk += _okHandler;
        receiver.OnTime += _timeHandler;
        receiver.OnRootPose += _rootPoseHandler;
        receiver.OnBonePose += _bonePoseHandler;
        receiver.OnBlendShapeValue += _blendShapeValueHandler;
        receiver.OnBlendShapeApply += _blendShapeApplyHandler;
    }

    private void OnDisable()
    {
        if (!receiver)
            return;

        receiver.OnOk -= _okHandler;
        receiver.OnTime -= _timeHandler;
        receiver.OnRootPose -= _rootPoseHandler;
        receiver.OnBonePose -= _bonePoseHandler;
        receiver.OnBlendShapeValue -= _blendShapeValueHandler;
        receiver.OnBlendShapeApply -= _blendShapeApplyHandler;
    }
}