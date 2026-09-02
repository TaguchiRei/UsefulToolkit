using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.Editor.ProjectSettings;

namespace UsefulToolkit.Editor.Initialize
{
    /// <summary>
    /// <see cref="IInitializerTemplateProvider"/> を集め、生成先ディレクトリへInitializerのソースを書き出す。
    /// 既存ファイルは利用者が書き換えている可能性があるため、警告を出して書き込まない。
    ///
    /// 生成は常駐シーンの作成/更新(<see cref="PersistentSceneCreator"/>)からは切り離されており、
    /// メニュー <c>UsefulToolkit/Generate/Initializers</c> から単独で実行する。実作業の順序は
    /// 「Initializerを生成 → UsefulToolkit/Scene/GenerateUsefulPersistentScene で取り付けとCompositor再生成」。
    /// </summary>
    internal static class InitializerTemplateGenerator
    {
        /// <summary>生成結果の内訳。</summary>
        internal readonly struct GenerateResult
        {
            /// <summary>生成対象として宣言された全クラス名。既存で書き込みを飛ばしたものも含む。</summary>
            public readonly IReadOnlyList<string> ClassNames;

            /// <summary>実際にファイルを書き出した数。</summary>
            public readonly int WrittenCount;

            public GenerateResult(IReadOnlyList<string> classNames, int writtenCount)
            {
                ClassNames = classNames;
                WrittenCount = writtenCount;
            }
        }

        /// <summary>
        /// 既存の常駐シーンの情報をもとに、Initializerのソースを生成先へ書き出す。
        /// 常駐シーンが存在しないとCompositorのクラス名を解決できないため、その場合は中止する。
        /// </summary>
        [MenuItem("UsefulToolkit/Generate/Initializers", false, 17)]
        internal static void GenerateFromMenu()
        {
            string scenePath = PersistentSceneCreator.FindExistingPersistentScenePath();
            if (string.IsNullOrEmpty(scenePath))
            {
                EditorUtility.DisplayDialog(
                    "エラー",
                    "UsefulToolkitRuntimeInitializer を持つ常駐シーンが見つかりませんでした。\n" +
                    "先に UsefulToolkit/Scene/GenerateUsefulPersistentScene で常駐シーンを作成してください。",
                    "OK");
                return;
            }

            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string compositorClassName = GameCompositorGenerator.ToCompositorClassName(sceneName);

            string saveDirectory = PersistentSceneCreator.FindExistingCompositorDirectory(
                sceneName, scenePath, out bool compositorFound);

            // 生成コードは <Compositor>.TryRegisterContent を呼ぶため、Compositorと同じアセンブリへ置く必要がある。
            // その所在が分からない状態で生成すると別アセンブリに落ちてコンパイルできない。
            if (!compositorFound)
            {
                EditorUtility.DisplayDialog(
                    "エラー",
                    $"常駐シーンの Compositor [{compositorClassName}] が見つかりませんでした。\n" +
                    "先に UsefulToolkit/Scene/GenerateUsefulPersistentScene を実行して Compositor を生成してください。",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Initializerの生成",
                    $"生成先 : {saveDirectory}\n常駐シーン : {sceneName}\n\n" +
                    "このフォルダへ Initializer のソースを生成します。\n" +
                    "既存ファイルは上書きしません。",
                    "生成", "キャンセル"))
            {
                return;
            }

            var result = Generate(saveDirectory, compositorClassName, sceneName);

            string skippedNote = result.WrittenCount < result.ClassNames.Count
                ? "既存ファイルがあるものは生成をスキップしました。" +
                  "作り直す場合はファイルを削除してから再実行してください。\n\n"
                : string.Empty;

            EditorUtility.DisplayDialog(
                "生成完了",
                $"宣言された Initializer : {result.ClassNames.Count} 種類\n" +
                $"新規に書き出したファイル : {result.WrittenCount} 件\n\n" +
                skippedNote +
                "この後 UsefulToolkit/Scene/GenerateUsefulPersistentScene を実行して、\n" +
                "生成した Initializer の取り付けと Compositor の再生成を行ってください。",
                "OK");
        }

        /// <summary>
        /// 全てのProviderからテンプレートを集めて書き出す。
        /// </summary>
        /// <param name="saveDirectory">Assets配下の保存先ディレクトリ</param>
        /// <param name="compositorClassName">常駐シーンのCompositorクラス名</param>
        /// <param name="sceneName">常駐シーンのシーン名</param>
        internal static GenerateResult Generate(string saveDirectory, string compositorClassName, string sceneName)
        {
            var context = BuildContext(compositorClassName, sceneName);

            var classNames = new List<string>();
            int writtenCount = 0;

            foreach (var template in EnumerateTemplates(context))
            {
                classNames.Add(template.ClassName);

                if (WriteTemplate(saveDirectory, template))
                {
                    writtenCount++;
                }
            }

            if (writtenCount > 0)
            {
                AssetDatabase.Refresh();
            }

            return new GenerateResult(classNames, writtenCount);
        }

