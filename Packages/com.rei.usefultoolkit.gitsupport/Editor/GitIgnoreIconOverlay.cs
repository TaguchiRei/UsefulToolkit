using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UsefulToolkit.GitSupport
{
    [InitializeOnLoad]
    public static class GitIgnoreIconOverlay
    {
        private static Texture2D ignoreIcon;
        private static double lastRefreshTime;
        private const double RefreshIntervalSeconds = 3.0;

        static GitIgnoreIconOverlay()
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(GitIgnoreIconOverlay).Assembly);

            if (packageInfo != null)
            {
                string path = $"{packageInfo.assetPath}/Editor/Texture/gitignore.png";
                ignoreIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            // 起動時に一度キャッシュを構築（非同期）
            GitIgnoreSupport.RefreshCacheAsync();

            EditorApplication.projectWindowItemOnGUI += OnProjectWindowGUI;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private static void OnProjectChanged()
        {
            // ファイル追加・削除など変更があったときだけ再取得（頻度制限つき）
            if (EditorApplication.timeSinceStartup - lastRefreshTime < RefreshIntervalSeconds)
                return;

            lastRefreshTime = EditorApplication.timeSinceStartup;
            GitIgnoreSupport.RefreshCacheAsync(EditorApplication.RepaintProjectWindow);
        }

        private static void OnProjectWindowGUI(string guid, Rect rect)
        {
            // Repaint時のみ処理（Layout等での無駄な呼び出しを防ぐ）
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            if (ignoreIcon == null)
                return;

            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (!GitIgnoreSupport.IsIgnored(path))
                return;

            GUI.DrawTexture(
                new Rect(rect.x, rect.y, 16, 16),
                ignoreIcon
            );
        }
    }
}