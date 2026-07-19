using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.GitSupport
{
    /// <summary>
    /// Gitの.gitignore設定を利用して、指定したファイルやディレクトリが
    /// 無視対象かどうかを判定するためのサポートクラスです。
    /// パフォーマンスのため、無視ファイル一覧をキャッシュして利用します。
    /// </summary>
    public static class GitIgnoreSupport
    {
        private static readonly HashSet<string> IgnoredPaths = new HashSet<string>();
        private static bool isRefreshing;
        private static bool cacheInitialized;

        private static readonly string ProjectRoot =
            Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);

        /// <summary>
        /// 指定したパスがGitのignore対象か判定します（キャッシュ参照のみ、プロセス起動なし）。
        /// </summary>
        public static bool IsIgnored(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (!cacheInitialized)
            {
                RefreshCacheSync();
            }

            string relativePath = ToRelativePath(path);
            if (string.IsNullOrEmpty(relativePath))
            {
                return false;
            }

            return IgnoredPaths.Contains(relativePath);
        }

        private static string ToRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string full;
            try
            {
                full = Path.GetFullPath(path).Replace('\\', '/');
            }
            catch
            {
                // 不正なパス文字列の場合は判定不能として扱う
                return string.Empty;
            }

            string root = ProjectRoot.Replace('\\', '/');
            if (full.StartsWith(root + "/"))
                return full.Substring(root.Length + 1);

            return path.Replace('\\', '/');
        }

        /// <summary>
        /// 無視ファイル一覧のキャッシュを非同期で再構築します。
        /// </summary>
        public static void RefreshCacheAsync(Action onComplete = null)
        {
            if (isRefreshing)
                return;

            isRefreshing = true;

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var result = RunGitCommand("ls-files --others --ignored --exclude-standard --directory");
                    var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                    lock (IgnoredPaths)
                    {
                        IgnoredPaths.Clear();
                        foreach (var line in lines)
                        {
                            IgnoredPaths.Add(line.TrimEnd('/'));
                        }
                    }

                    cacheInitialized = true;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[GitIgnoreSupport] キャッシュ更新に失敗しました: {e.Message}");
                }
                finally
                {
                    isRefreshing = false;

                    // メインスレッドに処理を戻してからコールバックを呼ぶ
                    if (onComplete != null)
                    {
                        EditorApplication.delayCall += () => onComplete();
                    }
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }

        private static void RefreshCacheSync()
        {
            if (isRefreshing)
                return;

            try
            {
                var result = RunGitCommand("ls-files --others --ignored --exclude-standard --directory");
                var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                lock (IgnoredPaths)
                {
                    IgnoredPaths.Clear();
                    foreach (var line in lines)
                    {
                        IgnoredPaths.Add(line.TrimEnd('/'));
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[GitIgnoreSupport] キャッシュ初期化に失敗しました: {e.Message}");
            }
            finally
            {
                cacheInitialized = true;
            }
        }

        private static string RunGitCommand(string arguments)
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "git";
                process.StartInfo.Arguments = arguments;
                process.StartInfo.WorkingDirectory = ProjectRoot;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return output;
            }
        }
    }
}