        /// <summary>
        /// ファイルを書き出さずに、Providerが宣言するInitializerのクラス名だけを集める。
        /// 常駐シーン側が「どの型をシーンへ取り付けるか」を知るために使う。
        /// </summary>
        /// <param name="compositorClassName">常駐シーンのCompositorクラス名</param>
        /// <param name="sceneName">常駐シーンのシーン名</param>
        internal static IReadOnlyList<string> CollectDeclaredClassNames(
            string compositorClassName, string sceneName)
        {
            var context = BuildContext(compositorClassName, sceneName);

            return EnumerateTemplates(context)
                .Select(template => template.ClassName)
                .ToArray();
        }

        /// <summary>
        /// テンプレートの組み立てに渡すコンテキストを作る。名前空間は生成設定から取る。
        /// </summary>
        private static InitializerTemplateContext BuildContext(string compositorClassName, string sceneName)
        {
            string namespaceName = UsefulToolkitSettingsScriptable.instance
                .CodeGenerationSectionSettings.Namespace;

            return new InitializerTemplateContext(namespaceName, compositorClassName, sceneName);
        }

        /// <summary>
        /// 全Providerを順に呼び、検証を通ったテンプレートだけを列挙する。
        /// Provider取得の失敗、空テンプレート、クラス名の重複はログを出して該当分を捨てる。
        /// </summary>
        private static IEnumerable<InitializerTemplate> EnumerateTemplates(InitializerTemplateContext context)
        {
            var declared = new HashSet<string>(StringComparer.Ordinal);

            foreach (var provider in CollectProviders())
            {
                IReadOnlyList<InitializerTemplate> templates;

                try
                {
                    templates = provider.GetTemplates(context)?.ToArray() ?? Array.Empty<InitializerTemplate>();
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[UsefulToolkit] {provider.GetType().FullName} からのInitializerテンプレート取得に失敗しました。");
                    Debug.LogException(exception);
                    continue;
                }

                foreach (var template in templates)
                {
                    if (string.IsNullOrEmpty(template.ClassName) || string.IsNullOrEmpty(template.Source))
                    {
                        Debug.LogWarning(
                            $"[UsefulToolkit] {provider.GetType().FullName} が空のInitializerテンプレートを返しました。");
                        continue;
                    }

                    // 同名クラスを2つ生成するとコンパイルエラーになるため、後から来た方を捨てる
                    if (!declared.Add(template.ClassName))
                    {
                        Debug.LogError(
                            $"[UsefulToolkit] Initializer [{template.ClassName}] が複数のProviderから宣言されました。" +
                            $"{provider.GetType().FullName} の分を無視します。");
                        continue;
                    }

                    yield return template;
                }
            }
        }

        /// <summary>
        /// テンプレート1件をファイルへ書き出す。既存ファイルには触れない。
        /// </summary>
        /// <param name="saveDirectory">Assets配下の保存先ディレクトリ</param>
        /// <param name="template">書き出すテンプレート</param>
        /// <returns>実際に書き込んだ場合はtrue</returns>
        private static bool WriteTemplate(string saveDirectory, InitializerTemplate template)
        {
            string filePath = Path.Combine(saveDirectory, template.ClassName + ".cs").Replace('\\', '/');

            if (File.Exists(filePath))
            {
                Debug.LogWarning(
                    $"[UsefulToolkit] {filePath} は既に存在する為、生成しませんでした。" +
                    "作り直す場合はこのファイルを削除してから再実行してください。");
                return false;
            }

            try
            {
                Directory.CreateDirectory(saveDirectory);
                File.WriteAllText(filePath, template.Source);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[UsefulToolkit] {filePath} の生成に失敗しました。");
                Debug.LogException(exception);
                return false;
            }

            Debug.Log($"[UsefulToolkit] Initializer {filePath} を生成しました。");
            return true;
        }

        /// <summary>
        /// <see cref="IInitializerTemplateProvider"/> の実装を、引数なしで生成できるものだけ集める。
        /// </summary>
        private static IEnumerable<IInitializerTemplateProvider> CollectProviders()
        {
            return TypeCache.GetTypesDerivedFrom<IInitializerTemplateProvider>()
                .Where(type => !type.IsAbstract && !type.IsInterface && type.GetConstructor(Type.EmptyTypes) != null)
                .Select(type => (IInitializerTemplateProvider)Activator.CreateInstance(type))
                .OrderBy(provider => provider.Order)
                .ToArray();
        }
    }
}
