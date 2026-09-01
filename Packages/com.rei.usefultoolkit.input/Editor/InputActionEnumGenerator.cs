using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine;
using UsefulToolkit.Editor.ProjectSettings;
using UsefulToolkit.Editor.Utility;

namespace UsefulToolkit.Editor.Input
{
    /// <summary>
    /// Project-wide ActionsのInputActionAssetから、ActionMapとActionのenumを自動生成する。
    /// 出力先フォルダ・名前空間は UsefulToolkit/Settings のコード生成設定
    /// (UsefulToolkitSettingsScriptable.CodeGenerationSectionSettings)に従う——
    /// このパッケージ自体は生成したenum型に一切依存しない
    /// (InputState/InputDispatcherはSystem.Enumで受け取る)。
    /// </summary>
    public static class InputActionEnumGenerator
    {
        private const string FolderName = "Input";

        [MenuItem("UsefulToolkit/Input/Generate Action Enums")]
        public static void GenerateFromProjectWideActions()
        {
            var asset = InputSystem.actions;

            if (asset == null)
            {
                Debug.LogError(
                    "[UsefulToolkit.Input] Project-wide Actions が設定されていません。\nProject Settings > Input System Package > Project-wide Actions を設定してください。");
                return;
            }

            Generate(asset);
        }

        public static bool Generate(InputActionAsset asset)
        {
            if (asset == null) return false;

            var ns = UsefulToolkitSettingsScriptable.instance.CodeGenerationSectionSettings.Namespace;

            var mapNames = asset.actionMaps.Select(m => SanitizeName(m.name)).ToArray();
            WriteEnum("ActionMaps", mapNames, ns);

            foreach (var map in asset.actionMaps)
            {
                var enumName = $"{SanitizeName(map.name)}Actions";
                var actionNames = map.actions.Select(a => SanitizeName(a.name)).ToArray();
                WriteEnum(enumName, actionNames, ns);
            }

            Debug.Log($"[UsefulToolkit.Input] '{asset.name}' から入力用enumを生成しました: {FileGenerator.GenerateRuntimeRootPath}/{GenerateType.Runtime}/{FolderName}");
            return true;
        }

        private static void WriteEnum(string enumName, string[] values, string ns)
        {
            var source = EnumGenerator.BuildSource(enumName, values, ns);
            FileGenerator.AutoGenerateFile($"{enumName}.cs", source, GenerateType.Runtime, FolderName);
        }

        private static string SanitizeName(string name)
        {
            var sanitized = Regex.Replace(name, @"[^a-zA-Z0-9_]", "");

            if (string.IsNullOrEmpty(sanitized))
                return "_";

            if (char.IsDigit(sanitized[0]))
                return "_" + sanitized;

            return sanitized;
        }
    }
}
