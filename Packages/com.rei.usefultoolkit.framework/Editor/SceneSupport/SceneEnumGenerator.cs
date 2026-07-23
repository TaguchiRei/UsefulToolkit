using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UsefulToolkit.Framework
{
    [InitializeOnLoad]
    public class SceneEnumGenerator
    {
        /// <summary>
        /// Enum生成完了時に発行されるイベント
        /// </summary>
        public static event Action OnGenerated;

        private static GenerateTiming _timing;

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

            string targetPath = "Assets/Scenes"; // デフォルトの検索パス
            string ns = settings.Namespace;
            
            // ターゲットフォルダが存在するか確認
            if (!AssetDatabase.IsValidFolder(targetPath))
            {
                Debug.LogWarning($"[UsefulTools] Target scenes directory not found: {targetPath}");
                return;
            }

            // 指定ディレクトリ内の全シーンファイルを取得
            var sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { targetPath });
            var scenePaths = sceneGuids.Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();

            // BuildSettings に登録されているかどうかの判定用
            var buildScenes = EditorBuildSettings.scenes.ToDictionary(s => s.path, s => s.enabled);

            var includedScenesList = new System.Collections.Generic.List<string>();
            var excludedScenesList = new System.Collections.Generic.List<string>();

            foreach (var path in scenePaths)
            {
                string sceneName = Path.GetFileNameWithoutExtension(path);
                string normalizedName = Regex.Replace(sceneName, @"[^a-zA-Z0-9_]", "_");

                if (buildScenes.TryGetValue(path, out bool enabled) && enabled)
                {
                    includedScenesList.Add(normalizedName);
                }
                else
                {
                    excludedScenesList.Add(normalizedName);
                }
            }

            // Enum生成実行
            FileGenerator.AutoGenerateFile("BuildScenes.cs", GenerateEnumContent("BuildScenes", includedScenesList.ToArray(), ns), GenerateType.Runtime);
            FileGenerator.AutoGenerateFile("NonBuildScenes.cs", GenerateEnumContent("NonBuildScenes", excludedScenesList.ToArray(), ns), GenerateType.Editor);

            Debug.Log($"[UsefulTools] SceneEnums generated with namespace {ns}");

            // イベント発行
            OnGenerated?.Invoke();
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
}