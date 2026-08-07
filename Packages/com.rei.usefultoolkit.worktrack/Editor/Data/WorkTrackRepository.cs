using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace UsefulToolkit.WorkTrack
{
    /// <summary>
    /// WorkTrackのJSONファイル(Sessions/CurrentSession/Projects)への読み書きを担う。
    /// 複数のUnityプロセスから同じ保存先を共有する可能性があるため、書き込みは一時ファイル経由の
    /// アトミックな置き換えとし、読み書きどちらもファイルロック競合時は短時間リトライする。
    /// 保存内容はWorkTrackCryptoで簡易暗号化しており、テキストエディタで開いても読めない。
    /// </summary>
    public static class WorkTrackRepository
    {
        private const int RetryCount = 5;
        private const int RetryDelayMs = 50;

        public static List<WorkSession> LoadSessions()
        {
            var data = LoadJson<WorkSessionListData>(WorkTrackPaths.SessionsFilePath);
            return data?.Sessions ?? new List<WorkSession>();
        }

        public static void SaveSessions(List<WorkSession> sessions)
        {
            SaveJsonAtomic(WorkTrackPaths.SessionsFilePath, new WorkSessionListData { Sessions = sessions });
        }

        public static WorkSession LoadCurrentSession()
        {
            var path = WorkTrackPaths.CurrentSessionFilePath;
            return File.Exists(path) ? LoadJson<WorkSession>(path) : null;
        }

        public static void SaveCurrentSession(WorkSession session)
        {
            SaveJsonAtomic(WorkTrackPaths.CurrentSessionFilePath, session);
        }

        public static void DeleteCurrentSession()
        {
            var path = WorkTrackPaths.CurrentSessionFilePath;
            if (File.Exists(path)) File.Delete(path);
        }

        public static List<ProjectInfo> LoadProjects()
        {
            var data = LoadJson<ProjectInfoListData>(WorkTrackPaths.ProjectsFilePath);
            return data?.Projects ?? new List<ProjectInfo>();
        }

        public static void SaveProjects(List<ProjectInfo> projects)
        {
            SaveJsonAtomic(WorkTrackPaths.ProjectsFilePath, new ProjectInfoListData { Projects = projects });
        }

        private static T LoadJson<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;

            var raw = ReadWithRetry(path);
            if (string.IsNullOrEmpty(raw)) return null;

            var json = DecryptOrFallback(raw);

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WorkTrack] JSONの読み込みに失敗しました: {path}\n{e.Message}");
                return null;
            }
        }

        private static string DecryptOrFallback(string raw)
        {
            try
            {
                return WorkTrackCrypto.Decrypt(raw);
            }
            catch
            {
                // 暗号化対応前に保存された素のJSON、または破損データの可能性があるため、そのまま読めるか試す
                return raw;
            }
        }

        private static string ReadWithRetry(string path)
        {
            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
                catch (IOException)
                {
                    if (i == RetryCount - 1) throw;
                    Thread.Sleep(RetryDelayMs);
                }
            }

            return null;
        }

        private static void SaveJsonAtomic(string path, object data)
        {
            WorkTrackPaths.EnsureDirectories();

            var json = JsonUtility.ToJson(data, true);
            var encrypted = WorkTrackCrypto.Encrypt(json);
            var tempPath = path + ".tmp";

            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    File.WriteAllText(tempPath, encrypted);

                    if (File.Exists(path)) File.Replace(tempPath, path, null);
                    else File.Move(tempPath, path);

                    return;
                }
                catch (IOException)
                {
                    if (i == RetryCount - 1) throw;
                    Thread.Sleep(RetryDelayMs);
                }
            }
        }
    }
}
