using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Editor.Utility
{
    /// <summary>
    /// ダイアログやファイル選択パネルを出さずにエディタツールを動かすAIモードの状態と、
    /// ダイアログの代わりに使う回答を保持する。状態と回答は <see cref="SessionState"/> に置くため、
    /// ドメインリロードをまたいで残り、エディタを再起動すると消える。
    ///
    /// AIからの使い方 : <see cref="Enabled"/> を true にし、<see cref="SetAnswer"/> で回答を登録してから
    /// <c>EditorApplication.ExecuteMenuItem</c> でメニューを実行する。回答が必要なのに未登録の場合、
    /// ツールは中止され、登録すべきキーがエラーログに出る。
    /// </summary>
    public static class AiMode
    {
        private const string MenuPath = "UsefulToolkit/AI Mode";
        private const string EnabledKey = "UsefulToolkit.AiMode.Enabled";
        private const string AnswerKeyPrefix = "UsefulToolkit.AiMode.Answer.";
        private const string AnswerKeyListKey = "UsefulToolkit.AiMode.AnswerKeys";
        private const char AnswerKeySeparator = '\n';

        /// <summary>AIモードが有効か。</summary>
        public static bool Enabled
        {
            get => SessionState.GetBool(EnabledKey, false);
            set
            {
                if (Enabled == value) return;

                SessionState.SetBool(EnabledKey, value);
                Debug.Log($"[UsefulToolkit] AIモードを{(value ? "有効" : "無効")}にしました。");
            }
        }

        /// <summary>
        /// ダイアログの代わりに使う回答を登録する。同じキーに登録済みなら上書きする。
        /// </summary>
        /// <param name="key">回答のキー。ダイアログを出す箇所ごとに決まっている</param>
        /// <param name="value">回答。確認ならボタン名かtrue/false、選択肢ならボタン名、パスならパス。空文字は不可</param>
        public static void SetAnswer(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(value));

            SessionState.SetString(AnswerKeyPrefix + key, value);

            var keys = LoadAnswerKeys();
            if (!keys.Contains(key))
            {
                keys.Add(key);
                SessionState.SetString(AnswerKeyListKey, string.Join(AnswerKeySeparator.ToString(), keys));
            }
        }

        /// <summary>
        /// 登録済みの回答を取得する。
        /// </summary>
        /// <param name="key">回答のキー</param>
        /// <param name="value">登録済みの回答</param>
        /// <returns>登録されていればtrue</returns>
        public static bool TryGetAnswer(string key, out string value)
        {
            // SessionState.GetString は未登録のキーに対して既定値ではなく空文字を返すため、空文字を未登録として扱う
            value = SessionState.GetString(AnswerKeyPrefix + key, string.Empty);
            return value.Length > 0;
        }

        /// <summary>登録済みの回答を全て消す。</summary>
        public static void ClearAnswers()
        {
            foreach (var key in LoadAnswerKeys())
            {
                SessionState.EraseString(AnswerKeyPrefix + key);
            }

            SessionState.EraseString(AnswerKeyListKey);
        }

        /// <summary>登録済みの回答のキーを読み出す。</summary>
        private static List<string> LoadAnswerKeys()
        {
            return SessionState.GetString(AnswerKeyListKey, string.Empty)
                .Split(new[] { AnswerKeySeparator }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        /// <summary>AIモードの有効/無効を切り替える。</summary>
        [MenuItem(MenuPath, false, 1)]
        private static void Toggle()
        {
            Enabled = !Enabled;
        }

        /// <summary>メニューのチェック表示をAIモードの状態に合わせる。</summary>
        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}
