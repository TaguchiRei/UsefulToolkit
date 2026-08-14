#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// 整理実行の結果
    /// </summary>
    public class ProjectStructureResult
    {
        public readonly List<string> Logs = new();
        public readonly List<string> Errors = new();

        public int CreatedCount;
        public int MovedCount;
        public int DeletedCount;
        public int CleanedFolderCount;

        /// <summary>移動・削除を一切実行せずに中断したか</summary>
        public bool Aborted;

        public string Summarize()
        {
            if (Aborted)
            {
                return "実行前の検証でエラーが見つかったため、移動と削除は実行していません。";
            }

            return $"フォルダ作成: {CreatedCount} / 移動: {MovedCount} / 削除: {DeletedCount} / 空フォルダ整理: {CleanedFolderCount}";
        }
    }

    /// <summary>
    /// 実行計画を実際のAssetsへ適用する。
    /// ファイル操作は必ずAssetDatabase経由で行い、.metaとGUID参照を壊さないようにする
    /// </summary>
    public static class ProjectStructureApplier
    {
        public static ProjectStructureResult Apply(ProjectStructurePlan plan)
        {
            var result = new ProjectStructureResult();

            if (plan.TemplateErrors.Count > 0)
            {
                result.Aborted = true;
                result.Errors.AddRange(plan.TemplateErrors);
                return result;
            }

            // 1. フォルダ作成。移動先の親を用意する必要があるので、検証より先に行う
            foreach (var operation in plan.Operations.Where(o => o.Type == StructureOperationType.CreateFolder))
            {
                if (EnsureFolder(operation.DestinationPath, result))
                {
                    result.CreatedCount++;
                    result.Logs.Add($"作成: {operation.DestinationPath}");
                }
            }

            var moves = plan.Operations.Where(o => o.Type == StructureOperationType.Move).ToList();
            var deletes = plan.Operations.Where(o => o.Type == StructureOperationType.Delete).ToList();

            // 2. 全ての移動をUnityに検証させる。1件でも通らなければ、何も動かさずに中断する
            foreach (var move in moves)
            {
                EnsureFolder(ProjectStructurePath.ParentOf(move.DestinationPath), result);

                string validation = AssetDatabase.ValidateMoveAsset(move.SourcePath, move.DestinationPath);
                if (!string.IsNullOrEmpty(validation))
                {
                    result.Errors.Add($"{move.SourcePath} → {move.DestinationPath} : {validation}");
                }
            }

            if (result.Errors.Count > 0)
            {
                result.Aborted = true;
                return result;
            }

            // 3. 実行
            var touchedFolders = new List<string>();
            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var move in moves)
                {
                    string error = AssetDatabase.MoveAsset(move.SourcePath, move.DestinationPath);
                    if (string.IsNullOrEmpty(error))
                    {
                        result.MovedCount++;
                        result.Logs.Add($"移動: {move.SourcePath} → {move.DestinationPath}");
                        touchedFolders.Add(ProjectStructurePath.ParentOf(move.SourcePath));
                    }
                    else
                    {
                        result.Errors.Add($"{move.SourcePath} → {move.DestinationPath} : {error}");
                    }
                }

                foreach (var delete in deletes)
                {
                    // 完全削除ではなくOSのゴミ箱へ送り、取り消せる状態を残す
                    if (AssetDatabase.MoveAssetToTrash(delete.SourcePath))
                    {
                        result.DeletedCount++;
                        result.Logs.Add($"削除（ゴミ箱へ）: {delete.SourcePath}");
                        touchedFolders.Add(ProjectStructurePath.ParentOf(delete.SourcePath));
                    }
                    else
                    {
                        result.Errors.Add($"削除に失敗しました: {delete.SourcePath}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            // 4. 移動元に残った空フォルダを片付ける
            result.CleanedFolderCount = CleanupEmptyFolders(touchedFolders, plan.ProtectedFolders, result);

            return result;
        }

        /// <summary>
        /// 指定フォルダを親から順に作成する。既に存在する場合は何もしない
        /// </summary>
        private static bool EnsureFolder(string folderPath, ProjectStructureResult result)
        {
            string normalized = ProjectStructurePath.Normalize(folderPath);

            if (string.IsNullOrEmpty(normalized)) return false;
            if (!ProjectStructurePath.IsAssetsPath(normalized)) return false;
            if (AssetDatabase.IsValidFolder(normalized)) return false;

            string parent = ProjectStructurePath.ParentOf(normalized);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent, result);
            }

            string guid = AssetDatabase.CreateFolder(parent, ProjectStructurePath.LeafName(normalized));
            if (string.IsNullOrEmpty(guid))
            {
                result.Errors.Add($"フォルダの作成に失敗しました: {normalized}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 移動・削除によって空になったフォルダを、下から順に取り除く
        /// </summary>
        private static int CleanupEmptyFolders(
            List<string> candidates,
            List<string> protectedFolders,
            ProjectStructureResult result)
        {
            int cleaned = 0;

            var ordered = candidates
                .Select(ProjectStructurePath.Normalize)
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(ProjectStructurePath.Depth)
                .ToList();

            foreach (string candidate in ordered)
            {
                string current = candidate;

                while (ProjectStructurePath.IsAssetsPath(current)
                       && !string.Equals(current, ProjectStructurePath.AssetsRoot, StringComparison.OrdinalIgnoreCase))
                {
                    // テンプレートが要求しているフォルダは、空でも残す
                    if (protectedFolders.Contains(current, StringComparer.OrdinalIgnoreCase)) break;
                    if (!AssetDatabase.IsValidFolder(current)) break;
                    if (!IsPhysicallyEmpty(current)) break;

                    string parent = ProjectStructurePath.ParentOf(current);

                    if (AssetDatabase.DeleteAsset(current))
                    {
                        cleaned++;
                        result.Logs.Add($"空フォルダを削除: {current}");
                    }
                    else
                    {
                        result.Errors.Add($"空フォルダの削除に失敗しました: {current}");
                        break;
                    }

                    current = parent;
                }
            }

            if (cleaned > 0)
            {
                AssetDatabase.Refresh();
            }

            return cleaned;
        }

        private static bool IsPhysicallyEmpty(string folderPath)
        {
            try
            {
                string absolute = ProjectStructurePath.ToAbsolute(folderPath);
                return Directory.Exists(absolute) && !Directory.EnumerateFileSystemEntries(absolute).Any();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UsefulToolkit] フォルダの確認に失敗しました: {folderPath} ({exception.Message})");
                return false;
            }
        }
    }
}
