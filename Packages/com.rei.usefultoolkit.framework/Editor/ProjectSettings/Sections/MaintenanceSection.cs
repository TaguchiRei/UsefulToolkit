namespace UsefulToolkit.Framework
{
    internal sealed class MaintenanceSection : IProjectSettingsSection
    {
        public string Title => "Maintenance";

        public void OnGUI()
        {
            MaintenanceSectionSettings settings = UsefulToolkitSettingsScriptable.instance.MaintenanceSectionSettings;
            // -----------------以下各種設定項目描画---------------------
            //---------------------描画ここまで-------------------------
        }
    }
}