using System;

namespace UsefulToolkit.Framework.BlackBoard
{
    public class BoardDispose : IDisposable
    {
        private Action _disposeAction;

        public BoardDispose(Action disposeAction)
        {
            _disposeAction = disposeAction;
        }

        public void Dispose()
        {
            _disposeAction?.Invoke();
            _disposeAction = null;
        }
    }
}