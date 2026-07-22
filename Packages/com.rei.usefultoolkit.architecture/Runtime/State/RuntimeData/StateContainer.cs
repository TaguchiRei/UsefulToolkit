using System;
using System.Collections.Generic;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// Stateの実体を保持し、取得・イベント購読・登録解除の窓口を提供するコンテナ。
    /// </summary>
    public class StateContainer : IStateContainer
    {
        public bool TryGetStateGetter<T>(out T state) where T : IStateGetter
        {
            throw new NotImplementedException();
        }

        public bool TryRegisterEvent<T>(Action<IStateContainerGetter> action, StatePhase phase, out IDisposable subscription) where T : IStateGetter
        {
            throw new NotImplementedException();
        }

        public bool TryRegisterState<TGetter, TState>(TState state) where TGetter : IStateGetter where TState : StateBase, TGetter
        {
            throw new NotImplementedException();
        }

        public bool TryUnRegisterState<TGetter>() where TGetter : IStateGetter
        {
            throw new NotImplementedException();
        }
    }
}