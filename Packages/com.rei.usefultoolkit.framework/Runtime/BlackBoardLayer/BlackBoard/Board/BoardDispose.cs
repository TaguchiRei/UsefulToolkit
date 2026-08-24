using System;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    public class BoardDispose : IDisposable
    {
        public static readonly BoardDispose Empty = new(null);
        
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