using UnityEditor;
using UnityEngine.InputSystem;
using UsefulToolkit.Editor.ProjectSettings;

namespace UsefulToolkit.Editor.Input
{
    /// <summary>
    /// Project-wide Actionsに設定された.inputactionsの変更を検知し、enumの自動生成をトリガーする。
    /// UsefulToolkit/Settingsのコード生成タイミング設定がNoneの場合は何もしない。
    ///
    /// 生成先のファイル名はenum名だけで決まる為、Project-wide Actions以外の.inputactionsを取り込むと
    /// 同じファイルを別アセットの内容で上書きしてしまう。その為、対象はProject-wide Actions一つに限る。
    /// </summary>
    internal sealed class InputActionAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (UsefulToolkitSettingsScriptable.instance.CodeGenerationSectionSettings.Timing == GenerateTiming.None)
                return;

            var projectWideActions = InputSystem.actions;
            if (projectWideActions == null) return;

            var projectWidePath = AssetDatabase.GetAssetPath(projectWideActions);
            if (string.IsNullOrEmpty(projectWidePath)) return;

            foreach (var path in importedAssets)
            {
                if (path != projectWidePath) continue;

                InputActionEnumGenerator.Generate(projectWideActions);
                return;
            }
        }
    }
}
