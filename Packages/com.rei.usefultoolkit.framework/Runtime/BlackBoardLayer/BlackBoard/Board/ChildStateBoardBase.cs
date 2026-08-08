using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UsefulToolkit.Application.StateManagement;
using UsefulToolkit.Framework.BlackBoard;

namespace UsefulToolkit.BlackBoard
{
    /// <summary>
    /// モジュール単位でStateを登録・取得する子ボードの基底クラス。
    /// 登録・検索・待受のキーには常にState自身が実装するGetterインターフェースの型を利用する
    /// </summary>
    public abstract class ChildStateBoardBase
    {
        /// <summary> ゲーム全体を通して保持されるステートの参照インターフェースを保持するコンテナ </summary>
        private readonly Dictionary<Type, IStateGetter> _gameStates = new();

        /// <summary> シーンに依存するステートの参照インターフェースのコンテナ </summary>
        private readonly Dictionary<Type, (IStateGetter State, string SceneName)> _sceneStates = new();

        /// <summary> 登録解除可能なステートの参照インターフェースを保持するコンテナ </summary>
        private readonly Dictionary<Type, IStateGetter> _unRegistableStates = new();

        /// <summary> 特定のステートが登録されたときに実行されるアクション </summary>
        private readonly Dictionary<Type, List<Action>> _availability = new();

        #region Register系メソッド

        /// <summary>
        /// ゲーム終了時まで破棄されないStateを登録するメソッド。
        /// </summary>
        /// <param name="state">登録するステート</param>
        /// <typeparam name="TStateGetter">ステートのGetterインターフェース</typeparam>
        /// <exception cref="InvalidOperationException">すでにそのステートが登録されているときに出力</exception>
        /// <exception cref="ArgumentException">指定されたステートがStateGetterを実装していないときに出力</exception>
        public void RegisterGameState<TStateGetter>(GameStateBase state) where TStateGetter : IStateGetter
        {
            if (state is not TStateGetter stateGetter)
            {
                throw new ArgumentException($"ステート [{state.GetType().Name}] は [{typeof(TStateGetter)}] を実装していません。");
            }

            if (_gameStates.TryAdd(typeof(TStateGetter), stateGetter))
            {
                UsefulLogger.Log($"[{state.GetType().Name}]が登録されました。", GetType());
            }
            else
            {
                throw new InvalidOperationException($"ステート [{typeof(TStateGetter)}] はすでに登録されています");
            }
        }

        /// <summary>
        /// シーンアンロードまで破棄されないステートを登録するメソッド。
        /// </summary>
        /// <param name="state">登録するステート</param>
        /// <param name="sceneName">依存するシーン名</param>
        /// <typeparam name="TStateGetter">ステートのGetterインターフェース</typeparam>
        /// <exception cref="InvalidOperationException">すでにそのステートが登録されているときに出力</exception>
        /// <exception cref="ArgumentException">指定されたステートがStateGetterを実装していないときに出力</exception>
        public void RegisterSceneState<TStateGetter>(SceneStateBase state, string sceneName)
            where TStateGetter : IStateGetter
        {
            if (string.IsNullOrEmpty(sceneName)) throw new ArgumentException("シーン名はnullにはできません");

            if (state is not TStateGetter stateGetter)
            {
                throw new ArgumentException($"ステート [{state.GetType().Name}] は [{typeof(TStateGetter)}] を実装していません。");
            }

            if (_sceneStates.TryAdd(typeof(TStateGetter), (stateGetter, sceneName)))
            {
                UsefulLogger.Log($"[{state.GetType().Name}]が登録されました。", GetType());
            }
            else
            {
                throw new InvalidOperationException($"ステート [{typeof(TStateGetter)}] はすでに登録されています");
            }
        }

        /// <summary>
        /// 登録解除可能なステートを登録する。
        /// </summary>
        /// <param name="state">登録するステート</param>
        /// <typeparam name="TStateGetter">ステートのGetterインターフェース</typeparam>
        /// <returns>登録解除のためのインターフェース</returns>
        /// <exception cref="InvalidOperationException">すでにそのステートが登録されているときに出力</exception>
        /// <exception cref="ArgumentException">指定されたステートがStateGetterを実装していないときに出力</exception>
        public IDisposable RegisterUnRegistableState<TStateGetter>(UnRegistableStateBase state)
            where TStateGetter : IStateGetter
        {
            if (state is not TStateGetter stateGetter)
            {
                throw new ArgumentException($"ステート [{state.GetType().Name}] は [{typeof(TStateGetter)}] を実装していません。");
            }

            if (_unRegistableStates.TryAdd(typeof(TStateGetter), stateGetter))
            {
                UsefulLogger.Log($"[{state.GetType().Name}]が登録されました。", GetType());
                return new StateDispose(() => _unRegistableStates.Remove(typeof(TStateGetter)));
            }

            throw new InvalidOperationException($"ステート [{typeof(TStateGetter)}] はすでに登録されています");
        }

        #endregion

        #region Utilityメソッド

        public bool CheckRegisterState<TState>(TState type) where TState : StateBase, IStateGetter
        {
            switch (type.LifeScope)
            {
                case StateLifeScope.OnGameEnd:
                    return _gameStates.ContainsKey(typeof(TState));
                case StateLifeScope.OnSceneEnd:
                    return _sceneStates.ContainsKey(typeof(TState));
                default:
                    return _unRegistableStates.ContainsKey(typeof(TState));
            }
        }

        public bool CheckRegisterGameState<TState>(TState type) where TState : GameStateBase, IStateGetter
        {
            return _gameStates.ContainsKey(typeof(TState));
        }

        public bool CheckRegisterSceneState<TState>(TState type) where TState : IStateGetter
        {
            return _sceneStates.ContainsKey(typeof(TState));
        }

        public bool CheckRegisterUnRegistableState<TState>(TState type) where TState : IStateGetter
        {
            return _unRegistableStates.ContainsKey(typeof(TState));
        }

        /// <summary>
        /// 特定のステートが登録された際に実行されるActionを登録するメソッド
        /// </summary>
        /// <param name="action">登録するAction</param>
        /// <typeparam name="TStateGetter"></typeparam>
        public IDisposable SubscribeStateRegister<TStateGetter>(Action action) where TStateGetter : IStateGetter
        {
            if (action is null) throw new ArgumentNullException(nameof(action));
            if (_availability.ContainsKey(typeof(TStateGetter)))
            {
                _availability[typeof(TStateGetter)].Add(action);
                return new StateDispose(() => { _availability[typeof(TStateGetter)].Remove(action); });
            }

            throw new InvalidOperationException($"[{typeof(TStateGetter)}]は未登録です");
        }

        #endregion


        /// <summary>
        /// シーン変更時にそのシーンのStateの登録を解除するためのメソッド
        /// </summary>
        /// <param name="sceneName"></param>
        internal void OnSceneChanged(string sceneName)
        {
            var keys = _sceneStates
                .Where(x => x.Value.SceneName == sceneName)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in keys)
            {
                _sceneStates.Remove(key);
            }
        }
    }
}