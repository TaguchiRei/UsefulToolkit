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

        private static readonly GUIContent AcknowledgeButtonLabel = new(
            "この参照で問題ない（警告を消す）",
            "現在のインデックスが指すシーンで正しい場合に押す。" +
            "シーン名を今の値へ記録し直すだけで、グループの参照先(インデックス)は変更しない。");

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
                string name = EnumMemberName(_mainScene);
                if (name.Length > 0)
                {
                    _mainSceneName.stringValue = name;
                }
            }

            if (_additionalSceneNames != null && _additionalScenes != null)
            {
                _additionalSceneNames.arraySize = _additionalScenes.arraySize;

                for (int i = 0; i < _additionalScenes.arraySize; i++)
                {
                    string name = EnumMemberName(_additionalScenes.GetArrayElementAtIndex(i));
                    if (name.Length > 0)
                    {
                        _additionalSceneNames.GetArrayElementAtIndex(i).stringValue = name;
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSceneReferenceWarnings()
        {
            if (_cachedResult.HasRemovedScene)
            {
                EditorGUILayout.HelpBox(
                    SceneGroupReferenceMaintainer.RemovedSceneMessage +
                    "\nシーン名でもインデックスでも解決できないため、インスペクターでシーンを選び直してください。",
                    MessageType.Warning);
            }

            if (_cachedResult.HasIndexResolvedScene)
            {
                EditorGUILayout.HelpBox(
                    SceneGroupReferenceMaintainer.IndexResolvedMessage, MessageType.Warning);

                if (GUILayout.Button(AcknowledgeButtonLabel))
                {
                    CaptureSceneNames();
                    RefreshCachedResult();
                }
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
