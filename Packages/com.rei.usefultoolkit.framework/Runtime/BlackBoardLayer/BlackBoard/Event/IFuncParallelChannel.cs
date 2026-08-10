using System;
using UnityEngine;

namespace UsefulToolkit.Framework.BlackBoard
{
    public interface IFuncParallelChannel<TArgument, TReturnValue> : IEvent
    {
        IDisposable Register(Func<TArgument, TReturnValue> handler);
    }
}