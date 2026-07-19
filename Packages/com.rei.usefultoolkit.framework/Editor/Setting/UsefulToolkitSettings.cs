using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UsefulToolkit.Framework
{
    public class UsefulToolkitSettings : EditorWindow
    {
        private SettingPageProvider _provider;
        private int _selectedTabIndex = 0;
        private Vector2 _scrollPosition;

        [MenuItem("UsefulToolkit/Settings", false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<UsefulToolkitSettings>("Useful Toolkit Settings");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            _provider = new SettingPageProvider();
        }

        private void OnGUI()
        {
            DrawHeader();

            if (_provider.Pages.Count == 0)
            {
                EditorGUILayout.HelpBox("設定ページが見つかりません。SettingPageBaseを継承したクラスを作成してください。", MessageType.Warning);
                if (GUILayout.Button("初期化を再試行", GUILayout.Height(30))) _provider.Reload();
                return;
            }

            // タブの選択
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                var tabNames = _provider.Pages.Select(p => p.Name).ToArray();
                _selectedTabIndex = GUILayout.Toolbar(_selectedTabIndex, tabNames, GUILayout.Height(25));
                if (check.changed)
                {
                    _scrollPosition = Vector2.zero; // ページ切り替え時にスクロールを戻す
                    GUI.FocusControl(null); // 前のページの入力フォーカスを外す
                }
            }

            EditorGUILayout.Space(10);

            // 選択されたページの描画
            using (var scroll = new EditorGUILayout.ScrollViewScope(_scrollPosition))
            {
                _scrollPosition = scroll.scrollPosition;

                using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
                {
                    _provider.Pages[_selectedTabIndex].OnGUI();
                }
            }
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Useful Tools Framework", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton))
                {
                    _provider.Reload();
                    _selectedTabIndex = 0;
                }
            }

            EditorGUILayout.Space(5);
        }
    }
}