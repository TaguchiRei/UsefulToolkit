using UnityEditor;
using UnityEngine;
using UsefulToolkit.Framework;

namespace UsefulToolkit.WorkTrack
{
    /// <summary>
    /// UsefulToolkitSettingsウィンドウに追加されるWorkTrackの設定タブ。
    /// </summary>
    public class WorkTrackSettingPage : SettingPageBase
    {
        public override string Name => "WorkTrack";

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("WorkTrack 保存先設定", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("現在の保存先", EditorStyles.miniBoldLabel);
                EditorGUILayout.SelectableLabel(WorkTrackPaths.SaveDirectory, EditorStyles.textField,
                    GUILayout.Height(18));

                if (WorkTrackPaths.IsDefault)
                {
                    EditorGUILayout.LabelField("(デフォルト)", EditorStyles.miniLabel);
                }

                EditorGUILayout.Space();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("フォルダを選択"))
                    {
                        var selected = EditorUtility.OpenFolderPanel("WorkTrack 保存先を選択",
                            WorkTrackPaths.SaveDirectory, "");
                        if (!string.IsNullOrEmpty(selected))
                        {
                            WorkTrackPaths.SaveDirectory = selected;
                        }
                    }

                    if (GUILayout.Button("デフォルトに戻す"))
                    {
                        WorkTrackPaths.ResetToDefault();
                    }

                    if (GUILayout.Button("フォルダを開く"))
                    {
                        WorkTrackPaths.EnsureDirectories();
                        EditorUtility.RevealInFinder(WorkTrackPaths.SaveDirectory);
                    }
                }

                EditorGUILayout.HelpBox(
                    "この設定はEditorPrefsに保存されるため、Unityプロジェクト単位ではなく利用者(PC)ごとに個別に適用されます。",
                    MessageType.Info);
            }
        }
    }
}
