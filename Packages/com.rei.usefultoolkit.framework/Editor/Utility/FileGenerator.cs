#nullable enable

using System.IO;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Framework
{
    public static class FileGenerator
    {
        public static string GenerateRuntimeRootPath => UsefulToolkitSettingsScriptable.instance
            .CodeGenerationSectionSettings.RuntimeSavePath;

        public static string GenerateLocalRootPath => UsefulToolkitSettingsScriptable.instance
            .CodeGenerationSectionSettings.LocalSavePath;

        /// <summary>
        /// 指定したパスにファイルを書き込み、AssetDatabaseをリフレッシュします。
        /// </summary>
        public static void WriteFile(string filePath, string content)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, content);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// ソースコードの自動生成を行う
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="content"></param>
        /// <param name="generateType"></param>
        /// <param name="folderName"></param>
        public static void AutoGenerateFile(
            string fileName,
            string content,
            GenerateType generateType,
            string? folderName = null)
        {
            string rootPath = GenerateRuntimeRootPath;

            // GenerateTypeごとのフォルダ
            rootPath = Path.Combine(rootPath, generateType.ToString());

            // 任意のサブフォルダ
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                rootPath = Path.Combine(rootPath, folderName);
            }

            Directory.CreateDirectory(rootPath);

            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
            {
                fileName += ".cs";
            }

            string filePath = Path.Combine(rootPath, fileName);

            File.WriteAllText(filePath, content);

            AssetDatabase.Refresh();
        }
public static T AutoGenerateAsset<T>(
    string assetName,
    GenerateType generateType,
    string? folderName = null)
    where T : ScriptableObject
{
    string rootPath = generateType == GenerateType.Editor ? GenerateLocalRootPath : GenerateRuntimeRootPath;

    if (generateType != GenerateType.Editor)
    {
        rootPath = Path.Combine(rootPath, generateType.ToString());
    }

    if (!string.IsNullOrWhiteSpace(folderName))
    {
        rootPath = Path.Combine(rootPath, folderName);
    }

    Directory.CreateDirectory(rootPath);

    if (string.IsNullOrEmpty(Path.GetExtension(assetName)))
    {
        assetName += ".asset";
    }

    string assetPath = Path.Combine(rootPath, assetName);

    T instance = ScriptableObject.CreateInstance<T>();
    AssetDatabase.CreateAsset(instance, assetPath);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    return instance;
}
}

public enum GenerateType
{
Editor,
Runtime
}
}