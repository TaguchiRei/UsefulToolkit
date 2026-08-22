#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace UsefulToolkit.Editor.ProjectStructure
{
    /// <summary>
    /// 整理処理で扱うアセットパス（Assets/から始まる、区切りが / のパス）のための小道具
    /// </summary>
    public static class ProjectStructurePath
    {
        public const string AssetsRoot = "Assets";

        /// <summary>
        /// 区切り文字を / に統一し、前後の空白と末尾の / を落とす
        /// </summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            string normalized = path.Trim().Replace('\\', '/');
            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return normalized;
        }

        /// <summary>
        /// Assets そのもの、または Assets 配下のパスか
        /// </summary>
        public static bool IsAssetsPath(string path)
        {
            string normalized = Normalize(path);
            return string.Equals(normalized, AssetsRoot, StringComparison.OrdinalIgnoreCase)
                   || normalized.StartsWith(AssetsRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 末尾のファイル名 / フォルダ名
        /// </summary>
        public static string LeafName(string path)
        {
            string normalized = Normalize(path);
            int index = normalized.LastIndexOf('/');
            return index < 0 ? normalized : normalized.Substring(index + 1);
        }

        /// <summary>
        /// 親フォルダのパス。親が無い場合は空文字を返す
        /// </summary>
        public static string ParentOf(string path)
        {
            string normalized = Normalize(path);
            int index = normalized.LastIndexOf('/');
            return index < 0 ? string.Empty : normalized.Substring(0, index);
        }

        /// <summary>
        /// child が parent 自身、または parent 配下にあるか
        /// </summary>
        public static bool IsSameOrUnder(string child, string parent)
        {
            string normalizedChild = Normalize(child);
            string normalizedParent = Normalize(parent);

            if (normalizedParent.Length == 0) return false;

            return string.Equals(normalizedChild, normalizedParent, StringComparison.OrdinalIgnoreCase)
                   || normalizedChild.StartsWith(normalizedParent + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// パスの階層の深さ（Assets が 1）
        /// </summary>
        public static int Depth(string path)
        {
            string normalized = Normalize(path);
            if (normalized.Length == 0) return 0;

            int depth = 1;
            foreach (char c in normalized)
            {
                if (c == '/') depth++;
            }

            return depth;
        }

        /// <summary>
        /// ドットで始まる要素を含むパスか。Unity自身が無視する隠しフォルダなので、常に対象外にする
        /// </summary>
        public static bool IsHidden(string path)
        {
            foreach (string segment in Normalize(path).Split('/'))
            {
                if (segment.StartsWith(".", StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// アセットパスをOS上の絶対パスへ変換する
        /// </summary>
        public static string ToAbsolute(string assetPath)
        {
            // UsefulToolkit.Application 名前空間と衝突しうるので、完全修飾で書いておく
            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)!.FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, Normalize(assetPath)));
        }
    }
}
