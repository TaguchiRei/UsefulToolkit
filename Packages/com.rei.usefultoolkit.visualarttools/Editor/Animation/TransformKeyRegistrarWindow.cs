using UnityEngine;
using UnityEditor;

namespace UsefulToolkit.Editor
{
    public class TransformKeyRegistrarWindow : EditorWindow
    {
        private AnimationClip targetClip;
        private GameObject targetObject;
        private float keyTime = 0f;

        [MenuItem("Tools/Animation/Transform Key Registrar")]
        private static void Open()
        {
            GetWindow<TransformKeyRegistrarWindow>("Transform Key Registrar");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            targetObject = (GameObject)EditorGUILayout.ObjectField("対象GameObject", targetObject, typeof(GameObject), true);
            targetClip = (AnimationClip)EditorGUILayout.ObjectField("AnimationClip", targetClip, typeof(AnimationClip), false);
            keyTime = EditorGUILayout.FloatField("キー時間", keyTime);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(targetObject == null || targetClip == null))
            {
                if (GUILayout.Button("全Transformにキーを登録", GUILayout.Height(30)))
                {
                    RegisterKeys();
                }
            }
        }

        private void RegisterKeys()
        {
            if (targetObject == null || targetClip == null) return;

            keyTime = Mathf.Clamp(keyTime, 0f, targetClip.length);
            Transform[] transforms = targetObject.GetComponentsInChildren<Transform>( true);
            Undo.RegisterCompleteObjectUndo(targetClip, "Register Transform Animation Keys");

            foreach (Transform transform in transforms)
            {
                RegisterPosition(transform);
                RegisterRotation(transform);
                RegisterScale(transform);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Transformキーを{transforms.Length}個のTransformへ登録しました。");
        }

        private void RegisterPosition(Transform transform)
        {
            string path = AnimationUtility.CalculateTransformPath(transform, targetObject.transform);

            AddKey(path, "m_LocalPosition.x", transform.localPosition.x);
            AddKey(path, "m_LocalPosition.y", transform.localPosition.y);
            AddKey(path, "m_LocalPosition.z", transform.localPosition.z);
        }

        private void RegisterRotation(Transform transform)
        {
            string path = AnimationUtility.CalculateTransformPath(transform, targetObject.transform);
            Vector3 rotation = transform.localEulerAngles;

            AddKey(path, "localEulerAnglesRaw.x", rotation.x);
            AddKey(path, "localEulerAnglesRaw.y", rotation.y);
            AddKey(path, "localEulerAnglesRaw.z",rotation.z);
        }

        private void RegisterScale(Transform transform)
        {
            string path = AnimationUtility.CalculateTransformPath(transform, targetObject.transform);

            AddKey(path, "m_LocalScale.x", transform.localScale.x);
            AddKey(path, "m_LocalScale.y", transform.localScale.y);
            AddKey(path, "m_LocalScale.z", transform.localScale.z);
        }

        private void AddKey(string path, string propertyName, float value)
        {
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName);
            AnimationCurve curve = AnimationUtility.GetEditorCurve( targetClip, binding);

            if (curve == null)
            {
                curve = new AnimationCurve();
            }

            Keyframe key = new Keyframe(keyTime, value);
            curve.AddKey(key);
            AnimationUtility.SetEditorCurve(targetClip, binding, curve);
        }
    }
}