using System.Collections.Generic;
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

        /// <summary>
        /// InputActionAssetからActionMapとActionのenumを生成する。
        /// 要素名の重複が一つでもあれば、何も書き出さずにfalseを返す。
        /// </summary>
        /// <param name="asset">生成元のInputActionAsset</param>
        public static bool Generate(InputActionAsset asset)
        {
            if (asset == null) return false;

            var ns = UsefulToolkitSettingsScriptable.instance.CodeGenerationSectionSettings.Namespace;

            if (!TryBuildEnumValues(asset.actionMaps.Select(map => map.name), "ActionMap", out var mapNames))
                return false;

            var pending = new List<(string EnumName, string[] Values)> { ("ActionMaps", mapNames) };

            foreach (var map in asset.actionMaps)
            {
                if (!TryBuildEnumValues(map.actions.Select(action => action.name), $"ActionMap '{map.name}' のAction",
                        out var actionNames))
                    return false;

                pending.Add(($"{SanitizeName(map.name)}Actions", actionNames));
            }

            // 一部だけ書き出すとActionMapsと各Actions enumの整合が崩れる為、全ての検証を通してから書き出す
            foreach (var (enumName, values) in pending)
            {
                WriteEnum(enumName, values, ns);
            }

            Debug.Log($"[UsefulToolkit.Input] '{asset.name}' から入力用enumを生成しました: {FileGenerator.GenerateRuntimeRootPath}/{GenerateType.Runtime}/{FolderName}");
            return true;
        }

        /// <summary>
        /// 名前の一覧をenumの要素名へ変換する。
        /// 変換で名前が変わった場合は警告し、変換後に重複が生じた場合はfalseを返す。
        /// </summary>
        /// <param name="names">変換元の名前</param>
        /// <param name="label">ログに出す対象の説明</param>
        /// <param name="values">変換後の要素名</param>
        private static bool TryBuildEnumValues(IEnumerable<string> names, string label, out string[] values)
        {
            values = null;

            var sourceNames = names.ToArray();
            var sanitized = new string[sourceNames.Length];
            var sources = new Dictionary<string, List<string>>();

            for (int i = 0; i < sourceNames.Length; i++)
            {
                sanitized[i] = SanitizeName(sourceNames[i]);

                if (sanitized[i] != sourceNames[i])
                {
                    // 実行時のActionMap/Actionの検索はenumのToString()で行う為、名前が変わると一致しなくなる
                    Debug.LogWarning(
                        $"[UsefulToolkit.Input] {label} '{sourceNames[i]}' はenumの要素名に使えない文字を含む為、" +
                        $"'{sanitized[i]}' として生成します。実行時の検索は元の名前で行われる為このenumは一致しません。" +
                        "InputActionAsset側の名前を変更してください。");
                }

                if (!sources.TryGetValue(sanitized[i], out var sourceList))
                {
                    sourceList = new List<string>();
                    sources[sanitized[i]] = sourceList;
                }

                sourceList.Add(sourceNames[i]);
            }

            var collisions = sources.Where(pair => pair.Value.Count > 1).ToArray();
            if (collisions.Length == 0)
            {
                values = sanitized;
                return true;
            }

            foreach (var collision in collisions)
            {
                Debug.LogError(
                    $"[UsefulToolkit.Input] {label} の {string.Join(" と ", collision.Value.Select(name => $"'{name}'"))} は" +
                    $"どちらもenum要素名 '{collision.Key}' になる為、enumを生成できません。" +
                    "どちらかの名前を変更してから再生成してください。");
            }

            return false;
        }

        /// <summary>
        /// enumのソースを組み立ててファイルへ書き出す。
        /// </summary>
        /// <param name="enumName">生成するenumの型名</param>
        /// <param name="values">enumの要素名</param>
        /// <param name="ns">生成先の名前空間</param>
        private static void WriteEnum(string enumName, string[] values, string ns)
        {
            var source = EnumGenerator.BuildSource(enumName, values, ns);
            FileGenerator.AutoGenerateFile($"{enumName}.cs", source, GenerateType.Runtime, FolderName);
        }

        /// <summary>
        /// 名前からenumの要素名に使えない文字を取り除く。
        /// </summary>
        /// <param name="name">変換元の名前</param>
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
