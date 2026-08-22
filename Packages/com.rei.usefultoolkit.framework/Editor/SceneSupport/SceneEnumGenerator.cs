using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
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

            WarnDuplicatedNames("BuildScenes", includedNames);
            WarnDuplicatedNames("NonBuildScenes", excludedNames);

            // Enum生成実行
            FileGenerator.AutoGenerateFile("BuildScenes.cs", GenerateEnumContent("BuildScenes", includedNames, ns), GenerateType.Runtime);
            FileGenerator.AutoGenerateFile("NonBuildScenes.cs", GenerateEnumContent("NonBuildScenes", excludedNames, ns), GenerateType.Editor);

            Debug.Log($"[UsefulTools] SceneEnums generated with namespace {ns} " +
                      $"(BuildScenes: {includedNames.Length} / NonBuildScenes: {excludedNames.Length})");

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
        /// enumはシーン名だけで作るため、別フォルダの同名シーンは1つにまとめられてしまう。
        /// 黙って消えると原因が分からなくなるので警告を出す。
        /// </summary>
        private static void WarnDuplicatedNames(string enumName, IReadOnlyList<string> names)
        {
            var duplicatedNames = names
                .GroupBy(name => name)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            if (duplicatedNames.Length == 0) return;

            Debug.LogWarning($"[UsefulTools] {enumName}: 同名のシーンが複数あるため、次の名前は1つにまとめられました: " +
                             $"{string.Join(", ", duplicatedNames)}");
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

            var distinctValues = values.Distinct().ToArray();
            for (int i = 0; i < distinctValues.Length; i++)
            {
                string comma = (i < distinctValues.Length - 1) ? "," : "";
                builder.AppendLine($"        {distinctValues[i]}{comma}");
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