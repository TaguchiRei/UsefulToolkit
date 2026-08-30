#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UsefulToolkit.Editor.ProjectStructure
{
    /// <summary>
    /// 現在のAssetsの状態を、そのまま「あるべき構造」としてテンプレート化する。
    /// 移動元は記録されていないため、生成できるのは「この名前のものはここに置く」というName照合ルールまで。
    /// ExactPath / Folder / Glob のルールは手書き分をそのまま引き継ぐ。
    /// </summary>
    public static class ProjectStructureSnapshot
    {
        /// <summary>
        /// 現在の構造からテンプレートを作る
        /// </summary>
        /// <param name="current">引き継ぎ元のテンプレート</param>
        /// <param name="includeFileRules">ファイル単位のName照合ルールまで作るか</param>
        /// <param name="warnings">ルールを作れなかった項目の一覧</param>
        public static ProjectStructureTemplate Capture(
            ProjectStructureTemplate? current,
            bool includeFileRules,
            out List<string> warnings)
        {
            warnings = new List<string>();

            var template = new ProjectStructureTemplate
            {
                version = current?.version ?? 1,
                description = current?.description ?? string.Empty,
                excludes = new List<string>(current?.excludes ?? new List<string>()),
            };

            var excludes = template.excludes
                .Select(ProjectStructurePath.Normalize)
                .Where(path => path.Length > 0)
                .ToList();

            var allPaths = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
                .Where(path => !ProjectStructurePath.IsHidden(path))
                .Where(path => !excludes.Any(exclude => ProjectStructurePath.IsSameOrUnder(path, exclude)))
                .ToList();

            var folders = allPaths
                .Where(AssetDatabase.IsValidFolder)
                .OrderBy(ProjectStructurePath.Depth)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            template.folders.AddRange(folders);

            // 手書きのルールはそのまま維持し、Name照合の移動ルールだけ作り直す
            if (current != null)
            {
                template.rules.AddRange(current.rules.Where(rule =>
                    rule.MatchMode != StructureMatchMode.Name || rule.Action != StructureActionType.Move));
            }

            // まずフォルダ単位のName照合ルールを作る
            template.rules.AddRange(BuildNameRules(folders, "フォルダ", warnings));

            if (includeFileRules)
            {
                var files = allPaths
                    .Where(path => !AssetDatabase.IsValidFolder(path))
                    // 親フォルダごと移動すれば済むファイルは、ルールを作らない
                    .Where(path => !folders.Contains(ProjectStructurePath.ParentOf(path),
                        StringComparer.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                template.rules.AddRange(BuildNameRules(files, "ファイル", warnings));
            }

            return template;
        }

        /// <summary>
        /// 「この名前のものはこのフォルダの直下に置く」というルールを作る。
        /// 同じ名前が複数の場所にある場合は、移動先を決められないのでルールにしない
        /// </summary>
        private static List<ProjectStructureRule> BuildNameRules(
            List<string> paths,
            string kindLabel,
            List<string> warnings)
        {
            var rules = new List<ProjectStructureRule>();

            var groups = paths.GroupBy(ProjectStructurePath.LeafName, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var members = group.ToList();

                if (members.Count > 1)
                {
                    warnings.Add($"{kindLabel}名 '{group.Key}' が複数の場所にあるため、ルールを作成しませんでした: "
                                 + string.Join(" , ", members));
                    continue;
                }

                string path = members[0];
                string parent = ProjectStructurePath.ParentOf(path);

                if (string.IsNullOrEmpty(parent)) continue;

                rules.Add(new ProjectStructureRule
                {
                    match = nameof(StructureMatchMode.Name),
                    pattern = ProjectStructurePath.LeafName(path),
                    action = nameof(StructureActionType.Move),
                    destination = parent,
                });
            }

            return rules
                .OrderBy(rule => rule.destination, StringComparer.OrdinalIgnoreCase)
                .ThenBy(rule => rule.pattern, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
