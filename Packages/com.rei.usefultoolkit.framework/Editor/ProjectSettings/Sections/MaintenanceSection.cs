using UnityEditor;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.Editor.ProjectSettings
{
    internal sealed class MaintenanceSection : IProjectSettingsSection
    {
        public string Title => "Maintenance";

        public void OnGUI()
        {
            MaintenanceSectionSettings settings = UsefulToolkitSettingsScriptable.instance.MaintenanceSectionSettings;
            // -----------------以下各種設定項目描画---------------------
            HeavyValidationSettings.Enabled = EditorGUILayout.ToggleLeft(
                "リフレクション等を使う重いバリデーションチェックを有効化する",
                HeavyValidationSettings.Enabled);
            //---------------------描画ここまで-------------------------
        }
    }
}