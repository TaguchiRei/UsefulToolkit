using System;
using System.Collections.Generic;
using System.Linq;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// IStateContainerの実装。
    /// 型登録・取得・寿命管理を単一クラスに集約する。
    /// スレッドセーフではない(Unityのメインスレッド上での利用を前提とする)。
    /// </summary>
    public sealed class StateContainer : IStateContainer, IStateContainerLifecycle
    {
        // 具象型 -> 実体。寿命管理(Release)の一次情報源。
        private readonly Dictionary<Type, StateBase> _statesByConcreteType = new();

        // 具象型 + 実装しているIStateGetter系interface型 -> 実体。TryGetStateGetterの検索用エイリアス。
        private readonly Dictionary<Type, StateBase> _lookup = new();

        // SceneStateのみ: 具象型 -> 登録時に渡されたシーン識別値(boxedEnum)
        private readonly Dictionary<Type, Enum> _sceneOwners = new();

        public bool TryGetStateGetter<T>(out T state) where T : IStateGetter
        {
            if (_lookup.TryGetValue(typeof(T), out var found) && found is T typed)
            {
                state = typed;
                return true;
            }

            state = default;
            return false;
        }

        public bool TryRegisterGameState<T>(T state)
            where T : GameStateBase, IStateGetter
        {
            return TryRegisterInternal(state);
        }

        public bool TryRegisterSceneState<T, TSceneEnum>(T state, TSceneEnum scene)
            where T : SceneStateBase, IStateGetter
            where TSceneEnum : Enum
        {
            if (!TryRegisterInternal(state))
            {
                return false;
            }

            _sceneOwners[typeof(T)] = scene;
            return true;
        }

        public bool TryRegisterUnRegistableState<T>(T state)
            where T : UnRegistableStateBase, IStateGetter
        {
            return TryRegisterInternal(state);
        }

        public bool TryUnRegisterState<T>()
            where T : UnRegistableStateBase, IStateGetter
        {
            var concreteType = typeof(T);

            if (!_statesByConcreteType.TryGetValue(concreteType, out var state))
            {
                return false;
            }

            // 型制約上ここに来る時点でOtherのみだが、念のため防衛的にチェックしておく。
            if (state.LifeScope != StateLifeScope.Other)
            {
                return false;
            }

            RemoveInternal(concreteType, state);
            return true;
        }

        void IStateContainerLifecycle.ReleaseSceneStates<TSceneEnum>(TSceneEnum scene)
        {
            var targets = _statesByConcreteType
                .Where(kv => kv.Value.LifeScope == StateLifeScope.OnSceneEnd
                             && _sceneOwners.TryGetValue(kv.Key, out var owner)
                             && Equals(owner, scene))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var type in targets)
            {
                RemoveInternal(type, _statesByConcreteType[type]);
                _sceneOwners.Remove(type);
            }
        }

        void IStateContainerLifecycle.ReleaseGameEndStates()
        {
            var targets = _statesByConcreteType
                .Where(kv => kv.Value.LifeScope == StateLifeScope.OnGameEnd)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var type in targets)
            {
                RemoveInternal(type, _statesByConcreteType[type]);
            }
        }

        private bool TryRegisterInternal<T>(T state) where T : StateBase, IStateGetter
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var concreteType = typeof(T);
            if (_statesByConcreteType.ContainsKey(concreteType))
            {
                return false;
            }

            _statesByConcreteType[concreteType] = state;
            RegisterLookupAliases(concreteType, state);
            return true;
        }

        private void RegisterLookupAliases(Type concreteType, StateBase state)
        {
            // 具象型自身でも取得できるようにしておく
            _lookup[concreteType] = state;

            // IStateGetterを継承したインターフェース(例: IPlayerStateGetter)経由でも取得できるようにする
            foreach (var interfaceType in concreteType.GetInterfaces())
            {
                if (interfaceType == typeof(IStateGetter))
                {
                    continue;
                }

                if (typeof(IStateGetter).IsAssignableFrom(interfaceType))
                {
                    _lookup[interfaceType] = state;
                }
            }
        }

        private void RemoveInternal(Type concreteType, StateBase state)
        {
            _statesByConcreteType.Remove(concreteType);

            var aliasKeys = _lookup
                .Where(kv => ReferenceEquals(kv.Value, state))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in aliasKeys)
            {
                _lookup.Remove(key);
            }
        }
    }

    /// <summary>
    /// シーン/ゲーム終了時の一括解放など、Application層に公開しないライフサイクル制御用インターフェース。
    /// SceneLoader等、フレームワーク内部のコードのみが利用する想定。
    /// </summary>
    internal interface IStateContainerLifecycle
    {
        /// <summary>指定したシーンに紐づくSceneStateBase派生をすべて解放する</summary>
        void ReleaseSceneStates<TSceneEnum>(TSceneEnum scene) where TSceneEnum : Enum;

        /// <summary>GameStateBase派生をすべて解放する</summary>
        void ReleaseGameEndStates();
    }
}