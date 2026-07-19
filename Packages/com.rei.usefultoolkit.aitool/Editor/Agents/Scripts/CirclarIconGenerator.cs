using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Ai
{
    public class CircularIconGeneratorWindow : EditorWindow
    {
        private const string SaveDirectory = "Assets/Code/Editor/AiChat/Agents/Icon/";

        private Texture2D sourceTexture;
        private Texture2D previewTexture;

        private string iconName = "NewIcon";

        private Vector2 cropCenter;
        private int diameter = 128;

        private bool dragging;

        private Rect imageRect;
        private float imageScale;
        private Vector2 imageOffset;

        // ★これが必要（今回の本体）
        private Action<Texture2D> onGenerated;

        public static void Open(Texture2D texture, Action<Texture2D> onGenerated)
        {
            var window = CreateInstance<CircularIconGeneratorWindow>();

            window.sourceTexture = texture;
            window.onGenerated = onGenerated;

            if (texture != null)
            {
                window.cropCenter = new Vector2(texture.width * 0.5f, texture.height * 0.5f);
            }

            window.titleContent = new GUIContent("Create Icon");
            window.minSize = new Vector2(700, 650);

            window.GeneratePreview();
            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (sourceTexture == null)
            {
                EditorGUILayout.HelpBox("Source texture is null.", MessageType.Error);
                return;
            }

            iconName = EditorGUILayout.TextField("Icon Name", iconName);

            DrawTextureArea();

            GUILayout.Space(10);

            GUILayout.Label("Preview", EditorStyles.boldLabel);

            Rect previewRect = GUILayoutUtility.GetRect(128, 128, GUILayout.Width(128), GUILayout.Height(128));

            if (previewTexture != null)
            {
                EditorGUI.DrawPreviewTexture(previewRect, previewTexture);
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Diameter", diameter.ToString());

            if (GUILayout.Button("Generate", GUILayout.Height(32)))
            {
                Generate();
            }

            HandleEvents();
        }

        private void DrawTextureArea()
        {
            float maxWidth = position.width - 20f;
            float maxHeight = 400f;

            float scaleX = maxWidth / sourceTexture.width;
            float scaleY = maxHeight / sourceTexture.height;

            imageScale = Mathf.Min(scaleX, scaleY);

            float drawWidth = sourceTexture.width * imageScale;
            float drawHeight = sourceTexture.height * imageScale;

            imageRect = GUILayoutUtility.GetRect(drawWidth, drawHeight);

            imageOffset = imageRect.position;

            EditorGUI.DrawPreviewTexture(imageRect, sourceTexture);

            Handles.BeginGUI();

            Vector2 centerGui = TextureToGUI(cropCenter);
            float radiusGui = diameter * 0.5f * imageScale;

            Handles.color = Color.green;
            Handles.DrawWireDisc(centerGui, Vector3.forward, radiusGui);

            Handles.color = new Color(0f, 1f, 0f, 0.2f);
            Handles.DrawSolidDisc(centerGui, Vector3.forward, radiusGui);

            Handles.EndGUI();
        }

        private void HandleEvents()
        {
            Event e = Event.current;

            if (!imageRect.Contains(e.mousePosition))
                return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                dragging = true;
                e.Use();
            }

            if (e.type == EventType.MouseUp)
            {
                dragging = false;
            }

            if (e.type == EventType.MouseDrag && dragging)
            {
                cropCenter = GUIToTexture(e.mousePosition);

                cropCenter.x = Mathf.Clamp(cropCenter.x, 0, sourceTexture.width);
                cropCenter.y = Mathf.Clamp(cropCenter.y, 0, sourceTexture.height);

                GeneratePreview();
                Repaint();
                e.Use();
            }

            if (e.type == EventType.ScrollWheel)
            {
                diameter -= Mathf.RoundToInt(e.delta.y * 8f);
                diameter = Mathf.Clamp(diameter, 16, Mathf.Min(sourceTexture.width, sourceTexture.height));

                GeneratePreview();
                Repaint();
                e.Use();
            }
        }

        private Vector2 TextureToGUI(Vector2 t)
        {
            return new Vector2(
                imageOffset.x + t.x * imageScale,
                imageOffset.y + (sourceTexture.height - t.y) * imageScale);
        }

        private Vector2 GUIToTexture(Vector2 g)
        {
            return new Vector2(
                (g.x - imageOffset.x) / imageScale,
                sourceTexture.height - ((g.y - imageOffset.y) / imageScale));
        }

        private void GeneratePreview()
        {
            previewTexture = CreateCircularIcon(
                sourceTexture,
                (int)cropCenter.x,
                (int)cropCenter.y,
                diameter);
        }

        private void Generate()
        {
            if (string.IsNullOrWhiteSpace(iconName))
            {
                EditorUtility.DisplayDialog("Error", "Icon Name is empty.", "OK");
                return;
            }

            Directory.CreateDirectory(SaveDirectory);

            string filePath = Path.Combine(SaveDirectory, $"{iconName}.png");

            Texture2D result = CreateCircularIcon(
                sourceTexture,
                (int)cropCenter.x,
                (int)cropCenter.y,
                diameter);

            File.WriteAllBytes(filePath, result.EncodeToPNG());

            AssetDatabase.Refresh();

            Texture2D asset = AssetDatabase.LoadAssetAtPath<Texture2D>(filePath);

            // ★ここで返す
            onGenerated?.Invoke(asset);

            Close();
        }

        private Texture2D CreateCircularIcon(Texture2D source, int cx, int cy, int d)
        {
            Texture2D output = new Texture2D(d, d, TextureFormat.RGBA32, false);

            float r = d * 0.5f;
            float r2 = r * r;

            for (int y = 0; y < d; y++)
            {
                for (int x = 0; x < d; x++)
                {
                    float dx = x - r;
                    float dy = y - r;

                    if (dx * dx + dy * dy > r2)
                    {
                        output.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    int sx = cx - d / 2 + x;
                    int sy = cy - d / 2 + y;

                    if (sx < 0 || sx >= source.width || sy < 0 || sy >= source.height)
                    {
                        output.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    output.SetPixel(x, y, source.GetPixel(sx, sy));
                }
            }

            output.Apply();
            return output;
        }
    }
}