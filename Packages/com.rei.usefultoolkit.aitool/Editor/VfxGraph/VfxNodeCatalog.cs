using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static UsefulToolkit.Editor.Ai.VfxGraphReflection;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// グラフに追加できるノードの一覧。グラフ画面のノード検索と同じ VFXLibrary の候補を、書き出しと同じ表示名で引けるようにする。
    /// </summary>
    internal static class VfxNodeCatalog
    {
        /// <summary>追加できるノード 1 つ。</summary>
        public sealed class Entry
        {
            public VfxNodeKind Kind { get; set; }

            /// <summary>書き出しと同じ表示名。</summary>
            public string Name { get; set; }

            /// <summary>「/」区切りのカテゴリ。並び順の印（#1 など）は除いてある。</summary>
            public string Category { get; set; }

            /// <summary>モデルの型名。Property では値の型名。</summary>
            public string TypeName { get; set; }

            /// <summary>VFX Graph の Variant。これからノードを作る。</summary>
            public object Variant { get; set; }
        }

        private static readonly Regex OrderMark = new(@"#\d+", RegexOptions.Compiled);

        /// <summary>指定した種類の候補をすべて返す。派生（サブバリアント）も平らに含める。</summary>
        public static List<Entry> GetEntries(VfxNodeKind kind)
        {
            Type library = VfxType("VFXLibrary");
            string methodName = kind switch
            {
                VfxNodeKind.Block => "GetBlocks",
                VfxNodeKind.Operator => "GetOperators",
                VfxNodeKind.Context => "GetContexts",
                _ => "GetParameters",
            };

            var entries = new List<Entry>();
            foreach (object descriptor in CallStatic(library, methodName) as IEnumerable ?? Array.Empty<object>())
            {
                AddWithSubVariants(kind, descriptor, entries);
            }

            return entries;
        }

        /// <summary>
        /// 名前（大文字小文字を区別しない完全一致）で 1 つに絞る。category を渡すとカテゴリの部分一致でも絞る。
        /// </summary>
        public static bool TryFind(VfxNodeKind kind, string name, string category, out Entry entry, out string error)
        {
            List<Entry> matches = GetEntries(kind)
                .Where(e => string.Equals(e.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(e => string.IsNullOrEmpty(category) || e.Category.IndexOf(category, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            entry = matches.Count == 1 ? matches[0] : null;
            error = matches.Count switch
            {
                1 => "",
                0 => $"{kind} \"{name}\" が見つかりません。list-vfx-nodes で名前を確認してください。",
                _ => $"{kind} \"{name}\" が {matches.Count} 個あります。category で絞ってください: " +
                     string.Join(", ", matches.Select(m => $"\"{m.Category}\"")),
            };
            return entry != null;
        }

        private static void AddWithSubVariants(VfxNodeKind kind, object descriptor, List<Entry> entries)
        {
            entries.Add(new Entry
            {
                Kind = kind,
                Name = VfxGraphTextExporter.DisplayName(Get(descriptor, "name") as string),
                Category = CleanCategory(Get(descriptor, "category") as string),
                TypeName = (Get(descriptor, "modelType") as Type)?.Name ?? "",
                Variant = Get(descriptor, "variant"),
            });

            foreach (object subVariant in Get(descriptor, "subVariantDescriptors") as IEnumerable ?? Array.Empty<object>())
            {
                AddWithSubVariants(kind, subVariant, entries);
            }
        }

        private static string CleanCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return "";
            return string.Join("/", OrderMark.Replace(category, "").Split('/').Select(s => s.Trim()).Where(s => s.Length > 0));
        }
    }
}
