using UnityEditor;

namespace UsefulToolkit.Framework
{
    internal sealed class AboutSection : IProjectSettingsSection
    {
        public string Title => "About";

        public void OnGUI()
        {
            AboutSectionSettings settings = UsefulToolkitSettingsScriptable.instance.AboutSectionSettings;
            // -----------------以下各種設定項目描画---------------------
            DrawAbout(settings);
            //---------------------描画ここまで-------------------------
        }

        private void DrawAbout(AboutSectionSettings settings)
        {
            EditorGUILayout.LabelField($"Package : UsefulToolkit", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Version : {settings.Version}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Copyright (C) 2026 Rei");
            EditorGUILayout.LabelField("Repository : https://github.com/TaguchiRei/UsefulToolkit");

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("License", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Custom License");
            EditorGUILayout.LabelField("- Personal & Commercial Use: Allowed");
            EditorGUILayout.LabelField("- Modification: Allowed");
            EditorGUILayout.LabelField("- Redistribution: Prohibited");
            EditorGUILayout.LabelField("- Attribution: Not Required");
        }
    }
}