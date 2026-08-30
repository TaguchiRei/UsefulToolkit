using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UsefulToolkit.Editor.SceneSupport
{
    /// <summary>
    /// プロジェクト内のシーンをボタン一発でエディタに開くためのウィンドウ。
    /// 常駐シーン(UsefulToolkitPersistent)を最上段、Build Settings登録シーンを左、
    /// 非登録シーンを右に並べる。エディタ作業でシーンを開き直す手間を減らすための道具で、
    /// ランタイムの初期化やシーン遷移システムとは無関係。
    /// </summary>
    public class SceneLoadWindow : EditorWindow
    {
        /// <summary>
        /// 常駐シーンとして最上段へ固定するシーン名。
        /// PersistentSceneCreator の既定シーン名と揃えている（リネームした場合は検出できない）。
        /// </summary>
        private const string PersistentSceneName = "UsefulToolkitPersistent";

        /// <summary>
        /// 表示に必要なシーン1件分の情報。
        /// </summary>
        private readonly struct SceneEntry
        {
            /// <summary> Assets からのシーンパス </summary>
            public readonly string Path;

            /// <summary> 拡張子を除いたシーン名 </summary>
            public readonly string Name;

            /// <summary> Build Settings に登録されているか（enabled/disabled は問わない） </summary>
            public readonly bool Registered;

            /// <summary> Build Settings で有効化されているか </summary>
            public readonly bool Enabled;

            /// <summary> 有効な登録シーンのみ 0 以上。無効・非登録は -1 </summary>
            public readonly int BuildIndex;

            public SceneEntry(string path, bool registered, bool enabled, int buildIndex)
            {
                Path = path;
                Name = System.IO.Path.GetFileNameWithoutExtension(path);
                Registered = registered;
                Enabled = enabled;
                BuildIndex = buildIndex;
            }
        }

        private readonly List<SceneEntry> _persistentScenes = new();
        private readonly List<SceneEntry> _buildScenes = new();
        private readonly List<SceneEntry> _nonBuildScenes = new();

        private Vector2 _leftScroll;
        private Vector2 _rightScroll;

        // 毎フレーム AssetDatabase を叩かないよう、変更契機でだけ作り直す
        private bool _dirty = true;

        [MenuItem("UsefulToolkit/Scene/SceneLoadWindow", false, 15)]
        public static void ShowWindow()
        {
            var window = GetWindow<SceneLoadWindow>("Scene Load");
            window.minSize = new Vector2(480, 360);
        }

        private void OnEnable()
        {
            _dirty = true;
            EditorBuildSettings.sceneListChanged += MarkDirty;
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private void OnDisable()
        {
            EditorBuildSettings.sceneListChanged -= MarkDirty;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
        }

        // フォーカスが戻ったタイミングで、外で増減したシーンを取り込む
        private void OnFocus() => _dirty = true;

        private void MarkDirty() => _dirty = true;

        private void OnProjectChange() => _dirty = true;

        private void OnSceneOpened(Scene scene, OpenSceneMode mode) => Repaint();

        private void OnGUI()
        {
            if (_dirty)
            {
                RebuildLists();
            }

            DrawHeader();

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("再生中はシーンを開けません。再生を停止してください。", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                DrawPersistentSection();
                GUILayout.Space(8);
                DrawColumns();
            }
        }

        /// <summary>
        /// Build Settings と Assets 以下の t:Scene から3つのリストを作り直す。
        /// </summary>
        private void RebuildLists()
        {
            _dirty = false;
            _persistentScenes.Clear();
            _buildScenes.Clear();
            _nonBuildScenes.Clear();

            // 左カラム: Build Settings 登録シーン。表示順は登録順（＝ビルドインデックス順）。
            // ビルドインデックスは有効なシーンだけを数えた値にする（Unity の実挙動に合わせる）。
            var registeredPaths = new HashSet<string>();
            int enabledCount = 0;

            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (string.IsNullOrEmpty(scene.path)) continue;
                if (!registeredPaths.Add(scene.path)) continue;

                int buildIndex = scene.enabled ? enabledCount++ : -1;
                var entry = new SceneEntry(scene.path, registered: true, enabled: scene.enabled, buildIndex);

                if (entry.Name == PersistentSceneName) _persistentScenes.Add(entry);
                else _buildScenes.Add(entry);
            }

            // 右カラム: 上記以外のシーン。順序に意味はないのでパス順で安定させる
            // （SceneEnumGenerator の NonBuildScenes と同じ並べ方）。
            var otherScenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Where(path => !registeredPaths.Contains(path))
                .OrderBy(path => path, StringComparer.Ordinal);

            foreach (var path in otherScenePaths)
            {
                var entry = new SceneEntry(path, registered: false, enabled: false, buildIndex: -1);

                if (entry.Name == PersistentSceneName) _persistentScenes.Add(entry);
                else _nonBuildScenes.Add(entry);
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            {
                var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
                EditorGUILayout.LabelField("Scene Load", titleStyle);

                if (GUILayout.Button("更新", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    _dirty = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            var descStyle = new GUIStyle(EditorStyles.miniLabel)
                { wordWrap = true, normal = { textColor = Color.gray } };
            EditorGUILayout.LabelField("名前ボタン = 単独で開く / [+] = 追加で開く。現在アクティブなシーンには ▶ が付きます。",
                descStyle);

            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);
        }

        private void DrawPersistentSection()
        {
            if (_persistentScenes.Count == 0) return;

            EditorGUILayout.LabelField("常駐シーン", EditorStyles.boldLabel);
            foreach (var entry in _persistentScenes)
            {
                DrawSceneRow(entry, big: true);
            }
        }

        private void DrawColumns()
        {
            EditorGUILayout.BeginHorizontal();
            {
                float columnWidth = position.width / 2f - 10f;

                DrawColumn("Build Settings", _buildScenes, ref _leftScroll, columnWidth);
                DrawColumn("Build 非対象", _nonBuildScenes, ref _rightScroll, columnWidth);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawColumn(string title, List<SceneEntry> scenes, ref Vector2 scroll, float width)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(width));
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

                scroll = EditorGUILayout.BeginScrollView(scroll, GUI.skin.box);
                {
                    if (scenes.Count == 0)
                    {
                        EditorGUILayout.LabelField("なし", EditorStyles.miniLabel);
                    }
                    else
                    {
                        foreach (var entry in scenes)
                        {
                            DrawSceneRow(entry, big: false);
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSceneRow(SceneEntry entry, bool big)
        {
            var activeScene = SceneManager.GetActiveScene();
            bool isActive = activeScene.IsValid() && activeScene.path == entry.Path;

            float height = big ? 26f : 20f;

            EditorGUILayout.BeginHorizontal();
            {
                string label = BuildPrefix(entry) + entry.Name;
                if (isActive) label = "▶ " + label;

                var buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                };

                if (GUILayout.Button(new GUIContent(label, entry.Path), buttonStyle, GUILayout.Height(height)))
                {
                    OpenScene(entry.Path, OpenSceneMode.Single);
                }

                if (GUILayout.Button(new GUIContent("+", $"{entry.Name} を追加で開く"),
                        GUILayout.Width(24), GUILayout.Height(height)))
                {
                    OpenScene(entry.Path, OpenSceneMode.Additive);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// シーン名の前に付ける、Build Settings 上の状態を表す短いラベルを返す。
        /// </summary>
        private static string BuildPrefix(SceneEntry entry)
        {
            if (!entry.Registered) return string.Empty;
            return entry.Enabled ? $"[{entry.BuildIndex}] " : "[off] ";
        }

        /// <summary>
        /// シーンを開く。Single の場合のみ、開く前に未保存シーンの保存を確認する。
        /// </summary>
        private void OpenScene(string path, OpenSceneMode mode)
        {
            if (mode == OpenSceneMode.Single &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            try
            {
                EditorSceneManager.OpenScene(path, mode);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[UsefulToolkit] シーン {path} を開けませんでした: {exception.Message}");
            }

            Repaint();
        }
    }
}
