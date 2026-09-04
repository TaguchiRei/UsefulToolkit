namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力コールバックが発生した段階。
    ///
    /// エンジン側のphase型をそのままペイロードに載せるとBlackBoardLayerが
    /// InputSystemへ依存してしまうため、この層独自の型として定義している。
    /// EngineAdapterLayerがエンジンの型からこの型へ変換する。
    /// </summary>
    public enum InputPhase
    {
        /// <summary> 入力を受け付けていない </summary>
        Disabled,

        /// <summary> 入力待ち </summary>
        Waiting,

        /// <summary> 入力が始まった </summary>
        Started,

        /// <summary> 入力が成立した </summary>
        Performed,

        /// <summary> 入力が打ち切られた </summary>
        Canceled,
    }
}
