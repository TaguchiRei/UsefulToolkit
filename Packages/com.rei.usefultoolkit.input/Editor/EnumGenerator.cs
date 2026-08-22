using System.Text;

namespace UsefulToolkit.Editor.Input
{
    /// <summary>
    /// 文字列配列から単純なenumのソースコード文字列を組み立てる。
    /// 書き込み自体はFileGenerator(UsefulToolkit.Framework)に任せる。
    /// </summary>
    internal static class EnumGenerator
    {
        public static string BuildSource(string enumName, string[] values, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// このファイルはUsefulToolkit.Inputによって自動生成されています。直接編集しないでください。");
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    public enum {enumName}");
            sb.AppendLine("    {");
            foreach (var value in values)
            {
                sb.AppendLine($"        {value},");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
