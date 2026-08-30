using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Editor.WorkTrack
{
    /// <summary>
    /// Unity Editorの起動・終了に合わせて作業セッションを自動記録する。
    /// CurrentSession.jsonへの書き込みはEditorの起動時と終了時のみで、途中経過は書き戻さない。
    /// クラッシュ等で終了処理が走らなかった場合は終了時刻が不明になり、開始時刻を終了時刻として作業時間0で確定する。
    ///
    /// [InitializeOnLoad]の静的コンストラクタはスクリプトのドメインリロード毎に再実行されるため、
    /// SessionState(ドメインリロードをまたいでEditorプロセス内で保持される)で
    /// 「本当にEditorが起動した直後か、単なる再コンパイルによる再実行か」を区別する。
    /// </summary>
    [InitializeOnLoad]
    public static class WorkSessionRecorder
    {
        private const string ActiveSessionIdKey = "UsefulToolkit.WorkTrack.ActiveSessionId";

        private static WorkSession _currentSession;

        static WorkSessionRecorder()
        {
            var activeSessionId = SessionState.GetString(ActiveSessionIdKey, string.Empty);
            if (!string.IsNullOrEmpty(activeSessionId))
            {
                _currentSession = WorkTrackRepository.LoadCurrentSession();
            }

            if (_currentSession == null)
            {
                RecoverOrphanedSession();
                StartNewSession();
                SessionState.SetString(ActiveSessionIdKey, _currentSession.SessionId);
            }

            EditorApplication.quitting += OnQuitting;
        }

        private static void RecoverOrphanedSession()
        {
            var orphaned = WorkTrackRepository.LoadCurrentSession();
            if (orphaned == null) return;

            // クラッシュ等で終了処理が走らなかったセッション。正確な終了時刻は追えないため、開始時刻を終了時刻として確定する。
            FinalizeSession(orphaned, orphaned.StartTime);
            WorkTrackRepository.DeleteCurrentSession();
        }

        private static void StartNewSession()
        {
            var projectPath = GetProjectPath();
            var projectName = GetProjectName(projectPath);

            RegisterProjectIfNeeded(projectName, projectPath);

            var now = NowIso();
            _currentSession = new WorkSession
            {
                SessionId = Guid.NewGuid().ToString(),
                ProjectName = projectName,
                ProjectPath = projectPath,
                StartTime = now,
                EndTime = string.Empty,
                Memo = string.Empty,
                Tags = Array.Empty<string>(),
                CreatedAt = now,
                UpdatedAt = now
            };

            WorkTrackRepository.SaveCurrentSession(_currentSession);
        }

        private static void OnQuitting()
        {
            if (_currentSession == null) return;

            FinalizeSession(_currentSession, NowIso());
            WorkTrackRepository.DeleteCurrentSession();
            SessionState.EraseString(ActiveSessionIdKey);
            _currentSession = null;
        }

        private static void FinalizeSession(WorkSession session, string endTime)
        {
            session.EndTime = endTime;
            session.UpdatedAt = endTime;

            var sessions = WorkTrackRepository.LoadSessions();
            sessions.Add(session);
            WorkTrackRepository.SaveSessions(sessions);
        }

        private static void RegisterProjectIfNeeded(string projectName, string projectPath)
        {
            var projects = WorkTrackRepository.LoadProjects();
            if (projects.Exists(p => p.ProjectPath == projectPath)) return;

            projects.Add(new ProjectInfo
            {
                ProjectName = projectName,
                ProjectPath = projectPath,
                CreatedAt = NowIso()
            });
            WorkTrackRepository.SaveProjects(projects);
        }

        private static string GetProjectPath() => Directory.GetParent(Application.dataPath).FullName;

        private static string GetProjectName(string projectPath) => new DirectoryInfo(projectPath).Name;

        private static string NowIso() => DateTime.UtcNow.ToString("o");
    }
}
