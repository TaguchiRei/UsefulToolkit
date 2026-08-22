using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;
using UsefulToolkit.BlackBoard.Logger;

namespace UsefulToolkit.BlackBoard.BlackBoard
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

#if UNITY_EDITOR
            var att = state.GetType().GetCustomAttribute<RegisterBoardAttribute>();
            if (att != null)
            {
                if (att.BoardType != GetType())
                {
                    throw new InvalidOperationException(
                        $"ステート[{state.GetType().Name}]　は[{GetType().Name}]に登録できません。正しい登録先は[{att.BoardType.Name}]です。");
                }
            }
            else
            {
                return;
            }
#endif
            if (_gameStates.TryAdd(typeof(TStateGetter), stateGetter))
            {
                UsefulLogger.Log($"[{state.GetType().Name}] を [{typeof(TStateGetter).Name}] として登録しました。", this);
                OnRegisterdState<TStateGetter>();
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
                UsefulLogger.Log($"[{state.GetType().Name}] を [{typeof(TStateGetter).Name}] として登録しました。", this);
                OnRegisterdState<TStateGetter>();
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
                UsefulLogger.Log($"[{state.GetType().Name}] を [{typeof(TStateGetter).Name}] として登録しました。", this);
                OnRegisterdState<TStateGetter>();
                return new BoardDispose(() => _unRegistableStates.Remove(typeof(TStateGetter)));
            }

            throw new InvalidOperationException($"ステート [{typeof(TStateGetter)}] はすでに登録されています");
        }

        #endregion

        #region 取得用メソッド

        public bool TryGetGameState<TStateGetter>(out TStateGetter state) where TStateGetter : IStateGetter
        {
            var result = _gameStates.TryGetValue(typeof(TStateGetter), out var stateGetter);

            if (result)
            {
                state = (TStateGetter)stateGetter;
            }
            else
            {
                state = default;
            }

            return result;
        }

        public bool TryGetSceneState<TStateGetter>(out TStateGetter state, out string sceneName)
            where TStateGetter : IStateGetter
        {
            var result = _sceneStates.TryGetValue(typeof(TStateGetter), out var stateGetter);

            if (result)
            {
                state = (TStateGetter)stateGetter.State;
                sceneName = stateGetter.SceneName;
            }
            else
            {
                sceneName = null;
                state = default;
            }

            return result;
        }

        public bool TryGetUnRegistableState<TStateGetter>(out TStateGetter state) where TStateGetter : IStateGetter
        {
            var result = _unRegistableStates.TryGetValue(typeof(TStateGetter), out var stateGetter);

            if (result)
            {
                state = (TStateGetter)stateGetter;
            }
            else
            {
                state = default;
            }

            return result;
        }

        #endregion

        #region Utilityメソッド

        public bool CheckRegisterGameState<TStateGetter>() where TStateGetter : IStateGetter
        {
            return _gameStates.ContainsKey(typeof(TStateGetter));
        }

        public bool CheckRegisterSceneState<TStateGetter>() where TStateGetter : IStateGetter
        {
            return _sceneStates.ContainsKey(typeof(TStateGetter));
        }

        public bool CheckRegisterUnRegistableState<TStateGetter>() where TStateGetter : IStateGetter
        {
            return _unRegistableStates.ContainsKey(typeof(TStateGetter));
        }

        /// <summary>
        /// 特定のステートが登録された際に実行されるActionを登録するメソッド
        /// GameStateに紐づけて登録する場合、基本的にGameStateは初期化時に登録されるため、実行されない可能性があります。
        /// その場合はinvokeIfRegisteredをtrueにすることで、登録済みならこの場で1回発火させられます。
        /// </summary>
        /// <param name="action">登録するAction</param>
        /// <param name="invokeIfRegistered">trueの場合、TStateGetterがすでに登録済みならその場で1回発火する</param>
        /// <typeparam name="TStateGetter"></typeparam>
        /// <exception cref="ArgumentNullException">actionがnullのときに出力</exception>
        public IDisposable SubscribeStateRegister<TStateGetter>(Action action, bool invokeIfRegistered = false)
            where TStateGetter : IStateGetter
        {
            if (action is null) throw new ArgumentNullException(nameof(action));

            if (!_availability.TryGetValue(typeof(TStateGetter), out var list))
            {
                list = new List<Action>();
                _availability[typeof(TStateGetter)] = list;
            }

            list.Add(action);

            // 待受を始める前に登録が済んでいたケースを拾うための即時発火
            if (invokeIfRegistered && IsRegistered<TStateGetter>())
            {
                action.Invoke();
            }

            return new BoardDispose(() => list.Remove(action));
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

        private bool IsRegistered<TStateGetter>() where TStateGetter : IStateGetter
        {
            var type = typeof(TStateGetter);
            return _gameStates.ContainsKey(type)
                   || _sceneStates.ContainsKey(type)
                   || _unRegistableStates.ContainsKey(type);
        }

        private void OnRegisterdState<TStateGetter>()
        {
            if (_availability.TryGetValue(typeof(TStateGetter), out var list))
            {
                foreach (var action in list.ToArray())
                {
                    action?.Invoke();
                }
            }
        }
    }
}