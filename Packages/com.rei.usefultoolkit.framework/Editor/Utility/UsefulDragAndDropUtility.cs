using UnityEngine;
using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// エディタ拡張でのドラッグ＆ドロップ操作を補助するユーティリティクラス
    /// </summary>
    public static class UsefulDragAndDropUtility
    {
        /// <summary>
        /// IMGUI用のドラッグ＆ドロップエリアを描画し、処理します。
        /// </summary>
        public static void DrawDropArea(Rect rect, string label, Action<string[]> onPathsDropped = null, Action<UnityEngine.Object[]> onObjectsDropped = null)
        {
            GUI.Box(rect, label, new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter });
            HandleDragAndDrop(rect, onPathsDropped, onObjectsDropped);
        }

        /// <summary>
        /// IMGUI用のドラッグ＆ドロップイベントを処理します。
        /// </summary>
        public static void HandleDragAndDrop(Rect rect, Action<string[]> onPathsDropped = null, Action<UnityEngine.Object[]> onObjectsDropped = null)
        {
            Event evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        onPathsDropped?.Invoke(DragAndDrop.paths);
                        onObjectsDropped?.Invoke(DragAndDrop.objectReferences);
                        evt.Use();
                    }
                    break;
            }
        }

        /// <summary>
        /// UI Elements用のドラッグ＆ドロップコールバックを登録します。
        /// </summary>
        public static void RegisterDragAndDropCallbacks(VisualElement element, Action<string[]> onPathsDropped = null, Action<UnityEngine.Object[]> onObjectsDropped = null)
        {
            element.RegisterCallback<DragEnterEvent>(evt => DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
            element.RegisterCallback<DragUpdatedEvent>(evt => DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
            element.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                onPathsDropped?.Invoke(DragAndDrop.paths);
                onObjectsDropped?.Invoke(DragAndDrop.objectReferences);
            });
        }
    }
}


