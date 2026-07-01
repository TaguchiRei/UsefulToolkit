using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Debugging
{
    using Debug = UnityEngine.Debug;

    public class DebugGuiSetUp
    {
        [MenuItem("UsefulToolkit/ProgramTools/DebugGUI Setup")]
        private static void Setup()
        {
            var debugGUI = Object.FindAnyObjectByType<DebugGUI>();
            if (debugGUI == null)
            {
                GameObject obj = new GameObject("DebugGUI");
                obj.AddComponent<DebugGUI>();
                return;
            }

            Debug.Log("DebugGUI is Already Exists");
        }
    }
}