using System;

namespace UsefulToolkit.BlackBoard
{
    internal class StateDispose : IDisposable
    {
        private Action _disposeAction;

        public StateDispose(Action disposeAction)
        {
            _disposeAction = disposeAction;
        }

        public void Dispose()
        {
            _disposeAction();
            _disposeAction = null;
        }
    }
}