using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Editor.ProjectSettings;
using UsefulToolkit.Editor.Utility;

namespace UsefulToolkit.Editor.SceneSupport
{
    [InitializeOnLoad]
    public class SceneEnumGenerator
    {
        /// <summary>
        /// Enum生成完了時に発行されるイベント
        /// </summary>
        public static event Action OnGenerated;

        private static GenerateTiming _timing;

        /// <summary> 自動生成が有効かどうか </summary>
        internal static bool AutoGenerateEnabled => _timing != GenerateTiming.None;

        static SceneEnumGenerator()
        {
            EditorBuildSettings.sceneListChanged += OnSceneListChanged;
            EditorSceneManager.newSceneCreated += OnNewSceneCreated;

            var settings = UsefulToolkitSettingsScriptable.instance.CodeGenerationSectionSettings;

            _timing = settings.Timing;
        }

        private static void OnSceneListChanged()
        {
            if (_timing != GenerateTiming.None)
            {
                Generate();
            }
        }

        private static void OnNewSceneCreated(UnityEngine.SceneManagement.Scene scene, NewSceneSetup setup,
            NewSceneMode mode)
        {
            if (_timing != GenerateTiming.None)
            {
                Generate();
            }
        }

        [MenuItem("UsefulToolkit/Generate/Scene Enum", false, 16)]
        public static void Generate()
        {
            var settings = UsefulToolkitSettingsScriptable.instance.CodeGenerationSectionSettings;

            string ns = settings.Namespace;

            // Assets以下のシーンをすべて拾う。シーンを1つのフォルダにまとめる運用は現実的ではないため、
            // 検索範囲は限定しない。どちらのenumに入るかはBuildSettingsへの登録有無だけで決める。
            var scenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .ToArray();

            var scenePathSet = new HashSet<string>(scenePaths);

            // BuildScenesの並びはビルドインデックス順にする。enumの値はSceneFlowAssetにintで
            // 保存されるため、並びが変わると既存アセットの指すシーンが変わってしまう。
            // ビルドインデックス順であれば、末尾にシーンを追加しても既存の値がずれない。
            var includedPaths = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && scenePathSet.Contains(scene.path))
                .Select(scene => scene.path)
                .Distinct()
                .ToArray();

            var includedPathSet = new HashSet<string>(includedPaths);

            // 残りはすべてNonBuildScenes。こちらは順序に意味がないのでパス順で安定させる。
            var excludedPaths = scenePaths
                .Where(path => !includedPathSet.Contains(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var includedNames = includedPaths.Select(ToEnumMemberName).ToArray();
            var excludedNames = excludedPaths.Select(ToEnumMemberName).ToArray();

            // 重複したままenumを作ると、BuildScenesの値がビルドインデックスとずれる。
            // 片方だけ更新すると2つのenumの内容がちぐはぐになるため、どちらか一方でも重複していれば両方止める。
            var duplicatedInBuildScenes = HasDuplicatedNames("BuildScenes", includedNames, includedPaths);
            var duplicatedInNonBuildScenes = HasDuplicatedNames("NonBuildScenes", excludedNames, excludedPaths);

            if (duplicatedInBuildScenes || duplicatedInNonBuildScenes)
            {
                UsefulLogger.LogWarning("シーン名が重複している為、Enumの生成を中止しました。" +
                                        "重複しているシーンの名前を変更してから、再度生成してください。",
                    typeof(SceneEnumGenerator));
                return;
            }

            // Enum生成実行
            FileGenerator.AutoGenerateFile("BuildScenes.cs", GenerateEnumContent("BuildScenes", includedNames, ns),
                GenerateType.Runtime);
            FileGenerator.AutoGenerateFile("NonBuildScenes.cs",
                GenerateEnumContent("NonBuildScenes", excludedNames, ns), GenerateType.Editor);

            UsefulLogger.Log($"SceneEnums generated with namespace {ns} " +
                             $"(BuildScenes: {includedNames.Length} / NonBuildScenes: {excludedNames.Length})",
                typeof(SceneEnumGenerator));

            // イベント発行
            OnGenerated?.Invoke();
        }

        /// <summary>
        /// シーンパスからenumメンバー名を作る。識別子として使えない文字は'_'へ置き換える。
        /// </summary>
        private static string ToEnumMemberName(string scenePath)
        {
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string normalizedName = Regex.Replace(sceneName, @"[^a-zA-Z0-9_]", "_");

            // 数字始まりはC#の識別子として不正なので接頭辞を付ける
            if (normalizedName.Length > 0 && char.IsDigit(normalizedName[0]))
            {
                normalizedName = "_" + normalizedName;
            }

            return normalizedName;
        }

        /// <summary>
        /// enumメンバー名が重複しているかを調べる。
        /// 重複していた場合は、重複した名前と該当シーンのパスを警告ログへ出力する。
        /// </summary>
        /// <param name="enumName">調べる対象のenum名</param>
        /// <param name="names">enumメンバー名</param>
        /// <param name="paths">namesと同じ並びのシーンパス</param>
        /// <returns>重複があったか</returns>
        private static bool HasDuplicatedNames(string enumName, IReadOnlyList<string> names,
            IReadOnlyList<string> paths)
        {
            var duplicatedGroups = names
                .Select((name, index) => (Name: name, Path: paths[index]))
                .GroupBy(scene => scene.Name)
                .Where(group => group.Count() > 1)
                .ToArray();

            if (duplicatedGroups.Length == 0) return false;

            foreach (var group in duplicatedGroups)
            {
                var duplicatedPaths = string.Join("\n", group.Select(scene => scene.Path));
                UsefulLogger.LogWarning(
                    $"{enumName}: シーン名[{group.Key}]が重複しています。\n{duplicatedPaths}",
                    typeof(SceneEnumGenerator));
            }

            return true;
        }

        private static string GenerateEnumContent(string enumName, string[] values, string namespaceName)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();

            builder.AppendLine("// 自動生成ファイルの為、手動での編集は上書きされます。");
            builder.AppendLine("");
            builder.AppendLine($"namespace {namespaceName}");
            builder.AppendLine("{");
            builder.AppendLine($"    public enum {enumName}");
            builder.AppendLine("    {");

            for (int i = 0; i < values.Length; i++)
            {
                string comma = (i < values.Length - 1) ? "," : "";
                builder.AppendLine($"        {values[i]} = {i}{comma}");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }
    }

    /// <summary>
    /// シーンファイルの追加・削除・移動を検知してenumを再生成する。
    /// BuildSettingsの変更だけを監視していると、Assets以下のどこかにシーンが増減しても
    /// NonBuildScenesが古いままになるため、アセット側の変更も拾う。
    /// </summary>
    internal sealed class SceneAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!SceneEnumGenerator.AutoGenerateEnabled) return;

            if (!ContainsScene(importedAssets) &&
                !ContainsScene(deletedAssets) &&
                !ContainsScene(movedAssets) &&
                !ContainsScene(movedFromAssetPaths))
            {
                return;
            }

            // インポート処理中にAssetDatabase.Refreshを呼ばないよう、生成は次のフレームへ回す
            EditorApplication.delayCall += SceneEnumGenerator.Generate;
        }

        private static bool ContainsScene(string[] assetPaths)
        {
            return assetPaths.Any(path => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));
        }
    }
}