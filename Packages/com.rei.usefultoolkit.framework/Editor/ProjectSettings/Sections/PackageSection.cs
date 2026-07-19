using UnityEngine;

namespace UsefulToolkit.Framework
{
    internal sealed class PackageSection : IProjectSettingsSection
    {
        public string Title => "Package / Tools";

        public void OnGUI()
        {
            PackageSectionSettings settings = UsefulToolkitSettingsScriptable.instance.PackageSectionSettings;
            // -----------------以下各種設定項目描画---------------------
            //---------------------描画ここまで-------------------------
        }
    }
}