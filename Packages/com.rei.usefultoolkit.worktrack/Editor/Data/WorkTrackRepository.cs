using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace UsefulToolkit.Editor.WorkTrack
{
    /// <summary>
    /// WorkTrackのJSONファイル(Sessions/CurrentSessions/Projects)への読み書きを担う。
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

        internal static CurrentSessionRecord LoadCurrentSessionRecord(string sessionId)
        {
            return LoadJson<CurrentSessionRecord>(WorkTrackPaths.GetCurrentSessionFilePath(sessionId));
        }

        /// <summary>
        /// 保存先にある記録中セッションを、他のUnityが記録しているものも含めてすべて読む。
        /// </summary>
        internal static List<CurrentSessionRecord> LoadCurrentSessionRecords()
        {
            var records = new List<CurrentSessionRecord>();
            var directory = WorkTrackPaths.CurrentSessionsDirectory;
            if (!Directory.Exists(directory)) return records;

            foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                if (Path.GetExtension(path) != ".json") continue;

                CurrentSessionRecord record;
                try
                {
                    record = LoadJson<CurrentSessionRecord>(path);
                }
                catch (IOException)
                {
                    // 一覧を取ってから読むまでの間に、他のUnityが削除または引き取った
                    continue;
                }

                if (record?.Session != null && !string.IsNullOrEmpty(record.Session.SessionId)) records.Add(record);
            }

            return records;
        }

        internal static void SaveCurrentSessionRecord(CurrentSessionRecord record)
        {
            SaveJsonAtomic(WorkTrackPaths.GetCurrentSessionFilePath(record.Session.SessionId), record);
        }

        internal static void DeleteCurrentSessionRecord(string sessionId)
        {
            var path = WorkTrackPaths.GetCurrentSessionFilePath(sessionId);
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// 記録中セッションのファイルを削除して引き取る。同じファイルを複数のUnityが同時に引き取ろうとしても、trueを返すのは1つだけ。
        /// </summary>
        internal static bool TryTakeCurrentSessionRecord(string sessionId)
        {
            return TryTakeJson<CurrentSessionRecord>(WorkTrackPaths.GetCurrentSessionFilePath(sessionId), out _);
        }

        internal static bool TryTakeLegacyCurrentSession(out WorkSession session)
        {
            return TryTakeJson(WorkTrackPaths.LegacyCurrentSessionFilePath, out session) && session != null;
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

        /// <summary>
        /// ファイルを一時的な名前へ移してから読み、削除する。移動に成功するのは1つのプロセスだけなので、これを引き取りの排他に使う。
        /// </summary>
        private static bool TryTakeJson<T>(string path, out T data) where T : class
        {
            data = null;
            if (!File.Exists(path)) return false;

            var takenPath = path + ".taking";
            try
            {
                File.Move(path, takenPath);
            }
            catch (IOException)
            {
                // 他のUnityが先に引き取った
                return false;
            }

            data = LoadJson<T>(takenPath);
            File.Delete(takenPath);
            return true;
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
