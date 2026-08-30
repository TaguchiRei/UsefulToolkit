using UnityEditor;
using UnityEngine;
using UsefulToolkit.External.Scene;

namespace UsefulToolkit.Editor.SceneSupport
{
    /// <summary>
    /// <see cref="SceneGroupDataBase"/> 系アセットのInspector。
    /// 「アクティブシーンを含む」がオンのときだけメインシーンのEnumフィールドを表示し、
    /// オフのときは追加シーンの配列だけを表示する。
    /// </summary>
    [CustomEditor(typeof(SceneGroupDataBase), true)]
    public sealed class SceneGroupDataEditor : UnityEditor.Editor
    {
        private SerializedProperty _hasMainScene;
        private SerializedProperty _mainScene;
        private SerializedProperty _additionalScenes;

        private static readonly GUIContent HasMainSceneLabel = new(
            "アクティブシーンを含む",
            "オンのとき、メインシーンをアクティブシーンとしてロードする。" +
            "オフのときは全て追加シーンで、ロードしてもアクティブシーンは変わらない。");

        private static readonly GUIContent MainSceneLabel = new("メインシーン");
        private static readonly GUIContent AdditionalScenesLabel = new("追加シーン");

        private void OnEnable()
        {
            _hasMainScene = serializedObject.FindProperty("_hasMainScene");
            _mainScene = serializedObject.FindProperty("_mainScene");
            _additionalScenes = serializedObject.FindProperty("_additionalScenes");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_hasMainScene != null)
            {
                EditorGUILayout.PropertyField(_hasMainScene, HasMainSceneLabel);

                if (_hasMainScene.boolValue && _mainScene != null)
                {
                    EditorGUILayout.PropertyField(_mainScene, MainSceneLabel);
                }
            }

            if (_additionalScenes != null)
            {
                EditorGUILayout.PropertyField(_additionalScenes, AdditionalScenesLabel, true);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
