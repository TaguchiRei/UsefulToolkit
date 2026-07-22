using System;
using UnityEngine;

namespace UsefulToolkit.Application.StateManagement
{
    public readonly struct StateContext<TValue>
    {
        public IStateContainerGetter StateContainerGetter { get; }

        public TValue NewValue { get; }

        public TValue OldValue { get; }

        public StateContext(IStateContainerGetter stateContainerGetter, TValue oldValue, TValue newValue)
        {
            StateContainerGetter = stateContainerGetter;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }
}