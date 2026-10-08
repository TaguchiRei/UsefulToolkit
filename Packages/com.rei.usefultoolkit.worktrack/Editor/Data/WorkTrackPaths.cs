using System;
using System.IO;
using UnityEditor;

namespace UsefulToolkit.Editor.WorkTrack
{
    /// <summary>
    /// WorkTrackのデータ保存先パスを管理する。保存先はEditorPrefsに保持するため、
    /// プロジェクト単位ではなく利用者(PC)単位で個別に設定できる。
    /// </summary>
    public static class WorkTrackPaths
    {
        private const string SaveDirectoryKey = "UsefulToolkit.WorkTrack.SaveDirectory";

        public static string DefaultSaveDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "UsefulToolkitLogs",
                "WorkTrack");

        public static string SaveDirectory
        {
            get
            {
                var saved = EditorPrefs.GetString(SaveDirectoryKey, string.Empty);
                return string.IsNullOrEmpty(saved) ? DefaultSaveDirectory : saved;
            }
            set => EditorPrefs.SetString(SaveDirectoryKey, value);
        }

        public static bool IsDefault => string.IsNullOrEmpty(EditorPrefs.GetString(SaveDirectoryKey, string.Empty));

        public static void ResetToDefault() => EditorPrefs.DeleteKey(SaveDirectoryKey);

        public static string SessionsFilePath => Path.Combine(SaveDirectory, "Sessions.json");
        public static string CurrentSessionsDirectory => Path.Combine(SaveDirectory, "CurrentSessions");

        /// <summary> 1つのファイルを全Unityで共有する旧形式の記録中セッション。見つけたら確定して削除する </summary>
        public static string LegacyCurrentSessionFilePath => Path.Combine(SaveDirectory, "CurrentSession.json");

        public static string ProjectsFilePath => Path.Combine(SaveDirectory, "Projects.json");
        public static string ExportDirectory => Path.Combine(SaveDirectory, "Export");

        public static string GetCurrentSessionFilePath(string sessionId) =>
            Path.Combine(CurrentSessionsDirectory, sessionId + ".json");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(SaveDirectory);
            Directory.CreateDirectory(CurrentSessionsDirectory);
            Directory.CreateDirectory(ExportDirectory);
        }
    }
}
