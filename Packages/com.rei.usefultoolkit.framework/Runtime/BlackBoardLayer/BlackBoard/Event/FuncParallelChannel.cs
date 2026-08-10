using System;
using System.Collections.Generic;

namespace UsefulToolkit.Framework.BlackBoard
{
    public class FuncParallelChannel<TArgument, TReturnValue> : IFuncParallelChannel<TArgument, TReturnValue>
    {
        private readonly List<Func<TArgument, TReturnValue>> _callbacks;

        public IDisposable Register(Func<TArgument, TReturnValue> handler)
        {
            _callbacks.Add(handler);
            return new BoardDispose(() => _callbacks.Remove(handler));
        }

        public TReturnValue[] Publish(TArgument argument)
        {
            var result = new TReturnValue[_callbacks.Count];

            for (int i = 0; i < _callbacks.Count; i++)
            {
                result[i] = _callbacks[i](argument);
            }

            return result;
        }
    }
}