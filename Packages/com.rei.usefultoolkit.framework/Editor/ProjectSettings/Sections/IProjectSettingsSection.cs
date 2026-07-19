namespace UsefulToolkit.Framework
{
    internal interface IProjectSettingsSection
    {
        string Title { get; }

        void OnGUI();
    }
}