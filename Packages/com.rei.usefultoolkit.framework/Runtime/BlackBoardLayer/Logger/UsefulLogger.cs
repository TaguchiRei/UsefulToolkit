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
        /// <param name="type">thisを指定</param>
        [Conditional("UNITY_EDITOR")]
        public static void Log(string message, object type)
        {
            Debug.Log($"[{type.GetType()}]  {message}");
        }

        /// <summary>
        /// 警告ログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">thisを指定</param>
        [Conditional("UNITY_EDITOR")]
        public static void LogWarning(string message, object type)
        {
            Debug.LogWarning($"[{type.GetType()}]  {message}");
        }

        /// <summary>
        /// エラーログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">thisを指定</param>
        [Conditional("UNITY_EDITOR")]
        public static void LogError(string message, object type)
        {
            Debug.LogError($"[{type.GetType()}]  {message}");
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
        /// staticクラスからエラーログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">typeof(自身のクラス)を指定</param>
        [Conditional("UNITY_EDITOR")]
        public static void LogError(string message, Type type)
        {
            Debug.LogError($"[{type}]  {message}");
        }

        /// <summary>
        /// 一時ログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">thisを指定</param>
        [Conditional("UNITY_EDITOR")]
        public static void TemporaryLog(string message, object type)
        {
            LogColor($"[{type.GetType()}] {message}", Color.green);
        }

        /// <summary>
        /// 特定の色でログを出すためのメソッド
        /// </summary>
        /// <param name="message"></param>
        /// <param name="color"></param>
        [Conditional("UNITY_EDITOR")]
        private static void LogColor(string message, Color color)
        {
            string hex = ColorUtility.ToHtmlStringRGB(color);
            Debug.Log($"<color=#{hex}>{message}</color>");
        }
    }
}