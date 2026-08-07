using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Framework
{
    internal sealed class CodeGenerationSection : IProjectSettingsSection
    {
        public string Title => "Code Generation";

        public void OnGUI()
        {
            var settings = UsefulToolkitSettingsScriptable.instance.CodeGenerationSectionSettings;
            EditorGUI.BeginChangeCheck();

            // -----------------以下各種設定項目描画---------------------
            EditorGUILayout.LabelField("Runtime Save Path", EditorStyles.boldLabel);
            DrawSelectGenerateDirectory(settings);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Local Save Path", EditorStyles.boldLabel);
            DrawSelectLocalGenerateDirectory(settings);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Namespace", EditorStyles.boldLabel);
            DrawNamespaceField(settings);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Generate Timing", EditorStyles.boldLabel);
            DrawTimingField(settings);
            //---------------------描画ここまで-------------------------

            if (EditorGUI.EndChangeCheck())
            {
                UsefulToolkitSettingsScriptable.instance.Save();
            }
        }

        private void DrawSelectGenerateDirectory(CodeGenerationSectionSettings settings)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.SelectableLabel(
                    settings.RuntimeSavePath,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight)
                );
            }

            if (GUILayout.Button("保存先を選択"))
            {
                var selectedPath =
                    EditorUtility.OpenFolderPanel("Select Save Path", settings.RuntimeSavePath, "");

                if (!string.IsNullOrEmpty(selectedPath))
                {
                    var assetsPath = Path.GetFullPath(Application.dataPath);
                    var fullSelectedPath = Path.GetFullPath(selectedPath);

                    if (!fullSelectedPath.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase))
                    {
                        EditorUtility.DisplayDialog("エラー", "Assetsフォルダ内を選択してください。", "OK");
                        return;
                    }

                    settings.RuntimeSavePath =
                        "Assets" + fullSelectedPath.Substring(assetsPath.Length).Replace('\\', '/');
                }
            }
        }

        private void DrawNamespaceField(CodeGenerationSectionSettings settings)
        {
            settings.Namespace = EditorGUILayout.TextField("Namespace", settings.Namespace);
        }

        private void DrawTimingField(CodeGenerationSectionSettings settings)
        {
            settings.Timing = (GenerateTiming)EditorGUILayout.EnumPopup("Generate Timing", settings.Timing);
        }

        private void DrawSelectLocalGenerateDirectory(CodeGenerationSectionSettings settings)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.SelectableLabel(
                    settings.LocalSavePath,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight)
                );
            }

            if (GUILayout.Button("保存先を選択"))
            {
                var selectedPath = EditorUtility.OpenFolderPanel(
                    "Select Local Save Path",
                    settings.LocalSavePath,
                    ""
                );

                if (!string.IsNullOrEmpty(selectedPath))
                {
                    var assetsPath = Path.GetFullPath(Application.dataPath);
                    var fullSelectedPath = Path.GetFullPath(selectedPath);

                    if (!fullSelectedPath.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase))
                    {
                        EditorUtility.DisplayDialog(
                            "エラー",
                            "Assetsフォルダ内を選択してください。",
                            "OK");
                        return;
                    }

                    settings.LocalSavePath =
                        "Assets" + fullSelectedPath.Substring(assetsPath.Length).Replace('\\', '/');
                }
            }
        }
    }
}