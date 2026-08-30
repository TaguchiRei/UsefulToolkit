using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.Attributes;

namespace UsefulToolkit.Editor.Attributes
{
    /// <summary>
    /// <see cref="ScenePopupAttribute"/> の描画。プロジェクト内の全シーンアセットを描画時に列挙し、
    /// 選択されたシーンのパスを string プロパティへ書き込む。先頭は常に「(なし)」。
    ///
    /// 表示は**シーン名のみ**。<see cref="EditorGUI.Popup(Rect,string,int,string[])"/> は項目中の '/' を
    /// サブメニュー区切りとして扱うため、表示文字列にパス区切りを入れない
    /// (入れると全フォルダを辿らないと選べない階層メニューになる)。同名シーンがある時だけ
    /// 親フォルダ名を「 — 」で添えて区別する。保存されるのは常にフルパスなので識別は一意。
    /// 保存済みのパスが現在存在しない場合は [missing] 付きで選択肢に残す。
    /// </summary>
    [CustomPropertyDrawer(typeof(ScenePopupAttribute))]
    public class ScenePopupDrawer : PropertyDrawer
    {
        private const string NoneLabel = "(なし)";

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            string[] scenePaths = AssetDatabase.FindAssets("t:SceneAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            string[] displayNames = BuildDisplayNames(scenePaths);

            var options = new List<string> { NoneLabel };
            options.AddRange(displayNames);

            string stored = property.stringValue;
            int current = 0;

            if (!string.IsNullOrEmpty(stored))
            {
                int found = Array.IndexOf(scenePaths, stored);
                if (found >= 0)
                {
                    current = found + 1;
                }
                else
                {
                    options.Add($"[missing] {SceneNameOf(stored)}");
                    current = options.Count - 1;
                }
            }

            EditorGUI.BeginProperty(position, label, property);

            int selected = EditorGUI.Popup(position, label.text, current, options.ToArray());

            if (selected != current)
            {
                property.stringValue = selected >= 1 && selected <= scenePaths.Length
                    ? scenePaths[selected - 1]
                    : string.Empty;
            }

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// シーン名だけの表示名を作る。同名が複数ある場合のみ親フォルダ名で区別し、
        /// それでも重複するなら連番を付ける。'/' は一切含めない。
        /// </summary>
        private static string[] BuildDisplayNames(string[] scenePaths)
        {
            var names = scenePaths.Select(SceneNameOf).ToArray();

            var nameCounts = new Dictionary<string, int>();
            foreach (var name in names)
            {
                nameCounts.TryGetValue(name, out int count);
                nameCounts[name] = count + 1;
            }

            var result = new string[scenePaths.Length];
            var usedLabels = new HashSet<string>();

            for (int i = 0; i < scenePaths.Length; i++)
            {
                string label = names[i];

                if (nameCounts[label] > 1)
                {
                    string parent = ParentFolderNameOf(scenePaths[i]);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        label = $"{label} — {parent}";
                    }
                }

                string unique = label;
                int suffix = 2;
                while (!usedLabels.Add(unique))
                {
                    unique = $"{label} ({suffix++})";
                }

                result[i] = unique;
            }

            return result;
        }

        private static string SceneNameOf(string scenePath)
        {
            return Path.GetFileNameWithoutExtension(scenePath);
        }

        private static string ParentFolderNameOf(string scenePath)
        {
            string dir = Path.GetDirectoryName(scenePath);
            return string.IsNullOrEmpty(dir) ? null : Path.GetFileName(dir);
        }
    }
}
