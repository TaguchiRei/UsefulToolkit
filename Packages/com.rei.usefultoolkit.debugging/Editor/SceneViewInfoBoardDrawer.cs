using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Debugging
{
    [InitializeOnLoad]
    public static class SceneViewInfoBoardDrawer
    {
        private static GUIStyle _boardStyle;

        private static readonly GUIStyle _titleStyle = new()
        {
            normal =
            {
                textColor = Color.white
            },
            fontStyle = FontStyle.Bold,
            fontSize = 12
        };

        private static readonly GUIStyle _contentStyle = new()
        {
            normal =
            {
                textColor = new Color(0.9f, 0.9f, 0.9f)
            },
            fontSize = 11,
            wordWrap = true
        };

        static SceneViewInfoBoardDrawer()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void InitializeStyles()
        {
            if (_boardStyle != null)
                return;

            _boardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 8, 8)
            };
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            Handles.BeginGUI();

            InitializeStyles();

            var boards = Object.FindObjectsByType<SceneViewInfoBoard>(FindObjectsSortMode.None);

            if (boards == null || boards.Length == 0)
            {
                Handles.EndGUI();
                return;
            }

            foreach (var board in boards)
            {
                if (board == null)
                    continue;

                Vector3 worldPos = board.transform.position + Vector3.up * 2.0f;

                Vector3 viewportPos = sceneView.camera.WorldToViewportPoint(worldPos);
                if (viewportPos.z < 0)
                    continue;

                Vector2 screenPos = HandleUtility.WorldToGUIPoint(worldPos);

                string contentText = board.GetFormattedText();

                const float width = 220f;
                const float titleHeight = 18f;

                float contentHeight = string.IsNullOrEmpty(contentText)
                    ? 0f
                    : _contentStyle.CalcHeight(new GUIContent(contentText), width - 16);

                float totalHeight = titleHeight + contentHeight + (string.IsNullOrEmpty(contentText) ? 16f : 20f);

                Rect rect = new(
                    screenPos.x - width * 0.5f,
                    screenPos.y - totalHeight,
                    width,
                    totalHeight);

                // 背景
                EditorGUI.DrawRect(rect, board.BoardColor);

                // 枠線
                Handles.color = Color.black;
                Handles.DrawSolidRectangleWithOutline(rect, Color.clear, Color.black);

                GUILayout.BeginArea(rect, _boardStyle);

                GUILayout.Label($"【{board.Title}】", _titleStyle);

                if (!string.IsNullOrEmpty(contentText))
                {
                    GUILayout.Space(4);
                    GUILayout.Label(contentText, _contentStyle);
                }

                GUILayout.EndArea();
            }

            Handles.EndGUI();
        }
    }
}