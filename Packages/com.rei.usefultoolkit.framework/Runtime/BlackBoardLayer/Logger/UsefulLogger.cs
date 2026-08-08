using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UsefulToolkit.Framework.BlackBoard
{
    public static class UsefulLogger
    {
        /// <summary>
        /// ログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">thisを指定</param>
        [Conditional("UNITY_EDITOR")]
        public static void Log(string message, Type type)
        {
            Debug.Log($"[{type.Name}]  {message}");
        }

        /// <summary>
        /// 警告ログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">thisを指定</param>
        [Conditional("UNITY_EDITOR")]
        public static void LogWarning(string message, Type type)
        {
            Debug.LogWarning($"[{type.Name}]  {message}");
        }

        /// <summary>
        /// エラーログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">thisを指定</param>
        [Conditional("UNITY_EDITOR")]
        public static void LogError(string message, Type type)
        {
            Debug.LogError($"[{type.Name}]  {message}");
        }

        /// <summary>
        /// 一時ログを出力する
        /// </summary>
        /// <param name="message">ログに出力するメッセージ</param>
        /// <param name="type">thisを指定</param>
        [Conditional("UNITY_EDITOR")]
        public static void TemporaryLog(string message, Type type)
        {
            LogColor($"[{type.Name}] {message}", Color.green);
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