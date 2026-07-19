using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UsefulToolkit.Framework;

namespace UsefulToolkit.Ai
{
    public sealed class AiSettingPage : SettingPageBase
    {
        private AiChatSettings chatSettings;
        private Vector2 scrollPosition;
        private SerializedObject serializedChatSettings;

        public override string Name => "AI Settings";

        public override void Initialize()
        {
            chatSettings = AiChatSettings.Load();
            serializedChatSettings = new SerializedObject(ScriptableObject.CreateInstance<SettingsWrapper>());
            ((SettingsWrapper)serializedChatSettings.targetObject).Settings = chatSettings;
        }

        public override void OnGUI()
        {
            if (serializedChatSettings == null || serializedChatSettings.targetObject == null)
            {
                Initialize();
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Space(5);

            EditorGUILayout.LabelField("AI Chat Global Settings", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            serializedChatSettings.Update();
            var prop = serializedChatSettings.FindProperty("Settings").FindPropertyRelative("ActiveClientSettings");
            EditorGUILayout.PropertyField(prop, new GUIContent("Active AI Client"), true);
            serializedChatSettings.ApplyModifiedProperties();

            chatSettings.ActiveClientSettings = ((SettingsWrapper)serializedChatSettings.targetObject).Settings.ActiveClientSettings;

            if (EditorGUI.EndChangeCheck())
            {
                chatSettings.Save();
            }

            GUILayout.Space(10);

            if (chatSettings.ActiveClientSettings != null)
            {
                chatSettings.ActiveClientSettings.DrawSettingsGUI();
            }

            DrawBlackList();

            EditorGUILayout.EndScrollView();
        }

        private class SettingsWrapper : ScriptableObject
        {
            public AiChatSettings Settings = new AiChatSettings();
        }

        private void DrawBlackList()
        {
            GUILayout.Space(15);
            EditorGUILayout.LabelField("AI Blacklist Management", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drag and drop assets or GameObjects here to block AI access.\nItems are added to your personal list by default. Use the 'Share' button to make them project-wide.",
                MessageType.Info);

            var blacklist = AIBlackList.Instance;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Drag and Drop Area
                Rect dropArea = GUILayoutUtility.GetRect(0.0f, 40.0f, GUILayout.ExpandWidth(true));
                UsefulDragAndDropUtility.DrawDropArea(dropArea,
                    "Drag & Drop Files, Folders, or GameObjects here to ADD",
                    paths =>
                    {
                        var blacklist = AIBlackList.Instance;
                        foreach (string path in paths) blacklist.AddPath(path, false);
                    },
                    objects =>
                    {
                        var blacklist = AIBlackList.Instance;
                        foreach (UnityEngine.Object draggedObject in objects)
                        {
                            if (draggedObject is GameObject go)
                            {
                                string assetPath = AssetDatabase.GetAssetPath(go);
                                blacklist.AddPath(string.IsNullOrEmpty(assetPath) ? GetGameObjectPath(go) : assetPath,
                                    false);
                            }
                            else
                            {
                                string path = AssetDatabase.GetAssetPath(draggedObject);
                                if (!string.IsNullOrEmpty(path)) blacklist.AddPath(path, false);
                            }
                        }
                    });

                // Unified List
                var allEntries = blacklist.BlacklistedPaths.Select(p => new { Path = p, IsShared = false })
                    .Concat(blacklist.SharedPaths.Select(p => new { Path = p, IsShared = true }))
                    .OrderBy(x => x.Path)
                    .ToList();

                if (allEntries.Count == 0)
                {
                    EditorGUILayout.LabelField("No items blacklisted.", EditorStyles.miniLabel);
                }
                else
                {
                    foreach (var entry in allEntries)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            string label = entry.IsShared ? $"[Shared] {entry.Path}" : $"[Local] {entry.Path}";
                            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);

                            bool isProtected = entry.Path.Equals("Assets/Code/Editor/AiChat",
                                StringComparison.OrdinalIgnoreCase);

                            using (new EditorGUI.DisabledScope(isProtected))
                            {
                                if (GUILayout.Button(entry.IsShared ? "Unshare" : "Share", GUILayout.Width(70)))
                                {
                                    blacklist.ToggleShare(entry.Path);
                                }

                                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                                {
                                    blacklist.RemovePath(entry.Path, entry.IsShared);
                                }
                            }
                        }
                    }
                }

                GUILayout.Space(5);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add Selected (Local)", GUILayout.Height(20)))
                    {
                        AddSelectedToBlacklist(false);
                    }

                    if (GUILayout.Button("Add Current Scene (Local)", GUILayout.Height(20)))
                    {
                        AddCurrentScene(false);
                    }
                }
            }
        }

        private void AddSelectedToBlacklist(bool shared)
        {
            var blacklist = AIBlackList.Instance;

            // Assets
            foreach (var guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) blacklist.AddPath(path, shared);
            }

            // GameObjects in Scene
            foreach (var go in Selection.gameObjects)
            {
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(go)))
                {
                    blacklist.AddPath(GetGameObjectPath(go), shared);
                }
            }
        }

        private void AddCurrentScene(bool shared)
        {
            var blacklist = AIBlackList.Instance;
            var scene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path))
            {
                blacklist.AddPath(scene.path, shared);
            }
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = "/" + obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = "/" + obj.name + path;
            }

            return path;
        }
    }
}