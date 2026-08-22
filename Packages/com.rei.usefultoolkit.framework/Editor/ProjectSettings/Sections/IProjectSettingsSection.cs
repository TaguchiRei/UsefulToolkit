namespace UsefulToolkit.Editor.ProjectSettings
{
    internal interface IProjectSettingsSection
    {
        string Title { get; }

        void OnGUI();
    }
}