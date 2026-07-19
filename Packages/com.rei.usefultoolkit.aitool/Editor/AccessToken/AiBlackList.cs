using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UsefulToolkit.Framework;

namespace UsefulToolkit.Ai
{
    [Serializable]
    public sealed class AIBlackList
    {
        private static AIBlackList _instance;
        public static AIBlackList Instance => _instance ??= Load();

        public string[] BlacklistedPaths = Array.Empty<string>();
        public string[] SharedPaths = Array.Empty<string>();

        private const string SavePath = "UserSettings/UsefulToolkit.AIBlackList.json";
        private const string SharedSavePath = "Assets/Data/UsefulToolkit/UsefulToolkit.AIBlackList.json";

        /// <summary>
        /// 指定されたパス（アセットパスまたはGameObjectパス）がブラックリストに含まれているか判定します。
        /// </summary>
        public bool IsBlacklisted(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            path = Normalize(path);

            var allPaths = BlacklistedPaths.Concat(SharedPaths);

            foreach (var blacklistedPath in allPaths)
            {
                if (string.IsNullOrWhiteSpace(blacklistedPath)) continue;

                var normalizedEntry = Normalize(blacklistedPath);

                // 完全一致
                if (path.Equals(normalizedEntry, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 階層下判定 (フォルダ配下、またはGameObjectの子要素)
                if (path.StartsWith(normalizedEntry + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public void ToggleShare(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = Normalize(path);

            if (SharedPaths.Any(p => Normalize(p).Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                // 共有 -> ローカルへ移動
                RemovePath(path, true);
                AddPath(path, false);
            }
            else if (BlacklistedPaths.Any(p => Normalize(p).Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                // ローカル -> 共有へ移動
                RemovePath(path, false);
                AddPath(path, true);
            }
        }

        public void Save()
        {
            // ローカル保存時はSharedPathsを空にして保存（現在の保存形式を維持しつつ分離）
            var data = new AIBlackList { BlacklistedPaths = this.BlacklistedPaths };
            var json = JsonUtility.ToJson(data, true);
            FileGenerator.WriteFile(SavePath, json);
        }

        public void SaveShared()
        {
            // 共有保存時はBlacklistedPathsを空にして保存
            var data = new AIBlackList { SharedPaths = this.SharedPaths };
            var json = JsonUtility.ToJson(data, true);
            FileGenerator.WriteFile(SharedSavePath, json);
        }

        public void AddPath(string path, bool shared = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = Normalize(path);

            if (shared)
            {
                if (SharedPaths.Any(p => Normalize(p).Equals(path, StringComparison.OrdinalIgnoreCase))) return;
                var list = SharedPaths.ToList();
                list.Add(path);
                SharedPaths = list.ToArray();
                SaveShared();
            }
            else
            {
                if (BlacklistedPaths.Any(p => Normalize(p).Equals(path, StringComparison.OrdinalIgnoreCase))) return;
                var list = BlacklistedPaths.ToList();
                list.Add(path);
                BlacklistedPaths = list.ToArray();
                Save();
            }
        }

        public void RemovePath(string path, bool shared = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = Normalize(path);

            // AI保護パスは削除不可
            if (path.Equals("Assets/Code/Editor/AiChat", StringComparison.OrdinalIgnoreCase)) return;

            if (shared)
            {
                var list = SharedPaths.ToList();
                if (list.RemoveAll(p => Normalize(p).Equals(path, StringComparison.OrdinalIgnoreCase)) > 0)
                {
                    SharedPaths = list.ToArray();
                    SaveShared();
                }
            }
            else
            {
                var list = BlacklistedPaths.ToList();
                if (list.RemoveAll(p => Normalize(p).Equals(path, StringComparison.OrdinalIgnoreCase)) > 0)
                {
                    BlacklistedPaths = list.ToArray();
                    Save();
                }
            }
        }

        public static AIBlackList Load()
        {
            var instance = new AIBlackList();

            // ローカル読み込み
            if (File.Exists(SavePath))
            {
                try
                {
                    var json = File.ReadAllText(SavePath);
                    var local = JsonUtility.FromJson<AIBlackList>(json);
                    if (local != null) instance.BlacklistedPaths = local.BlacklistedPaths ?? Array.Empty<string>();
                }
                catch
                {
                }
            }

            // 共有読み込み
            if (File.Exists(SharedSavePath))
            {
                try
                {
                    var json = File.ReadAllText(SharedSavePath);
                    var shared = JsonUtility.FromJson<AIBlackList>(json);
                    if (shared != null) instance.SharedPaths = shared.SharedPaths ?? Array.Empty<string>();
                }
                catch
                {
                }
            }

            // デフォルト値（自己保護）の確認と追加
            bool hasAiChat = instance.BlacklistedPaths.Contains("Assets/Code/Editor/AiChat") ||
                             instance.SharedPaths.Contains("Assets/Code/Editor/AiChat");

            if (!hasAiChat)
            {
                var list = instance.BlacklistedPaths.ToList();
                list.Add("Assets/Code/Editor/AiChat");
                instance.BlacklistedPaths = list.ToArray();
                instance.Save();
            }

            return instance;
        }

        public static void Reload()
        {
            _instance = Load();
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }
    }
}