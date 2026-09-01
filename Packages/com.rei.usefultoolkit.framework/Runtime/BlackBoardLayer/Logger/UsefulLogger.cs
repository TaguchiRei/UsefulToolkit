using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.Logger
{
    public static class UsefulLogger
    {
        /// <summary>
        /// ログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">thisを指定。UnityEngine.Objectならコンソールのログからそのオブジェクトへ移動できる</param>
        [Conditional("UNITY_EDITOR")]
        public static void Log(string message, object type)
        {
            string formatted = $"[{type?.GetType()}]  {message}";

            if (type is UnityEngine.Object context)
            {
                Debug.Log(formatted, context);
            }
            else
            {
                Debug.Log(formatted);
            }
        }

        /// <summary>
        /// 警告ログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">thisを指定。UnityEngine.Objectならコンソールのログからそのオブジェクトへ移動できる</param>
        [Conditional("UNITY_EDITOR")]
        public static void LogWarning(string message, object type)
        {
            string formatted = $"[{type?.GetType()}]  {message}";

            if (type is UnityEngine.Object context)
            {
                Debug.LogWarning(formatted, context);
            }
            else
            {
                Debug.LogWarning(formatted);
            }
        }

        /// <summary>
        /// エラーログを出力する。エディタに加えて開発ビルドでも出力される。
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">thisを指定。UnityEngine.Objectならコンソールのログからそのオブジェクトへ移動できる</param>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void LogError(string message, object type)
        {
            string formatted = $"[{type?.GetType()}]  {message}";

            if (type is UnityEngine.Object context)
            {
                Debug.LogError(formatted, context);
            }
            else
            {
                Debug.LogError(formatted);
            }
        }

        /// <summary>
        /// staticクラスからログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">typeof(自身のクラス)を指定</param>
        [Conditional("UNITY_EDITOR")]
        public static void Log(string message, Type type)
        {
            Debug.Log($"[{type}]  {message}");
        }

        /// <summary>
        /// staticクラスから警告ログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">typeof(自身のクラス)を指定</param>
        [Conditional("UNITY_EDITOR")]
        public static void LogWarning(string message, Type type)
        {
            Debug.LogWarning($"[{type}]  {message}");
        }

        /// <summary>
        /// staticクラスからエラーログを出力する。エディタに加えて開発ビルドでも出力される。
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">typeof(自身のクラス)を指定</param>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void LogError(string message, Type type)
        {
            Debug.LogError($"[{type}]  {message}");
        }

        /// <summary>
        /// 一時ログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">thisを指定。UnityEngine.Objectならコンソールのログからそのオブジェクトへ移動できる</param>
        [Conditional("UNITY_EDITOR")]
        public static void TemporaryLog(string message, object type)
        {
            LogColor($"[{type?.GetType()}] {message}", Color.green, type as UnityEngine.Object);
        }

        /// <summary>
        /// 特定の色でログを出すためのメソッド
        /// </summary>
        /// <param name="message"></param>
        /// <param name="color"></param>
        /// <param name="context">指定するとコンソールのログからこのオブジェクトへ移動できる</param>
        [Conditional("UNITY_EDITOR")]
        private static void LogColor(string message, Color color, UnityEngine.Object context = null)
        {
            string hex = ColorUtility.ToHtmlStringRGB(color);
            string colored = $"<color=#{hex}>{message}</color>";

            if (context != null)
            {
                Debug.Log(colored, context);
            }
            else
            {
                Debug.Log(colored);
            }
        }
    }
}