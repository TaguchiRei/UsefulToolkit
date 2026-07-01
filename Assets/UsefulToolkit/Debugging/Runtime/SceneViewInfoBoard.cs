using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UsefulToolkit.Debugging
{
    using Debug = UnityEngine.Debug;

    public class SceneViewInfoBoard : MonoBehaviour
    {
        public string Title { get; private set; }
        public string Description { get; private set; }
        public Color BoardColor { get; private set; } = new(0.1f, 0.1f, 0.1f, 0.8f);
        
        private readonly List<string> _staticInfos = new();
        
        // 毎フレーム監視して更新する動的な情報リスト
        private readonly List<Func<string>> _observedInfos = new();

        public static SceneViewInfoBoard Setup(
            GameObject targetObject,
            string title,
            string description,
            Color boardColor = default)
        {
            if (targetObject == null) throw new ArgumentNullException(nameof(targetObject));
            if (string.IsNullOrEmpty(title))
            {
                Debug.LogWarning("Title is null or empty");
                return null;
            }

            var info = targetObject.AddComponent<SceneViewInfoBoard>();
            info.Title = title;
            info.Description = description;
            
            if (boardColor != default)
            {
                info.BoardColor = boardColor;
            }

            return info;
        }

        public void AddInfo(string info)
        {
            if (!string.IsNullOrEmpty(info))
            {
                _staticInfos.Add(info);
            }
        }

        public void ObserveInfo(Func<string> func)
        {
            if (func != null)
            {
                _observedInfos.Add(func);
            }
        }

        /// <summary>
        /// エディタスクリプトから呼び出して、表示する最終的な文字列を取得する
        /// </summary>
        public string GetFormattedText()
        {
            var sb = new StringBuilder();
            
            if (!string.IsNullOrEmpty(Description))
            {
                sb.AppendLine(Description);
            }
            
            foreach (var info in _staticInfos)
            {
                sb.AppendLine($"- {info}");
            }
            
            
            foreach (var func in _observedInfos)
            {
                try
                {
                    var val = func.Invoke();
                    sb.AppendLine($"> {val}");
                }
                catch (Exception e)
                {
                    sb.AppendLine($"> [Error: {e.Message}]");
                }
            }

            return sb.ToString().TrimEnd();
        }
    }
}