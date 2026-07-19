using UnityEditor;
using UnityEngine;
using UsefulToolkit.Framework;

namespace UsefulToolkit.Debugging
{
    public class DebugSettingPage : SettingPageBase
    {
        public override string Name => "Debug";

        private const string LogCaptureEnabledKey = "UsefulTools.Debug.LogCaptureEnabled";
        private const string DebugFontSizeKey = "UsefulTools.Debug.FontSize";
        private const string DebugPosXKey = "UsefulTools.Debug.PosX";
        private const string DebugPosYKey = "UsefulTools.Debug.PosY";
        private const string DebugFPSSamplingKey = "UsefulTools.Debug.FPSSampling";
        private const string DebugRemoveMissingKey = "UsefulTools.Debug.RemoveMissing";
        private const string DebugMaxLogCountKey = "UsefulTools.Debug.MaxLogCount";
        private const string DebugLogTimeoutKey = "UsefulTools.Debug.LogTimeout";

        public static bool LogCaptureEnabled
        {
            get => EditorPrefs.GetBool(LogCaptureEnabledKey, false);
            set => EditorPrefs.SetBool(LogCaptureEnabledKey, value);
        }

        public static int FontSize
        {
            get => EditorPrefs.GetInt(DebugFontSizeKey, 20);
            set => EditorPrefs.SetInt(DebugFontSizeKey, value);
        }

        public static Vector2 Position
        {
            get => new(EditorPrefs.GetFloat(DebugPosXKey, 10f), EditorPrefs.GetFloat(DebugPosYKey, 10f));
            set
            {
                EditorPrefs.SetFloat(DebugPosXKey, value.x);
                EditorPrefs.SetFloat(DebugPosYKey, value.y);
            }
        }

        public static int FPSSampling
        {
            get => EditorPrefs.GetInt(DebugFPSSamplingKey, 10);
            set => EditorPrefs.SetInt(DebugFPSSamplingKey, value);
        }

        public static bool RemoveMissingReferences
        {
            get => EditorPrefs.GetBool(DebugRemoveMissingKey, true);
            set => EditorPrefs.SetBool(DebugRemoveMissingKey, value);
        }

        public static int MaxLogCount
        {
            get => EditorPrefs.GetInt(DebugMaxLogCountKey, 10);
            set => EditorPrefs.SetInt(DebugMaxLogCountKey, value);
        }

        public static float LogTimeout
        {
            get => EditorPrefs.GetFloat(DebugLogTimeoutKey, 5f);
            set => EditorPrefs.SetFloat(DebugLogTimeoutKey, value);
        }

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("Debug GUI & Log Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Log Management", EditorStyles.miniBoldLabel);
                
                LogCaptureEnabled = EditorGUILayout.Toggle("Capture Application Logs", LogCaptureEnabled);
                MaxLogCount = EditorGUILayout.IntSlider("Max Log Count", MaxLogCount, 1, 50);
                LogTimeout = EditorGUILayout.Slider("Log Timeout (sec)", LogTimeout, 1f, 30f);
                
                EditorGUILayout.HelpBox("Settings for DebugGUI.Log and captured application logs.", MessageType.Info);
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Debug GUI Appearance & Behavior", EditorStyles.miniBoldLabel);

                Position = EditorGUILayout.Vector2Field("Screen Position", Position);
                FontSize = EditorGUILayout.IntSlider("Font Size", FontSize, 20, 100);
                FPSSampling = EditorGUILayout.IntSlider("FPS Sampling Count", FPSSampling, 5, 100);
                RemoveMissingReferences =
                    EditorGUILayout.Toggle("Remove Missing References", RemoveMissingReferences);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Generate Debug GUI in Scene", GUILayout.Height(30)))
            {
                DebugGuiSetUp.Setup();
            }
        }
    }
}