using System;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// EventBoard側の購読インターフェース。
    /// 前のハンドラの戻り値を次のハンドラへ渡していく、値の加工チェーン用。
    /// </summary>
    public interface IFuncChainChannel<TPayload> : IEvent
    {
        /// <summary>
        /// ハンドラを登録する。返り値のIDisposableをDisposeすることで解除する。
        /// priorityは値が小さいものから先に適用され、同じ優先度同士は登録順に適用される。
        /// </summary>
        IDisposable Register(Func<TPayload, TPayload> handler, int priority = 0);
    }
}
