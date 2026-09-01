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
    /// </summary>
    internal static class InitializerTemplateGenerator
    {
        /// <summary>生成結果の内訳。</summary>
        internal readonly struct GenerateResult
        {
            /// <summary>生成対象として宣言された全クラス名。既存で書き込みを飛ばしたものも含む。</summary>
            public readonly IReadOnlyList<string> ClassNames;

            /// <summary>
            /// 実際にファイルを書き出した数。
            /// 0の場合はコンパイルが走らないため、ドメインリロードを待たずに続きを進める必要がある。
            /// </summary>
            public readonly int WrittenCount;

            public GenerateResult(IReadOnlyList<string> classNames, int writtenCount)
            {
                ClassNames = classNames;
                WrittenCount = writtenCount;
            }
        }

        /// <summary>
        /// 全てのProviderからテンプレートを集めて書き出す。
        /// </summary>
        /// <param name="saveDirectory">Assets配下の保存先ディレクトリ</param>
        /// <param name="compositorClassName">常駐シーンのCompositorクラス名</param>
        /// <param name="sceneName">常駐シーンのシーン名</param>
        internal static GenerateResult Generate(string saveDirectory, string compositorClassName, string sceneName)
        {
            string namespaceName = UsefulToolkitSettingsScriptable.instance
                .CodeGenerationSectionSettings.Namespace;

            var context = new InitializerTemplateContext(namespaceName, compositorClassName, sceneName);

            var classNames = new List<string>();
            var declared = new HashSet<string>(StringComparer.Ordinal);
            int writtenCount = 0;

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

                    classNames.Add(template.ClassName);

                    if (WriteTemplate(saveDirectory, template))
                    {
                        writtenCount++;
                    }
                }
            }

            if (writtenCount > 0)
            {
                AssetDatabase.Refresh();
            }

            return new GenerateResult(classNames, writtenCount);
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
