using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UsefulToolkit.Initialization;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Scene;
using UsefulToolkit.Editor.ProjectSettings;
using UsefulToolkit.Editor.Reflection;
using UsefulToolkit.Editor.Utility;

namespace UsefulToolkit.Editor.Initialize
{
    /// <summary>
    /// アクティブシーンを走査し、そのシーン専用のGameCompositer派生クラスを生成する。
    /// 生成されたクラスのフィールドへの実体の割り当てはInspectorでの手作業とし、
    /// このツールはシーンファイルには一切触れない。
    /// </summary>
    public static class GameCompositerGenerator
    {
        private const string LastFolderKey = "UsefulToolkit.GameCompositerGenerator.LastFolder";
        private const string ClassNameSuffix = "Compositer";

        [MenuItem("UsefulToolkit/Generate/Scene Compositer", false, 18)]
        public static void Generate()
        {
            var scene = EditorSceneManager.GetActiveScene();

            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("エラー", "シーンが保存されていません。先にシーンを保存してください。", "OK");
                return;
            }

            if (CollectInitializerFields(scene).Count == 0 &&
                !EditorUtility.DisplayDialog(
                    "確認",
                    $"シーン [{scene.name}] にInitializerBaseを継承したコンポーネントが見つかりませんでした。\n" +
                    "ChildBoardの登録のみを行うクラスを生成しますか？",
                    "生成する",
                    "中止"))
            {
                return;
            }

            string saveDirectory = SelectSaveDirectory();
            if (saveDirectory == null) return;

            string filePath = BuildFilePath(scene, saveDirectory);

            if (File.Exists(filePath) &&
                !EditorUtility.DisplayDialog("確認", $"{filePath} は既に存在します。上書きしますか？", "上書き", "中止"))
            {
                return;
            }

            var result = GenerateTo(scene, saveDirectory);

            EditorUtility.DisplayDialog(
                "生成完了",
                $"{Path.GetFileName(result.FilePath)} を生成しました。\n\n" +
                $"ChildStateBoard : {result.StateBoardCount} 件\n" +
                $"ChildEventBoard : {result.EventBoardCount} 件\n" +
                $"Initializer : {result.InitializerCount} 種類\n\n" +
                "生成されたコンポーネントをシーンに配置し、Inspectorから各フィールドを割り当ててください。",
                "OK");
        }

        /// <summary>生成結果の内訳。</summary>
        public readonly struct GenerateResult
        {
            public readonly string FilePath;
            public readonly int StateBoardCount;
            public readonly int EventBoardCount;
            public readonly int InitializerCount;

            public GenerateResult(string filePath, int stateBoardCount, int eventBoardCount, int initializerCount)
            {
                FilePath = filePath;
                StateBoardCount = stateBoardCount;
                EventBoardCount = eventBoardCount;
                InitializerCount = initializerCount;
            }
        }

        /// <summary>
        /// 対話なしで生成を行う本体。Generate()はこれをダイアログで包んでいるだけ。
        /// </summary>
        /// <param name="scene">走査対象のシーン</param>
        /// <param name="saveDirectory">Assets配下の保存先ディレクトリ</param>
        public static GenerateResult GenerateTo(UnityEngine.SceneManagement.Scene scene, string saveDirectory)
        {
            var initializerFields = CollectInitializerFields(scene);

            var stateBoardTypes = CollectBoardTypes<ChildStateBoardBase>()
                // SceneBoardはBlackBoardのコンストラクタが受け取るため、ここで登録すると二重管理になる
                .Where(type => type != typeof(SceneBoard))
                .ToArray();

            var eventBoardTypes = CollectBoardTypes<ChildEventBoardBase>();

            string filePath = BuildFilePath(scene, saveDirectory);

            string namespaceName = UsefulToolkitSettingsScriptable.instance
                .CodeGenerationSectionSettings.Namespace;

            string source = GameCompositerSourceBuilder.Build(
                namespaceName,
                Path.GetFileNameWithoutExtension(filePath),
                scene.name,
                stateBoardTypes,
                eventBoardTypes,
                initializerFields);

            FileGenerator.WriteFile(filePath, source);

            WarnRequiredAssemblies(stateBoardTypes, eventBoardTypes, initializerFields);

            return new GenerateResult(
                filePath,
                stateBoardTypes.Length,
                eventBoardTypes.Length,
                initializerFields.Count);
        }

        private static string BuildFilePath(UnityEngine.SceneManagement.Scene scene, string saveDirectory)
        {
            string className = ToIdentifier(scene.name) + ClassNameSuffix;
            return Path.Combine(saveDirectory, className + ".cs").Replace('\\', '/');
        }

