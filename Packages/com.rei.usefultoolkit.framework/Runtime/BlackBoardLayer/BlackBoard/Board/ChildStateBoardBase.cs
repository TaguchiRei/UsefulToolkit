using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.Application.StateManagement;

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
        private readonly Dictionary<Type, object> _availability = new();

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
                Debug.Log("");
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
            if (state is TStateGetter stateGetter)
            {
                if (!_sceneStates.ContainsKey(typeof(TStateGetter)))
                {
                    _sceneStates.Add(typeof(TStateGetter), (stateGetter, sceneName));
                }
                else
                {
                    throw new InvalidOperationException($"ステート [{typeof(TStateGetter)}] はすでに登録されています");
                }
            }
            else
            {
                throw new ArgumentException($"ステート [{state.GetType().Name}] は [{typeof(TStateGetter)}] を実装していません。");
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
            if (state is TStateGetter stateGetter)
            {
                if (!_unRegistableStates.ContainsKey(typeof(TStateGetter)))
                {
                    _unRegistableStates.Add(typeof(TStateGetter), stateGetter);
                    return new StateDispose(() => _unRegistableStates.Remove(typeof(TStateGetter)));
                }
                else
                {
                    throw new InvalidOperationException($"ステート [{typeof(TStateGetter)}] はすでに登録されています");
                }
            }
            else
            {
                throw new ArgumentException($"ステート [{state.GetType().Name}] は [{typeof(TStateGetter)}] を実装していません。");
            }
        }

        #endregion
    }
}