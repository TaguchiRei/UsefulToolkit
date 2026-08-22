using System;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// EventBoard側の購読インターフェース。
    /// </summary>
    public interface IActionChannel<TPayload> : IEvent
    {
        /// <summary>
        /// ハンドラを登録する。返り値のIDisposableをDisposeすることで解除する。
        /// </summary>
        IDisposable Register(Action<TPayload> handler);
    }
}