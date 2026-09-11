using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.External.Input;

namespace UsefulToolkit.Editor.Input
{
    /// <summary>
    /// 外部入力スロットのリネームに合わせて、プロジェクト内の .inputactions のバインディングを書き換える。
    ///
    /// 書き換えは .inputactions のテキストのうち <c>"path": "&lt;レイアウト名&gt;/旧名</c> の部分だけを置換する。
    /// InputSystem の API で読み書きし直すとJSON全体が再シリアライズされ、差分が全体に及ぶ為。
    /// </summary>
    internal static class ExternalInputBindingPatcher
    {
        /// <summary> 書き換え1ファイル分 </summary>
        internal readonly struct AssetPatch
        {
            public readonly string AssetPath;
            public readonly string NewText;
            public readonly bool HasBom;

            public AssetPatch(string assetPath, string newText, bool hasBom)
            {
                AssetPath = assetPath;
                NewText = newText;
                HasBom = hasBom;
            }
        }

        /// <summary> 書き換えの計画。これを作る段階ではファイルに一切触れない </summary>
        internal sealed class Plan
        {
            /// <summary> 旧名 → 新名 </summary>
            public readonly Dictionary<string, string> Renames = new(StringComparer.OrdinalIgnoreCase);

            /// <summary> 旧名ごとの書き換え件数 </summary>
            public readonly Dictionary<string, int> RewriteCounts = new(StringComparer.OrdinalIgnoreCase);

            /// <summary> 削除されたスロットの名前 </summary>
            public readonly List<string> RemovedNames = new();

            /// <summary> 書き換えるファイルと書き換え後の内容 </summary>
            public readonly List<AssetPatch> Patches = new();

            /// <summary> 削除されたスロットを指したまま残るバインディング。「アセットパス : パス」の形 </summary>
            public readonly List<string> DanglingBindings = new();

            /// <summary> 書き換える件数の合計 </summary>
            public int TotalRewrites => RewriteCounts.Values.Sum();
        }

