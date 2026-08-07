using System.IO;
using System.Linq;
using System.Text;

namespace UsefulToolkit.Framework
{
    public static class EnumGenerator
    {
        private static string NAME_SPACE = "UsefulToolkit.AutoGenerate";

        /// <summary>
        /// Enumファイルを生成します。
        /// </summary>
        /// <param name="enumName">Enumの名前</param>
        /// <param name="values">Enumの値リスト</param>
        /// <param name="outputFolder">出力先フォルダ（未指定時はCodeSupport設定を使用）</param>
        /// <param name="namespaceName">名前空間（未指定時はCodeSupport設定を使用）</param>
        public static void GenerateEnum(string enumName, string[] values, string outputFolder = null,
            string namespaceName = null)
        {
            if (values == null || values.Length == 0) return;

            var settings = UsefulToolkitSettingsScriptable.instance.CodeGenerationSectionSettings;

            var savePath = settings.RuntimeSavePath;
            var generateNamespace = settings.Namespace;

            // 引数が未指定なら設定から取得
            if (string.IsNullOrEmpty(outputFolder)) outputFolder = savePath;
            if (string.IsNullOrEmpty(namespaceName)) namespaceName = generateNamespace;

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            string path = Path.Combine(outputFolder, enumName + ".cs");

            StringBuilder builder = new StringBuilder();

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

            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        }
    }
}