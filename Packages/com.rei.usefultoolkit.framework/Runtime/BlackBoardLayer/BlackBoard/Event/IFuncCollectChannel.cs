using System;

namespace UsefulToolkit.Framework.BlackBoard
{
    /// <summary>
    /// EventBoard側の購読インターフェース。
    /// 全ハンドラへ同じ引数を渡し、それぞれの戻り値をまとめて受け取る問い合わせ用。
    /// </summary>
    public interface IFuncCollectChannel<TArgument, TReturnValue> : IEvent
    {
        /// <summary>
        /// ハンドラを登録する。返り値のIDisposableをDisposeすることで解除する。
        /// </summary>
        IDisposable Register(Func<TArgument, TReturnValue> handler);
    }
}
