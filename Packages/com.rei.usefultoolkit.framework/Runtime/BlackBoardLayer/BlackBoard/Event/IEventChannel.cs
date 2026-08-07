using System;

namespace UsefulToolkit.BlackBoard
{
    /// <summary>
    /// EventBoard側の購読インターフェース。
    /// Register/Unregisterのみを公開し、public event Actionを直接公開しない
    /// (ラムダ登録時の解除漏れ事故を防ぐため)。
    /// </summary>
    public interface IEventChannel<TPayload>
    {
        /// <summary>
        /// ハンドラを登録する。返り値のIDisposableをDisposeすることで解除する。
        /// </summary>
        IDisposable Register(Action<TPayload> handler);
    }
}
