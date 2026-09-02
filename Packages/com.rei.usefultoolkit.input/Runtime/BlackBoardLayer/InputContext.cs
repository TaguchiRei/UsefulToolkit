namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力のチャンネルを流れるペイロード。1コールバック分のphaseと値を運ぶ。
    /// </summary>
    public readonly struct InputContext<TValue> where TValue : unmanaged
    {
        public InputPhase Phase { get; }
        public TValue Value { get; }

        public bool IsStarted => Phase == InputPhase.Started;
        public bool IsPerformed => Phase == InputPhase.Performed;
        public bool IsCanceled => Phase == InputPhase.Canceled;

        public InputContext(InputPhase phase, TValue value)
        {
            Phase = phase;
            Value = value;
        }
    }
}
