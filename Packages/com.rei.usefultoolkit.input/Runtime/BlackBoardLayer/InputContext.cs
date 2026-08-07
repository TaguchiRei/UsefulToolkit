using UnityEngine.InputSystem;

namespace UsefulToolkit.Input
{
    /// <summary>
    /// InputBoardのチャンネルを流れるペイロード。InputActionの1コールバック分の
    /// phaseと値を運ぶ。EventChannel&lt;InputContext&lt;TValue&gt;&gt;を通じてApplicationへ届く。
    /// </summary>
    public readonly struct InputContext<TValue> where TValue : unmanaged
    {
        public InputActionPhase Phase { get; }
        public TValue Value { get; }

        public bool IsStarted => Phase == InputActionPhase.Started;
        public bool IsPerformed => Phase == InputActionPhase.Performed;
        public bool IsCanceled => Phase == InputActionPhase.Canceled;

        public InputContext(InputActionPhase phase, TValue value)
        {
            Phase = phase;
            Value = value;
        }
    }
}
