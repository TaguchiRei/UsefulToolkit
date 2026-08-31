using UnityEditor;
using UnityEngine;
using UsefulToolkit.External.Scene;

namespace UsefulToolkit.Editor.SceneSupport
{
    /// <summary>
    /// <see cref="SceneGroupDataBase"/> 系アセットのInspector。
    /// 「アクティブシーンを含む」がオンのときだけメインシーンのEnumフィールドを表示し、
    /// オフのときは追加シーンの配列だけを表示する。
    /// Enumフィールドが変更されたら、そのシーン名を記録用フィールドへ書き出す。
    /// シーン参照が名前で解決できない場合は警告を表示する。
    /// </summary>
    [CustomEditor(typeof(SceneGroupDataBase), true)]
    public sealed class SceneGroupDataEditor : UnityEditor.Editor
    {
        private SerializedProperty _hasMainScene;
        private SerializedProperty _mainScene;
        private SerializedProperty _additionalScenes;
        private SerializedProperty _mainSceneName;
        private SerializedProperty _additionalSceneNames;

        private SceneGroupRebindResult _cachedResult;

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
            _mainSceneName = serializedObject.FindProperty("_mainSceneName");
            _additionalSceneNames = serializedObject.FindProperty("_additionalSceneNames");

            RefreshCachedResult();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawSceneReferenceWarnings();

            EditorGUI.BeginChangeCheck();

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

            bool changed = EditorGUI.EndChangeCheck();

            serializedObject.ApplyModifiedProperties();

            if (changed)
            {
                CaptureSceneNames();
                RefreshCachedResult();
            }
        }

        /// <summary>
        /// 表示中のEnumフィールドの選択メンバー名を、記録用のシーン名フィールドへ書き出す。
        /// </summary>
        private void CaptureSceneNames()
        {
            serializedObject.Update();

            if (_mainSceneName != null && _mainScene != null)
            {
                _mainSceneName.stringValue = EnumMemberName(_mainScene);
            }

            if (_additionalSceneNames != null && _additionalScenes != null)
            {
                _additionalSceneNames.arraySize = _additionalScenes.arraySize;

                for (int i = 0; i < _additionalScenes.arraySize; i++)
                {
                    _additionalSceneNames.GetArrayElementAtIndex(i).stringValue =
                        EnumMemberName(_additionalScenes.GetArrayElementAtIndex(i));
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSceneReferenceWarnings()
        {
            if (_cachedResult.HasRemovedScene)
            {
                EditorGUILayout.HelpBox(
                    SceneGroupReferenceMaintainer.RemovedSceneMessage, MessageType.Warning);
            }

            if (_cachedResult.HasIndexResolvedScene)
            {
                EditorGUILayout.HelpBox(
                    SceneGroupReferenceMaintainer.IndexResolvedMessage, MessageType.Warning);
            }
        }

        private void RefreshCachedResult()
        {
            _cachedResult = target is SceneGroupDataBase asset
                ? asset.InspectSceneReferences()
                : default;
        }

        /// <summary>
        /// Enumのシリアライズドプロパティから、選択中のメンバー名を返す。
        /// 有効なメンバーを指していない場合は空文字を返す。
        /// </summary>
        private static string EnumMemberName(SerializedProperty enumProperty)
        {
            int index = enumProperty.enumValueIndex;
            var names = enumProperty.enumNames;
            return index >= 0 && index < names.Length ? names[index] : string.Empty;
        }
    }
}
