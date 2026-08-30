using UnityEditor;
using UnityEngine;
using UsefulToolkit.Editor.Setting;

namespace UsefulToolkit.Editor.ProjectSettings
{
    /// <summary>
    /// サブパッケージの導入・削除・一括アップデートは <see cref="UsefulToolkitInstaller"/> に一本化しているため、
    /// このセクションはインストーラーを開く導線だけを提供する。
    /// </summary>
    internal sealed class PackageSection : IProjectSettingsSection
    {
        public string Title => "Package / Tools";

        public void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "サブパッケージの導入・削除・一括アップデートは Toolkit Installer に統合されています。",
                MessageType.Info);

            if (GUILayout.Button("Toolkit Installer を開く", GUILayout.Height(28)))
            {
                UsefulToolkitInstaller.ShowWindow();
            }
        }
    }
}