        /// <summary>
        /// 前回生成時と現在の宣言を突き合わせ、書き換えの計画を作る。
        /// .inputactions を1つでも読めなかった場合はエラーを出してfalseを返す。
        /// </summary>
        /// <param name="layoutName">仮想デバイスのレイアウト名</param>
        /// <param name="previousNames">前回生成時の Id → 名前</param>
        /// <param name="currentSlots">現在の宣言</param>
        /// <param name="plan">作った計画</param>
        /// <returns>計画を作れた場合はtrue</returns>
        public static bool TryBuildPlan(string layoutName, IReadOnlyDictionary<string, string> previousNames,
            IReadOnlyList<ExternalInputSlot> currentSlots, out Plan plan)
        {
            plan = new Plan();

            var currentIds = new HashSet<string>();

            foreach (var slot in currentSlots)
            {
                currentIds.Add(slot.Id);

                if (!previousNames.TryGetValue(slot.Id, out var oldName)) continue;
                if (string.Equals(oldName, slot.Name, StringComparison.Ordinal)) continue;

                plan.Renames[oldName] = slot.Name;
            }

            var currentNames = new HashSet<string>(currentSlots.Select(slot => slot.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var pair in previousNames)
            {
                if (currentIds.Contains(pair.Key)) continue;

                // 同じ名前のスロットが今もある場合、その名前へのバインディングは生きたコントロールを指している
                if (currentNames.Contains(pair.Value)) continue;

                plan.RemovedNames.Add(pair.Value);
            }

            if (plan.Renames.Count == 0 && plan.RemovedNames.Count == 0) return true;

            string[] assetPaths;

            try
            {
                assetPaths = Directory.GetFiles("Assets", "*.inputactions", SearchOption.AllDirectories);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[UsefulToolkit.Input] .inputactions の検索に失敗した為、生成を中止しました。\n{exception.Message}");
                return false;
            }

            // 全てのリネームを1回の置換で行う。順に置換すると A→B と B→A の入れ替えで両方が同じ名前になる為
            var renamePattern = plan.Renames.Count == 0 ? null : BuildPathPattern(layoutName, plan.Renames.Keys);
            var removedPattern = plan.RemovedNames.Count == 0 ? null : BuildPathPattern(layoutName, plan.RemovedNames);

            foreach (var rawPath in assetPaths)
            {
                string assetPath = rawPath.Replace('\\', '/');

                if (!TryReadText(assetPath, out string text, out bool hasBom)) return false;

                if (removedPattern != null)
                {
                    foreach (Match match in removedPattern.Matches(text))
                    {
                        plan.DanglingBindings.Add($"{assetPath} : <{layoutName}>/{match.Groups[2].Value}");
                    }
                }

                if (renamePattern == null) continue;

                int count = 0;
                var counts = plan.RewriteCounts;
                var renames = plan.Renames;

                string newText = renamePattern.Replace(text, match =>
                {
                    string oldName = match.Groups[2].Value;

                    count++;
                    counts[oldName] = counts.TryGetValue(oldName, out int current) ? current + 1 : 1;

                    return match.Groups[1].Value + renames[oldName];
                });

                if (count > 0) plan.Patches.Add(new AssetPatch(assetPath, newText, hasBom));
            }

            return true;
        }

        /// <summary>
        /// 計画どおりにファイルを書き換え、再インポートする。
        /// 書き込みに失敗した場合は、そこまでに書き換えたファイルをエラーログに出してfalseを返す。
        /// </summary>
        /// <param name="plan">適用する計画</param>
        /// <returns>全て書き換えられた場合はtrue</returns>
        public static bool Apply(Plan plan)
        {
            var written = new List<string>();

            try
            {
                foreach (var patch in plan.Patches)
                {
                    File.WriteAllText(patch.AssetPath, patch.NewText, new UTF8Encoding(patch.HasBom));
                    written.Add(patch.AssetPath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[UsefulToolkit.Input] バインディングの書き換え中に失敗した為、生成を中止しました。" +
                    $"書き換え済み : {(written.Count == 0 ? "なし" : string.Join(", ", written))}\n{exception.Message}");

                ImportAll(written);
                return false;
            }

            ImportAll(written);
            return true;
        }

        /// <summary>
        /// InputSystem の Input Actions 編集ウィンドウが開いているか。
        /// 開いたまま書き換えると、ウィンドウ側が古い内容で上書き保存する恐れがある。
        /// </summary>
        public static bool IsInputActionsEditorOpen()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                var type = window.GetType();

                if (type.FullName == null) continue;
                if (!type.FullName.StartsWith("UnityEngine.InputSystem", StringComparison.Ordinal)) continue;
                if (type.Name.Contains("InputAction")) return true;
            }

            return false;
        }

        /// <summary>
        /// <c>"path": "&lt;レイアウト名&gt;/名前</c> に一致する正規表現を作る。
        /// 名前の後ろは <c>"</c>(パスの終わり)か <c>/</c>(子コントロール)に限り、前方一致の誤爆を防ぐ。
        /// InputSystem のパスは大文字小文字を区別しない為、大文字小文字を無視して照合する。
        /// グループ1がパスの接頭部、グループ2が名前になる。
        /// </summary>
        /// <param name="layoutName">レイアウト名</param>
        /// <param name="names">照合する名前</param>
        private static Regex BuildPathPattern(string layoutName, IEnumerable<string> names)
        {
            string alternation = string.Join("|", names.Select(Regex.Escape));

            return new Regex(
                $"(\"path\"\\s*:\\s*\"<{Regex.Escape(layoutName)}>/)({alternation})(?=[\"/])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// ファイルをUTF-8として読む。先頭のBOMの有無も返し、書き戻すときに保てるようにする。
        /// </summary>
        /// <param name="assetPath">読むファイル</param>
        /// <param name="text">読んだ内容(BOMを除く)</param>
        /// <param name="hasBom">先頭にBOMがあったか</param>
        private static bool TryReadText(string assetPath, out string text, out bool hasBom)
        {
            text = null;
            hasBom = false;

            try
            {
                byte[] bytes = File.ReadAllBytes(assetPath);

                hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

                int offset = hasBom ? 3 : 0;
                text = new UTF8Encoding(false).GetString(bytes, offset, bytes.Length - offset);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[UsefulToolkit.Input] '{assetPath}' を読み込めない為、生成を中止しました。\n{exception.Message}");
                return false;
            }
        }

        private static void ImportAll(List<string> assetPaths)
        {
            foreach (var assetPath in assetPaths)
            {
                AssetDatabase.ImportAsset(assetPath);
            }
        }
    }
}
