using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UsefulToolkit.GitSupport
{
    /// <summary>
    /// .gitignore に登録するフォルダ階層を表現するノード
    /// </summary>
    [System.Serializable]
    public class GitIgnoreFolderNode
    {
        public string folderName;
        public string folderPath;
        public List<GitIgnoreFolderNode> children = new List<GitIgnoreFolderNode>();
        public bool isExpanded = false;
        public bool isIgnored = false;

        public GitIgnoreFolderNode(string name, string path)
        {
            folderName = name;
            folderPath = path;
        }
    }

    /// <summary>
    /// .gitignore 管理のデータ保持とバックエンド処理を担うクラス
    /// </summary>
    public class GitIgnoreSetting
    {
        public List<GitIgnoreFolderNode> CurrentStructure { get; private set; } = new List<GitIgnoreFolderNode>();
        
        private readonly HashSet<string> _gitIgnoreEntries = new HashSet<string>();
        private string _gitIgnorePath;

        public void Initialize()
        {
            _gitIgnorePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".gitignore");
            Refresh();
        }

        /// <summary>
        /// .gitignore とプロジェクトのフォルダ階層を再スキャンして同期する
        /// </summary>
        public void Refresh()
        {
            LoadGitIgnore();
            ScanCurrentStructure();
        }

        private void LoadGitIgnore()
        {
            _gitIgnoreEntries.Clear();
            if (File.Exists(_gitIgnorePath))
            {
                string[] lines = File.ReadAllLines(_gitIgnorePath);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                    {
                        _gitIgnoreEntries.Add(trimmed);
                    }
                }
            }
        }

        private void ScanCurrentStructure()
        {
            CurrentStructure.Clear();
            if (!Directory.Exists(Application.dataPath)) return;

            string[] dirs = Directory.GetDirectories(Application.dataPath, "*", SearchOption.TopDirectoryOnly);
            foreach (var dir in dirs)
            {
                string folderName = Path.GetFileName(dir);
                CurrentStructure.Add(BuildHierarchyRecursive("Assets/" + folderName));
            }
        }

        private GitIgnoreFolderNode BuildHierarchyRecursive(string relativePath)
        {
            var node = new GitIgnoreFolderNode(Path.GetFileName(relativePath), relativePath);
            string ignorePath = relativePath.EndsWith("/") ? relativePath : relativePath + "/";
            node.isIgnored = _gitIgnoreEntries.Contains(ignorePath);

            string systemPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, relativePath);
            if (Directory.Exists(systemPath))
            {
                foreach (var subDir in Directory.GetDirectories(systemPath))
                {
                    node.children.Add(BuildHierarchyRecursive(relativePath + "/" + Path.GetFileName(subDir)));
                }
            }
            return node;
        }

        /// <summary>
        /// 現在のチェック状態を .gitignore へ統合して保存する
        /// </summary>
        public void Save()
        {
            List<string> lines = new List<string>();
            if (File.Exists(_gitIgnorePath))
            {
                lines = File.ReadAllLines(_gitIgnorePath).ToList();
            }

            HashSet<string> currentUIEntries = new HashSet<string>();
            CollectIgnoredPaths(CurrentStructure, currentUIEntries);

            // 既存の Assets フォルダ指定行のみをクレンジング
            lines.RemoveAll(line => {
                string trimmed = line.Trim();
                return trimmed.StartsWith("Assets/") && (trimmed.EndsWith("/") || !trimmed.Contains("."));
            });

            foreach (var path in currentUIEntries)
            {
                lines.Add(path);
            }

            var finalLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().ToList();

            try
            {
                File.WriteAllLines(_gitIgnorePath, finalLines, System.Text.Encoding.UTF8);
                Debug.Log($"[UsefulToolkit] .gitignore updated. Path: {_gitIgnorePath}");
                Refresh(); // 保存後の状態にリフレッシュ
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UsefulToolkit] Failed to save .gitignore: {e.Message}");
            }
        }

        private void CollectIgnoredPaths(List<GitIgnoreFolderNode> list, HashSet<string> result)
        {
            if (list == null) return;
            foreach (var item in list)
            {
                if (item.isIgnored)
                {
                    string path = item.folderPath.EndsWith("/") ? item.folderPath : item.folderPath + "/";
                    result.Add(path);
                }
                CollectIgnoredPaths(item.children, result);
            }
        }
    }
}