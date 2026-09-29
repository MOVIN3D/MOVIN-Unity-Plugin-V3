using System;
using UnityEngine;

namespace MOVIN
{
    /// <summary>
    /// Simple example: logs a few applied stream poses and shows how to subscribe.
    /// Add this component alongside MOVINStreamReceiver in your scene.
    /// </summary>
    public class MotionStreamExampleLogger : MonoBehaviour
    {
        public MOVINStreamReceiver receiver;

        private Action<string, Vector3, Quaternion, Vector3?> _rootPoseHandler;
        private Action<string, Vector3, Quaternion> _bonePoseHandler;

        private void Reset()
        {
            receiver = GetComponent<MOVINStreamReceiver>();
            if (!receiver)
                receiver = gameObject.AddComponent<MOVINStreamReceiver>();
        }

        private void OnEnable()
        {
            if (!receiver)
                receiver = GetComponent<MOVINStreamReceiver>();
            if (!receiver)
                return;

            _rootPoseHandler = (name, p, q, s) => Debug.Log($"Root {name} p={p} q={q} s={s}");
            _bonePoseHandler = (name, p, q) =>
            {
                if (name == "Hips" || name == "Head")
                    Debug.Log($"Bone {name} p={p} q={q}");
            };

            receiver.OnRootPose += _rootPoseHandler;
            receiver.OnBonePose += _bonePoseHandler;
        }

        private void OnDisable()
        {
            if (!receiver)
                return;

            receiver.OnRootPose -= _rootPoseHandler;
            receiver.OnBonePose -= _bonePoseHandler;
        }
    }
}