        /// <summary>
        /// シーン内のInitializerBase派生を具象型ごとにまとめる。
        /// 同じ型が複数あるシーンではListフィールドにする。
        /// </summary>
        private static IReadOnlyList<GameCompositerSourceBuilder.InitializerField> CollectInitializerFields(
            UnityEngine.SceneManagement.Scene scene)
        {
            var initializers = scene.GetRootGameObjects()
                // 無効化されたオブジェクト上のInitializerも初期化対象になりうるので含める
                .SelectMany(root => root.GetComponentsInChildren<InitializerBase>(true))
                // GameCompositerがAwakeで直接呼ぶため、生成物からは除外する
                .Where(initializer => initializer is not UsefulToolkitRuntimeInitializer)
                .ToArray();

            return initializers
                .GroupBy(initializer => initializer.GetType())
                .OrderBy(group => group.Key.FullName, StringComparer.Ordinal)
                .Select(group => new GameCompositerSourceBuilder.InitializerField(
                    group.Key,
                    ToFieldName(group.Key.Name, group.Count() > 1),
                    group.Count() > 1))
                .ToArray();
        }

        /// <summary>
        /// プロジェクト全体からChildBoardの具象型を集める。
        /// 引数なしで生成できないものは今の設計では扱えないため、警告を出して除外する。
        /// </summary>
        private static Type[] CollectBoardTypes<T>()
        {
            var result = new List<Type>();

            foreach (var type in TypeCollector.GetDerivedTypes<T>())
            {
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    Debug.LogWarning(
                        $"[UsefulToolkit] {type.FullName} は引数なしのコンストラクタを持たないため、登録対象から除外しました。");
                    continue;
                }

                result.Add(type);
            }

            return result
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>保存先をユーザーに選ばせる。キャンセルまたはAssets外ならnullを返す。</summary>
        private static string SelectSaveDirectory()
        {
            string lastFolder = EditorPrefs.GetString(LastFolderKey, "Assets");
            string selectedPath = EditorUtility.OpenFolderPanel("GameCompositerの保存先を選択", lastFolder, "");

            if (string.IsNullOrEmpty(selectedPath)) return null;

            string assetsPath = Path.GetFullPath(UnityEngine.Application.dataPath);
            string fullSelectedPath = Path.GetFullPath(selectedPath);

            if (!fullSelectedPath.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("エラー", "Assetsフォルダ内を選択してください。", "OK");
                return null;
            }

            string relativePath = "Assets" + fullSelectedPath.Substring(assetsPath.Length).Replace('\\', '/');
            EditorPrefs.SetString(LastFolderKey, relativePath);

            return relativePath;
        }

        /// <summary>
        /// 生成コードが参照する型のアセンブリを知らせる。保存先のasmdefに参照が足りないと
        /// コンパイルエラーになるが、asmdefの自動編集は影響が大きいので警告に留める。
        /// </summary>
        private static void WarnRequiredAssemblies(
            IReadOnlyList<Type> stateBoardTypes,
            IReadOnlyList<Type> eventBoardTypes,
            IReadOnlyList<GameCompositerSourceBuilder.InitializerField> initializerFields)
        {
            // Injectされる依存型は生成コードにTryGetContent<T>として現れるが、Initializerと違って
            // シーン上には存在せず(Application層の実体などが入る)、別アセンブリにあることが多い。
            // ここを拾い漏らすと生成先asmdefの参照不足に気付けない。
            var dependencyTypes = initializerFields
                .SelectMany(field => GameCompositerSourceBuilder.GetInjectableInterfaces(field.InitializerType))
                .SelectMany(injectable => injectable.GetGenericArguments());

            var assemblies = stateBoardTypes
                .Concat(eventBoardTypes)
                .Concat(initializerFields.Select(field => field.InitializerType))
                .Concat(dependencyTypes)
                .Select(type => type.Assembly)
                .Append(typeof(GameCompositer).Assembly)
                .Append(typeof(IBlackBoard).Assembly)
                .Select(assembly => assembly.GetName().Name)
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal);

            Debug.LogWarning(
                "[UsefulToolkit] 生成先のasmdefが次のアセンブリを参照しているか確認してください : " +
                string.Join(", ", assemblies));
        }

        /// <summary>クラス名からフィールド名を作る。先頭を小文字にし、_を付ける。</summary>
        private static string ToFieldName(string typeName, bool isList)
        {
            string identifier = ToIdentifier(typeName);
            string camel = identifier.Length > 0
                ? char.ToLowerInvariant(identifier[0]) + identifier.Substring(1)
                : identifier;

            return isList ? $"_{camel}List" : $"_{camel}";
        }

        /// <summary>C#の識別子として使えない文字を'_'へ置き換える。</summary>
        private static string ToIdentifier(string source)
        {
            string normalized = Regex.Replace(source, @"[^a-zA-Z0-9_]", "_");

            if (normalized.Length > 0 && char.IsDigit(normalized[0]))
            {
                normalized = "_" + normalized;
            }

            return normalized;
        }
    }
}
