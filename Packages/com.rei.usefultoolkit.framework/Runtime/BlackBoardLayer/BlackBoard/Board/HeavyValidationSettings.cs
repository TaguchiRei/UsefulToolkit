namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// リフレクション等を使う重いバリデーション処理を一括で有効化/無効化するための設定。
    /// エディタ上でのテスト時に無効化できるよう、EditorPrefsで永続化する。
    /// </summary>
    public static class HeavyValidationSettings
    {
#if UNITY_EDITOR
        private const string Key = "UsefulToolkit.HeavyValidation.Enabled";

        public static bool Enabled
        {
            get => UnityEditor.EditorPrefs.GetBool(Key, true);
            set => UnityEditor.EditorPrefs.SetBool(Key, value);
        }
#else
        public static bool Enabled => false;
#endif
    }
}
