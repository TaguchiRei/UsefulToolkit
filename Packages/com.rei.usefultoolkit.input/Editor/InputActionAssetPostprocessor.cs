using UnityEditor;
using UnityEngine.InputSystem;
using UsefulToolkit.Framework;

namespace UsefulToolkit.Input.Editor
{
    /// <summary>
    /// .inputactionsファイルの変更を検知し、enumの自動生成をトリガーする。
    /// UsefulToolkit/Settingsのコード生成タイミング設定がNoneの場合は何もしない。
    /// </summary>
    internal sealed class InputActionAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (UsefulToolkitSettingsScriptable.instance.CodeGenerationSectionSettings.Timing == GenerateTiming.None)
                return;

            foreach (var path in importedAssets)
            {
                if (!path.EndsWith(".inputactions")) continue;

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                if (asset != null)
                {
                    InputActionEnumGenerator.Generate(asset);
                }
            }
        }
    }
}
