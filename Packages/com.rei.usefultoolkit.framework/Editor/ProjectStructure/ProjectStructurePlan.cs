#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// 実行計画に含まれる操作の種別
    /// </summary>
    public enum StructureOperationType
    {
        CreateFolder = 0,
        Move = 1,
        Delete = 2,
        Skip = 3,
    }

    /// <summary>
    /// 実行計画1件分
    /// </summary>
    public class ProjectStructureOperation
    {
        public StructureOperationType Type;
        public string SourcePath = string.Empty;
        public string DestinationPath = string.Empty;

        /// <summary>適用したルールの説明、またはスキップした理由</summary>
        public string Reason = string.Empty;

        /// <summary>ユーザーが目視で確認すべき項目か</summary>
        public bool IsWarning;
    }

    /// <summary>
    /// テンプレートと現在のAssetsを突き合わせた結果の実行計画。
    /// このクラスを作る処理には副作用が無いので、そのままドライランとして使える
    /// </summary>
    public class ProjectStructurePlan
    {
        public readonly List<ProjectStructureOperation> Operations = new();

        /// <summary>テンプレート自体の記述ミス。1件でもあれば実行させない</summary>
        public readonly List<string> TemplateErrors = new();

        /// <summary>掃除の対象外にするフォルダ（テンプレートが作成を要求しているフォルダ）</summary>
        public readonly List<string> ProtectedFolders = new();

        /// <summary>既に正しい場所にあった対象の数</summary>
        public int AlreadyInPlaceCount;

        public int CreateCount => Operations.Count(o => o.Type == StructureOperationType.CreateFolder);
        public int MoveCount => Operations.Count(o => o.Type == StructureOperationType.Move);
        public int DeleteCount => Operations.Count(o => o.Type == StructureOperationType.Delete);
        public int SkipCount => Operations.Count(o => o.Type == StructureOperationType.Skip);

        public bool HasWork => CreateCount + MoveCount + DeleteCount > 0;
    }

    /// <summary>
    /// テンプレートから実行計画を組み立てる。ここでは一切ファイルを触らない
    /// </summary>
    public static class ProjectStructurePlanner
    {
        /// <summary>照合の優先順位。先に確定した対象は後段では再評価しない</summary>
        private static readonly StructureMatchMode[] EvaluationOrder =
        {
            StructureMatchMode.ExactPath,
            StructureMatchMode.Folder,
            StructureMatchMode.Glob,
            StructureMatchMode.Name,
        };

        private static readonly Dictionary<string, Regex> GlobCache = new();

        public static ProjectStructurePlan Build(ProjectStructureTemplate? template)
        {
            var plan = new ProjectStructurePlan();

            if (template == null)
            {
                plan.TemplateErrors.Add("テンプレートが読み込まれていません。");
                return plan;
            }

            ValidateTemplate(template, plan);
            if (plan.TemplateErrors.Count > 0) return plan;

            var excludes = template.excludes
                .Select(ProjectStructurePath.Normalize)
                .Where(path => path.Length > 0)
                .ToList();

            plan.ProtectedFolders.AddRange(template.folders.Select(ProjectStructurePath.Normalize));

            // Unityが認識しているアセット（.metaは含まれない）を対象にする
            var allPaths = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
                .Where(path => !ProjectStructurePath.IsHidden(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var moves = new List<ProjectStructureOperation>();
            var deletes = new List<ProjectStructureOperation>();
            var skips = new List<ProjectStructureOperation>();

            // 確定済みの対象。配下のアセットもまとめて確定したものとして扱う
            var claimed = new List<string>();
            var plannedDestinations = new List<string>();

            // 実行時にその場所が空になる対象。移動・削除するものだけが該当する
            var vacated = new List<string>();

            foreach (var mode in EvaluationOrder)
            {
                foreach (var rule in template.rules.Where(r => r.MatchMode == mode))
                {
                    var targets = FindTargets(rule, allPaths);
                    if (targets.Count == 0) continue;

                    // 名前照合で複数見つかった場合、どれが正解か決められないので触らない
                    if (mode == StructureMatchMode.Name && targets.Count > 1)
                    {
                        skips.Add(new ProjectStructureOperation
                        {
                            Type = StructureOperationType.Skip,
                            SourcePath = rule.pattern,
                            Reason = $"名前 '{rule.pattern}' に一致する対象が{targets.Count}件あるため自動では移動できません: "
                                     + string.Join(" , ", targets),
                            IsWarning = true,
                        });
                        continue;
                    }

                    foreach (string target in targets)
                    {
                        if (IsExcluded(target, excludes)) continue;
                        if (IsClaimed(target, claimed)) continue;

                        if (rule.Action == StructureActionType.Delete)
                        {
                            deletes.Add(new ProjectStructureOperation
                            {
                                Type = StructureOperationType.Delete,
                                SourcePath = target,
                                Reason = rule.Describe(),
                            });
                            claimed.Add(target);
                            vacated.Add(target);
                            continue;
                        }

                        string destinationFolder = ProjectStructurePath.Normalize(rule.destination);
                        string destinationPath = $"{destinationFolder}/{ProjectStructurePath.LeafName(target)}";

                        // 既に正しい場所にある
                        if (string.Equals(destinationPath, target, StringComparison.OrdinalIgnoreCase))
                        {
                            plan.AlreadyInPlaceCount++;
                            claimed.Add(target);
                            continue;
                        }

                        // 自分自身の配下へは移動できない
                        if (ProjectStructurePath.IsSameOrUnder(destinationFolder, target))
                        {
                            skips.Add(new ProjectStructureOperation
                            {
                                Type = StructureOperationType.Skip,
                                SourcePath = target,
                                DestinationPath = destinationPath,
                                Reason = "移動先が自分自身の配下のため移動できません。",
                                IsWarning = true,
                            });
                            claimed.Add(target);
                            continue;
                        }

                        if (DestinationOccupied(destinationPath, allPaths, vacated, plannedDestinations))
                        {
                            skips.Add(new ProjectStructureOperation
                            {
                                Type = StructureOperationType.Skip,
                                SourcePath = target,
                                DestinationPath = destinationPath,
                                Reason = "移動先に同名のアセットが既に存在するため、上書きせずスキップします。",
                                IsWarning = true,
                            });
                            claimed.Add(target);
                            continue;
                        }

                        moves.Add(new ProjectStructureOperation
                        {
                            Type = StructureOperationType.Move,
                            SourcePath = target,
                            DestinationPath = destinationPath,
                            Reason = rule.Describe(),
                        });
                        claimed.Add(target);
                        vacated.Add(target);
                        plannedDestinations.Add(destinationPath);
                    }
                }
            }

            moves = OrderMoves(moves);

            var creates = BuildFolderCreations(template, moves, allPaths);

            plan.Operations.AddRange(creates);
            plan.Operations.AddRange(moves);
            plan.Operations.AddRange(deletes);
            plan.Operations.AddRange(skips);

            return plan;
        }

        private static void ValidateTemplate(ProjectStructureTemplate template, ProjectStructurePlan plan)
        {
            for (int i = 0; i < template.rules.Count; i++)
            {
                if (!template.rules[i].Validate(out string error))
                {
                    plan.TemplateErrors.Add($"rules[{i}] : {error}");
                }
            }

            foreach (string folder in template.folders)
            {
                if (!ProjectStructurePath.IsAssetsPath(folder))
                {
                    plan.TemplateErrors.Add($"folders : Assets/ から始まるパスで指定してください: {folder}");
                }
            }

            foreach (string exclude in template.excludes)
            {
                if (!ProjectStructurePath.IsAssetsPath(exclude))
                {
                    plan.TemplateErrors.Add($"excludes : Assets/ から始まるパスで指定してください: {exclude}");
                }
            }
        }

        /// <summary>
        /// 作成が必要なフォルダを、親から順に並べて返す
        /// </summary>
        private static List<ProjectStructureOperation> BuildFolderCreations(
            ProjectStructureTemplate template,
            List<ProjectStructureOperation> moves,
            List<string> allPaths)
        {
            var wanted = new List<string>();
            wanted.AddRange(template.folders.Select(ProjectStructurePath.Normalize));

            // 移動先の親フォルダは、移動を実行する前に存在している必要がある
            foreach (var move in moves)
            {
                wanted.Add(ProjectStructurePath.ParentOf(move.DestinationPath));
            }

            // 移動によって出来上がるフォルダは、先に作ると移動が失敗するので作成対象から外す
            var moveDestinations = moves
                .Select(move => move.DestinationPath)
                .ToList();

            var required = new List<string>();
            foreach (string folder in wanted)
            {
                for (string current = folder;
                     ProjectStructurePath.IsAssetsPath(current);
                     current = ProjectStructurePath.ParentOf(current))
                {
                    if (!required.Contains(current, StringComparer.OrdinalIgnoreCase))
                    {
                        required.Add(current);
                    }

                    if (string.Equals(current, ProjectStructurePath.AssetsRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }

            return required
                .Where(folder => !string.Equals(folder, ProjectStructurePath.AssetsRoot,
                    StringComparison.OrdinalIgnoreCase))
                .Where(folder => !AssetDatabase.IsValidFolder(folder))
                .Where(folder => !allPaths.Contains(folder, StringComparer.OrdinalIgnoreCase))
                .Where(folder => !moveDestinations.Any(destination =>
                    ProjectStructurePath.IsSameOrUnder(folder, destination)))
                .OrderBy(ProjectStructurePath.Depth)
                .ThenBy(folder => folder, StringComparer.OrdinalIgnoreCase)
                .Select(folder => new ProjectStructureOperation
                {
                    Type = StructureOperationType.CreateFolder,
                    DestinationPath = folder,
                    Reason = "テンプレートで定義されたフォルダ",
                })
                .ToList();
        }

        private static List<string> FindTargets(ProjectStructureRule rule, List<string> allPaths)
        {
            string pattern = rule.MatchMode == StructureMatchMode.Name
                ? rule.pattern.Trim()
                : ProjectStructurePath.Normalize(rule.pattern);

            switch (rule.MatchMode)
            {
                case StructureMatchMode.ExactPath:
                    return allPaths
                        .Where(path => string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                case StructureMatchMode.Folder:
                    return allPaths
                        .Where(path => string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase))
                        .Where(AssetDatabase.IsValidFolder)
                        .ToList();

                case StructureMatchMode.Glob:
                    var regex = GetGlobRegex(pattern);
                    return allPaths.Where(path => regex.IsMatch(path)).ToList();

                case StructureMatchMode.Name:
                    return allPaths
                        .Where(path => string.Equals(ProjectStructurePath.LeafName(path), pattern,
                            StringComparison.OrdinalIgnoreCase))
                        .ToList();

                default:
                    return new List<string>();
            }
        }

        /// <summary>
        /// 移動を実行する順序を決める。
        /// 基本は浅い移動先から処理し、移動先がまだ別の移動元に占有されている場合はその移動を先に回す
        /// </summary>
        private static List<ProjectStructureOperation> OrderMoves(List<ProjectStructureOperation> moves)
        {
            var remaining = moves
                .OrderBy(op => ProjectStructurePath.Depth(op.DestinationPath))
                .ThenBy(op => op.DestinationPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ordered = new List<ProjectStructureOperation>(remaining.Count);

            while (remaining.Count > 0)
            {
                var next = remaining.FirstOrDefault(candidate =>
                    !remaining.Any(other => !ReferenceEquals(other, candidate)
                                            && ProjectStructurePath.IsSameOrUnder(candidate.DestinationPath,
                                                other.SourcePath)));

                // 循環していてどれも先に置けない場合は、残りをそのまま積む（実行前の検証で弾かれる）
                if (next == null)
                {
                    ordered.AddRange(remaining);
                    break;
                }

                ordered.Add(next);
                remaining.Remove(next);
            }

            return ordered;
        }

        /// <summary>
        /// 移動先が既に埋まっているか。
        /// ただし、その場所自体が別の移動・削除で空になる場合は、埋まっていないものとして扱う
        /// </summary>
        private static bool DestinationOccupied(
            string destinationPath,
            List<string> allPaths,
            List<string> vacated,
            List<string> plannedDestinations)
        {
            if (plannedDestinations.Contains(destinationPath, StringComparer.OrdinalIgnoreCase)) return true;

            if (!allPaths.Contains(destinationPath, StringComparer.OrdinalIgnoreCase)) return false;

            // 移動先そのものだけでなく、それを含むフォルダごと移動する場合も空くと判断する
            return !vacated.Any(path => ProjectStructurePath.IsSameOrUnder(destinationPath, path));
        }

        private static bool IsExcluded(string path, List<string> excludes)
        {
            return excludes.Any(exclude => ProjectStructurePath.IsSameOrUnder(path, exclude));
        }

        private static bool IsClaimed(string path, List<string> claimed)
        {
            return claimed.Any(claim => ProjectStructurePath.IsSameOrUnder(path, claim));
        }

        /// <summary>
        /// グロブを正規表現へ変換する。* は階層を跨がず、** は跨ぐ
        /// </summary>
        private static Regex GetGlobRegex(string pattern)
        {
            if (GlobCache.TryGetValue(pattern, out var cached)) return cached;

            var builder = new StringBuilder("^");
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '*')
                {
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        builder.Append(".*");
                        i++;
                    }
                    else
                    {
                        builder.Append("[^/]*");
                    }
                }
                else if (c == '?')
                {
                    builder.Append("[^/]");
                }
                else
                {
                    builder.Append(Regex.Escape(c.ToString()));
                }
            }

            builder.Append('$');

            var regex = new Regex(builder.ToString(), RegexOptions.IgnoreCase);
            GlobCache[pattern] = regex;
            return regex;
        }
    }
}
