using System;
using System.Collections.Generic;
using UsefulToolkit.Application.StateManagement;

namespace UsefulToolkit.BlackBoard
{
    /// <summary>
    /// モジュール単位でStateを登録・取得する子ボードの基底クラス。
    /// 登録・検索・待受のキーには常にState自身が実装するGetterインターフェースの型を利用する
    /// </summary>
    public abstract class ChildStateBoardBase
    {
        private readonly Dictionary<Type, IStateGetter> _states = new();

        // シーンスコープ(SceneStateBase)専用のコンテナ。どのシーンが登録したかをsceneNameで保持し、
        // OnSceneChangedで該当シーン分だけを一括Unregisterできるようにする
        private readonly Dictionary<Type, (IStateGetter State, string SceneName)> _sceneStates = new();

        // 特定の型が登録されたときに実行されるActionを保存する
        private readonly Dictionary<Type, object> _availability = new();

        /// <summary>
        /// StateをGetterインターフェース型TGetterで登録する。呼び出し例:
        /// <c>playerBoard.TryRegisterState&lt;IPlayerStateGetter&gt;(playerState);</c>
        /// 同じ型が既に登録されていれば例外を投げる。
        /// 返り値のIDisposableをDisposeすることで解除できる(スコープの管理は呼び出し側の責任)。
        /// SceneStateBaseを継承したStateはこちらではなくTryRegisterSceneStateを使うこと。
        /// </summary>
        public IDisposable TryRegisterState<TGetter>(TGetter state) where TGetter : IStateGetter
        {
            var type = typeof(TGetter);
            if (_states.ContainsKey(type) || _sceneStates.ContainsKey(type))
                throw new InvalidOperationException($"State '{type.Name}' はすでに {GetType().Name} に登録されています。");

            _states[type] = state;

            PublishAvailability(type, state);

            return new StateDispose(() => _states.Remove(type));
        }

        /// <summary>
        /// SceneStateBaseスコープのStateを、所属するシーン名(sceneName)付きで登録する。
        /// 通常のTryRegisterStateとは別のコンテナで管理され、OnSceneChanged(sceneName)で
        /// 該当シーン分のみが一括Unregisterされる(他シーンのSceneStateBase Stateには影響しない)。
        /// </summary>
        public IDisposable TryRegisterSceneState<TGetter>(TGetter state, string sceneName) where TGetter : IStateGetter
        {
            if (string.IsNullOrEmpty(sceneName))
                throw new ArgumentException("sceneNameを空にすることはできません。", nameof(sceneName));

            if (state is StateBase stateBase && stateBase.LifeScope != StateLifeScope.OnSceneEnd)
                throw new InvalidOperationException(
                    $"TryRegisterSceneStateはLifeScope.OnSceneEndのStateのみ登録できます('{typeof(TGetter).Name}'は{stateBase.LifeScope}です)。");

            var type = typeof(TGetter);
            if (_states.ContainsKey(type) || _sceneStates.ContainsKey(type))
                throw new InvalidOperationException($"State '{type.Name}' はすでに {GetType().Name} に登録されています。");

            _sceneStates[type] = (state, sceneName);

            PublishAvailability(type, state);

            return new StateDispose(() => _sceneStates.Remove(type));
        }

        /// <summary>Getterインターフェース型TGetterで登録済みのStateを取得する。</summary>
        public bool TryGetState<TGetter>(out TGetter state) where TGetter : IStateGetter
        {
            var type = typeof(TGetter);

            if (_states.TryGetValue(type, out var raw) && raw is TGetter typed)
            {
                state = typed;
                return true;
            }

            if (_sceneStates.TryGetValue(type, out var sceneEntry) && sceneEntry.State is TGetter sceneTyped)
            {
                state = sceneTyped;
                return true;
            }

            state = default;
            return false;
        }

        /// <summary>
        /// TGetter型のStateが登録された瞬間(既に登録済みなら即時)にonRegisteredを呼ぶ。
        /// 対象がUnregister→再登録を繰り返しても、呼び出し側がDisposeするまで毎回発火し続ける
        /// (1回限りの通知ではない)。設定画面用StateやAdditional SceneのStateのように
        /// 後から動的に登録されるStateを、毎フレームのポーリングなしで待ち受けられる。
        /// TryRegisterState/TryRegisterSceneStateのどちらで登録されたStateにも反応する。
        /// </summary>
        public IDisposable RegisterOnRegistered<TGetter>(Action<TGetter> onRegistered) where TGetter : IStateGetter
        {
            var type = typeof(TGetter);

            if (_states.TryGetValue(type, out var existing) && existing is TGetter typed)
                onRegistered(typed);
            else if (_sceneStates.TryGetValue(type, out var sceneEntry) && sceneEntry.State is TGetter sceneTyped)
                onRegistered(sceneTyped);

            if (!_availability.TryGetValue(type, out var raw) || raw is not EventChannel<TGetter> channel)
            {
                channel = new EventChannel<TGetter>();
                _availability[type] = channel;
            }

            return channel.Register(onRegistered);
        }

        /// <summary>
        /// 指定したsceneNameでTryRegisterSceneStateされているStateのみを一括Unregisterする。
        /// シーン管理システムが該当シーンのUnload時に呼ぶ想定——他シーンが登録した
        /// SceneStateBase Stateや、TryRegisterStateで登録された通常Stateには影響しない。
        /// </summary>
        public void OnSceneChanged(string sceneName)
        {
            List<Type> toRemove = null;

            foreach (var pair in _sceneStates)
            {
                if (pair.Value.SceneName != sceneName) continue;
                toRemove ??= new List<Type>();
                toRemove.Add(pair.Key);
            }

            if (toRemove == null) return;

            foreach (var type in toRemove)
                _sceneStates.Remove(type);
        }

        private void PublishAvailability<TGetter>(Type type, TGetter state) where TGetter : IStateGetter
        {
            if (_availability.TryGetValue(type, out var raw) && raw is EventChannel<TGetter> channel)
                channel.Publish(state);
        }
    }
}