using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace UsefulToolkit.Framework
{
    public sealed class UsefulToolkitProjectSettings : SettingsProvider
    {
        private readonly List<IProjectSettingsSection> _sections = new();

        public UsefulToolkitProjectSettings(string path, SettingsScope scope)
            : base(path, scope)
        {
        }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new UsefulToolkitProjectSettings(
                "Project/Useful Toolkit",
                SettingsScope.Project);
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _sections.Clear();

            _sections.Add(new PackageSection());
            _sections.Add(new CodeGenerationSection());
            _sections.Add(new MaintenanceSection());
            _sections.Add(new AboutSection());
        }

        public override void OnGUI(string searchContext)
        {
            foreach (var section in _sections)
            {
                EditorGUILayout.LabelField(section.Title, EditorStyles.boldLabel);

                using (new EditorGUI.IndentLevelScope())
                {
                    section.OnGUI();
                }

                EditorGUILayout.Space(10);
            }
        }
    }
}