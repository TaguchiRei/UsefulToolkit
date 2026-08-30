#nullable enable

using System;
using System.Collections.Generic;

namespace UsefulToolkit.Editor.ProjectStructure
{
    /// <summary>
    /// 整理ルールの照合方法。この列挙体の並び順がそのまま評価の優先順位になる。
    /// 先に確定した対象は、後段のルールでは再評価されない。
    /// </summary>
    public enum StructureMatchMode
    {
        /// <summary>Assetsからの完全パスで照合する。最も安全なので既定はこれ</summary>
        ExactPath = 0,

        /// <summary>フォルダの完全パスで照合し、フォルダごと移動する</summary>
        Folder = 1,

        /// <summary>ワイルドカード（* は階層を跨がない / ** は階層を跨ぐ）で照合する</summary>
        Glob = 2,

        /// <summary>Assets以下のどこにあっても、末尾の名前が一致すれば照合する。複数見つかった場合は誤爆を避けて中断する</summary>
        Name = 3,
    }

    /// <summary>
    /// 対象に対して行う操作
    /// </summary>
    public enum StructureActionType
    {
        /// <summary>destinationで指定したフォルダの直下へ移動する</summary>
        Move = 0,

        /// <summary>OSのゴミ箱へ送る（完全削除はしない）</summary>
        Delete = 1,
    }

    /// <summary>
    /// 整理ルール1件分。JsonUtilityで読み書きするため、列挙体は文字列で保持する
    /// </summary>
    [Serializable]
    public class ProjectStructureRule
    {
        public string match = nameof(StructureMatchMode.ExactPath);
        public string pattern = string.Empty;
        public string action = nameof(StructureActionType.Move);
        public string destination = string.Empty;

        public StructureMatchMode MatchMode =>
            Enum.TryParse(match, true, out StructureMatchMode mode) ? mode : StructureMatchMode.ExactPath;

        public StructureActionType Action =>
            Enum.TryParse(action, true, out StructureActionType actionType) ? actionType : StructureActionType.Move;

        /// <summary>
        /// ルールの記述ミスを検出する。実行前に必ず通す。
        /// </summary>
        public bool Validate(out string error)
        {
            if (!Enum.TryParse(match, true, out StructureMatchMode _))
            {
                error = $"match の値 '{match}' が不正です。ExactPath / Folder / Glob / Name のいずれかを指定してください。";
                return false;
            }

            if (!Enum.TryParse(action, true, out StructureActionType actionType))
            {
                error = $"action の値 '{action}' が不正です。Move / Delete のいずれかを指定してください。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(pattern))
            {
                error = "pattern が空です。";
                return false;
            }

            // Name照合以外はAssets/始まりのパスを要求する
            if (MatchMode != StructureMatchMode.Name && !ProjectStructurePath.IsAssetsPath(pattern))
            {
                error = $"pattern '{pattern}' は Assets/ から始まるパスで指定してください。";
                return false;
            }

            if (MatchMode == StructureMatchMode.Name && pattern.Contains("/"))
            {
                error = $"Name照合の pattern '{pattern}' にはフォルダ区切りを含められません。名前のみを指定してください。";
                return false;
            }

            if (actionType == StructureActionType.Move)
            {
                if (string.IsNullOrWhiteSpace(destination))
                {
                    error = "action が Move の場合は destination が必須です。";
                    return false;
                }

                if (!ProjectStructurePath.IsAssetsPath(destination))
                {
                    error = $"destination '{destination}' は Assets/ から始まるパスで指定してください。";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// プレビュー表示用のルール名
        /// </summary>
        public string Describe()
        {
            return Action == StructureActionType.Delete
                ? $"{MatchMode}: {pattern} → 削除"
                : $"{MatchMode}: {pattern} → {destination}";
        }
    }

    /// <summary>
    /// Assets以下のあるべき構造を表すテンプレート。ProjectSettings配下のJSONとして保存され、
    /// プロジェクト間でそのまま持ち運べるようにしてある
    /// </summary>
    [Serializable]
    public class ProjectStructureTemplate
    {
        public int version = 1;

        /// <summary>JSONを直接開いた人向けのメモ。処理には影響しない</summary>
        public string description = string.Empty;

        /// <summary>
        /// 走査・移動の対象外にするパス。ここで指定した場所は「移動元」にならないが、
        /// 「移動先」としては使える（例：Assets/Pluginsの中身は動かさないが、そこへ移動はする）
        /// </summary>
        public List<string> excludes = new();

        /// <summary>存在しなければ作成する空フォルダ</summary>
        public List<string> folders = new();

        /// <summary>整理ルール</summary>
        public List<ProjectStructureRule> rules = new();
    }
}
