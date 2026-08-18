#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// 構造テンプレートJSONの読み書き。
    /// プロジェクト固有の定義があればそれを、無ければパッケージ同梱の既定テンプレートを使う
    /// </summary>
    public static class ProjectStructureTemplateIO
    {
        /// <summary>プロジェクト固有の定義。Assets外に置くことで、整理処理自身の対象にならないようにしている</summary>
        public const string ProjectTemplatePath = "ProjectSettings/UsefulToolkitProjectStructure.json";

        private const string DefaultTemplateRelativePath = "Editor/ProjectStructure/DefaultTemplate.json";
        private const string FallbackPackageRoot = "Packages/com.rei.usefultoolkit.framework";

        public static bool HasProjectTemplate => File.Exists(ProjectTemplatePath);

        /// <summary>
        /// パッケージ同梱の既定テンプレートの物理パス。
        /// git経由でインストールされた場合はPackageCache配下に実体があるため、UPMに解決させる
        /// </summary>
        public static string DefaultTemplatePath
        {
            get
            {
                var packageInfo = PackageInfo.FindForAssembly(typeof(ProjectStructureTemplateIO).Assembly);
                string root = packageInfo != null ? packageInfo.resolvedPath : FallbackPackageRoot;
                return Path.Combine(root, DefaultTemplateRelativePath).Replace('\\', '/');
            }
        }

        /// <summary>
        /// テンプレートを読み込む
        /// </summary>
        /// <param name="loadedFrom">実際に読み込んだファイルのパス</param>
        /// <param name="error">読み込みに失敗した場合の理由</param>
        public static ProjectStructureTemplate? Load(out string loadedFrom, out string? error)
        {
            loadedFrom = HasProjectTemplate ? ProjectTemplatePath : DefaultTemplatePath;

            if (!File.Exists(loadedFrom))
            {
                error = $"テンプレートが見つかりませんでした: {loadedFrom}";
                return null;
            }

            try
            {
                string json = File.ReadAllText(loadedFrom);
                var template = JsonUtility.FromJson<ProjectStructureTemplate>(json);

                if (template == null)
                {
                    error = $"テンプレートの解析に失敗しました: {loadedFrom}";
                    return null;
                }

                // JsonUtilityは配列項目が無いとnullを入れてくるので、以降で扱いやすいように埋めておく
                template.excludes ??= new();
                template.folders ??= new();
                template.rules ??= new();

                error = null;
                return template;
            }
            catch (Exception exception)
            {
                error = $"テンプレートの読み込みに失敗しました: {exception.Message}";
                return null;
            }
        }

        /// <summary>
        /// プロジェクト固有のテンプレートとして保存する
        /// </summary>
        public static bool SaveProjectTemplate(ProjectStructureTemplate template, out string? error)
        {
            try
            {
                string? directory = Path.GetDirectoryName(ProjectTemplatePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(ProjectTemplatePath, JsonUtility.ToJson(template, true));
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = $"テンプレートの保存に失敗しました: {exception.Message}";
                return false;
            }
        }

        /// <summary>
        /// 既定テンプレートをプロジェクト固有のテンプレートとして複製する
        /// </summary>
        public static bool CopyDefaultToProject(out string? error)
        {
            var template = Load(out _, out error);
            if (template == null) return false;

            return SaveProjectTemplate(template, out error);
        }

        /// <summary>
        /// テンプレートJSONを既定のエディタで開く
        /// </summary>
        public static void OpenInEditor(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[UsefulToolkit] テンプレートが見つかりません: {path}");
                return;
            }

            EditorUtility.OpenWithDefaultApp(path);
        }
    }
}
