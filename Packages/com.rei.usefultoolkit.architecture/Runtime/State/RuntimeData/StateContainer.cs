using System;
using System.Collections.Generic;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// Stateの実体を保持し、取得・イベント購読・登録解除の窓口を提供するコンテナ。
    /// </summary>
    public class StateContainer : IStateContainer
    {
        private readonly Dictionary<Type, StateBase> _states = new();

        public bool TryGetStateGetter<T>(out T state) where T : IStateGetter
        {
            if (_states.TryGetValue(typeof(T), out var stateBase) && stateBase is T typed)
            {
                state = typed;
                return true;
            }

            state = default;
            return false;
        }

        public bool TryRegisterEvent<T>(Action<IStateContainerGetter> action, StatePhase phase, out IDisposable subscription)
            where T : IStateGetter
        {
            if (_states.TryGetValue(typeof(T), out var stateBase))
            {
                subscription = stateBase.RegisterEventToState(action, phase);
                return true;
            }

            subscription = null;
            return false;
        }

        public bool TryRegisterState<TGetter, TState>(TState state)
            where TGetter : IStateGetter
            where TState : StateBase, TGetter
        {
#if UNITY_EDITOR
            if (!typeof(TState).IsNestedPrivate)
            {
                throw new InvalidOperationException(
                    $"{typeof(TState).Name} は private ネストクラスとして定義される必要があります。");
            }
#endif
            var key = typeof(TGetter);
            if (_states.ContainsKey(key))
            {
                return false;
            }

            _states[key] = state;
            return true;
        }

        public bool TryUnRegisterState<TGetter>() where TGetter : IStateGetter
        {
            var key = typeof(TGetter);
            if (!_states.TryGetValue(key, out var stateBase))
            {
                return false;
            }

            stateBase.Dispose();
            _states.Remove(key);
            return true;
        }

        /// <summary>
        /// シーン終了時などに全State を一括破棄する。
        /// IStateContainerには含めず、Initialization/Compositer層など具象型を扱える箇所からのみ呼ぶ想定。
        /// </summary>
        public void DisposeAll()
        {
            foreach (var state in _states.Values)
            {
                state.Dispose();
            }
            _states.Clear();
        }
    }
}