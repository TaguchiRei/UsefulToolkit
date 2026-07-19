using UnityEditor;
using UnityEngine;
using UsefulToolkit.Framework;
using System.Collections.Generic;

namespace UsefulToolkit.GitSupport
{
    public class GitSupportSettingPage : SettingPageBase
    {
        private GitSupportSettings _settings;
        private GitIgnoreSetting _gitIgnoreSetting;
        private string _newBranch = "";
        private bool _showGitIgnore = true;

        public override string Name => "Git";

        public override void Initialize()
        {
            _settings = GitSupportSettings.Load();
            
            // gitignoreロジッククラスの初期化
            _gitIgnoreSetting = new GitIgnoreSetting();
            _gitIgnoreSetting.Initialize();
        }

        public override void OnGUI()
        {
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                // Warning Branches Settings
                EditorGUILayout.LabelField("Warning Branches", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("このリスト内にある名前のブランチの使用時、アセット更新時やセーブ時に警告が出ます。", MessageType.Info);
                
                for (int i = 0; i < _settings.WarningBranches.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _settings.WarningBranches[i] = EditorGUILayout.TextField(_settings.WarningBranches[i]);
                        if (GUILayout.Button("Remove", GUILayout.Width(60)))
                        {
                            _settings.WarningBranches.RemoveAt(i);
                            i--;
                        }
                    }
                }

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    _newBranch = EditorGUILayout.TextField(_newBranch);
                    if (GUILayout.Button("Add", GUILayout.Width(60)))
                    {
                        if (!string.IsNullOrEmpty(_newBranch))
                        {
                            _settings.WarningBranches.Add(_newBranch);
                            _newBranch = "";
                        }
                    }
                }

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Warning Settings", EditorStyles.boldLabel);
                
                _settings.WarningType = (BranchWarningType)EditorGUILayout.EnumPopup("Warning Type", _settings.WarningType);
                _settings.WarningOnSaved = EditorGUILayout.Toggle("Warning On Saved", _settings.WarningOnSaved);
                _settings.WarningOnCompiled = EditorGUILayout.Toggle("Warning On Compiled", _settings.WarningOnCompiled);

                if (check.changed)
                {
                    EditorUtility.SetDirty(_settings);
                    AssetDatabase.SaveAssets();
                }
            }

            EditorGUILayout.Space(20);
            
            // 2 gitignore Management
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _showGitIgnore = EditorGUILayout.Foldout(_showGitIgnore, ".gitignore Folder Tree", true);
                    if (GUILayout.Button("Scan Folders", GUILayout.Width(100)))
                    {
                        _gitIgnoreSetting.Refresh();
                    }
                }

                if (_showGitIgnore)
                {
                    EditorGUILayout.HelpBox("無視したいフォルダの 'Ignore' にチェックを入れます。\nShiftキーを押しながらクリックすると子フォルダも一括で同期します。", MessageType.Info);

                    var structure = _gitIgnoreSetting.CurrentStructure;
                    if (structure == null || structure.Count == 0)
                    {
                        EditorGUILayout.HelpBox("フォルダ構造が見つかりません。'Scan Folders' を押してください。", MessageType.None);
                    }
                    else
                    {
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            DrawHierarchyList(structure, 0);
                        }
                    }

                    EditorGUILayout.Space(5);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Save to .gitignore", GUILayout.Height(30)))
                        {
                            _gitIgnoreSetting.Save();
                        }
                        if (GUILayout.Button("Reload .gitignore", GUILayout.Width(140), GUILayout.Height(30)))
                        {
                            _gitIgnoreSetting.Refresh();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// フォルダツリーを再帰的にGUI描画する
        /// </summary>
        private void DrawHierarchyList(List<GitIgnoreFolderNode> list, int depth)
        {
            if (list == null) return;

            foreach (var item in list)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(depth * 16);

                    bool hasChildren = item.children != null && item.children.Count > 0;

                    // hasChildren の有無に関わらず、常に同じサイズ・種類のRectを確保する
                    Rect foldoutRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
                    if (hasChildren)
                    {
                        item.isExpanded = EditorGUI.Foldout(foldoutRect, item.isExpanded, GUIContent.none, true);
                    }
                    // hasChildrenがfalseの場合は何も描画せず、Rectだけ消費して空間を揃える

                    bool exists = AssetDatabase.IsValidFolder(item.folderPath);
                    GUILayout.Label(exists ? "📁" : "⚪", GUILayout.Width(18));

                    EditorGUILayout.LabelField(item.folderName, EditorStyles.miniLabel);

                    if (exists)
                    {
                        GUILayout.FlexibleSpace();
                        bool prevIgnored = item.isIgnored;
                        item.isIgnored = EditorGUILayout.ToggleLeft("Ignore", item.isIgnored, GUILayout.Width(60));

                        if (prevIgnored != item.isIgnored && Event.current.shift)
                        {
                            SetIgnoreRecursive(item, item.isIgnored);
                        }
                    }
                    else
                    {
                        GUILayout.FlexibleSpace();
                    }
                }

                if (item.isExpanded && item.children != null && item.children.Count > 0)
                {
                    DrawHierarchyList(item.children, depth + 1);
                }
            }
        }

        private void SetIgnoreRecursive(GitIgnoreFolderNode item, bool ignore)
        {
            item.isIgnored = ignore;
            foreach (var child in item.children)
            {
                SetIgnoreRecursive(child, ignore);
            }
        }
    }
}