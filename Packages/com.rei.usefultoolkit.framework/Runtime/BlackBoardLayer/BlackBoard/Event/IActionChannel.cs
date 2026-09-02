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
        /// <param name="handler">登録するハンドラ</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        /// <exception cref="ArgumentNullException">handlerがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">同じハンドラが既に登録されているときに出力</exception>
        IDisposable Register(Action<TPayload> handler);
    }
}