using System;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// Dispose時に指定したActionを実行する汎用クラス。
    /// イベント購読解除など、IDisposableで解除処理を表現したい場面で利用する。
    /// </summary>
    internal sealed class DisposableAction : IDisposable
    {
        private Action _onDispose;
        private bool _disposed;

        public DisposableAction(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _onDispose?.Invoke();
            _onDispose = null;
        }
    }
}