namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// <see cref="InputState"/>の内容をゲームエンジン側へ反映し、現在の入力値を読み出すための橋渡し。
    /// EngineServiceLayerのクラスが実装し、Stateを生成したクラスが
    /// <see cref="InputState.RegisterInputEngine"/>で繋ぐ。
    /// </summary>
    public interface IInputEngineBridge
    {
        /// <summary>
        /// 指定したActionの現在値を読み出す。
        /// </summary>
        /// <param name="map">ActionMap名</param>
        /// <param name="action">Action名</param>
        InputContext<TValue> ReadValue<TValue>(string map, string action) where TValue : unmanaged;

        /// <summary>
        /// 有効なActionMapを指定された内容へ揃える。列挙に含まれないActionMapは無効化する。
        /// </summary>
        /// <param name="activeActionMaps">有効にするActionMap名</param>
        void ApplyActiveActionMaps(System.Collections.Generic.IReadOnlyList<string> activeActionMaps);

        /// <summary>
        /// 入力全体の有効・無効を反映する。
        /// </summary>
        /// <param name="inputEnabled">入力を受け付けるか</param>
        void ApplyInputEnabled(bool inputEnabled);
    }
}
