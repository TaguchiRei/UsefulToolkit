using UnityEditor;

namespace UsefulToolkit.Framework
{
    public class UsefulToolkitInstaller : EditorWindow
    {
        [MenuItem("UsefulToolkit/Installer")]
        public static void ShowWindow()
        {
            GetWindow<UsefulToolkitInstaller>("UsefulToolkitInstaller");
        }

        private void OnGUI()
        {
            
        }
    }
}
