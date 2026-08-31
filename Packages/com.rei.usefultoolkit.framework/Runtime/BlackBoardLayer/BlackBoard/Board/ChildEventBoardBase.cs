using System.Collections.Generic;
using System.Linq;
using System;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.BlackBoard.Scene;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// モジュール単位でイベントチャンネルを登録・取得する子ボードの基底クラス。
    /// 登録・検索・待受のキーには常にチャンネルが実装するIEvent派生インターフェースの型を利用する。
    /// ChildStateBoardBaseとは保存領域を共有しない——ChildBoardはState用かEvent用のどちらかであり、
    /// 両方を兼ねることはない。
    /// </summary>
    public abstract class ChildEventBoardBase
    {
        /// <summary> ゲーム全体を通して保持されるイベントの購読インターフェースを保持するコンテナ </summary>
        private readonly Dictionary<Type, IEvent> _gameEvents = new();

        /// <summary> シーンに依存するイベントの購読インターフェースのコンテナ </summary>
        private readonly Dictionary<Type, (IEvent Event, int SceneId)> _sceneEvents = new();

        /// <summary> 登録解除可能なイベントの購読インターフェースを保持するコンテナ </summary>
        private readonly Dictionary<Type, IEvent> _unRegistableEvents = new();

        /// <summary> 特定のイベントが登録されたときに実行されるアクション </summary>
        private readonly Dictionary<Type, List<Action>> _availability = new();

        #region Register系メソッド

        /// <summary>
        /// ゲーム終了時まで破棄されないイベントを登録するメソッド。
        /// </summary>
        /// <param name="channel">登録するイベントチャンネル</param>
        /// <typeparam name="TEvent">チャンネルの購読インターフェース</typeparam>
        /// <exception cref="ArgumentNullException">channelがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">すでにそのイベントが登録されているときに出力</exception>
        /// <exception cref="ArgumentException">指定されたチャンネルがTEventを実装していないときに出力</exception>
        public void RegisterGameEvent<TEvent>(IEvent channel) where TEvent : IEvent
        {
            if (channel is null) throw new ArgumentNullException(nameof(channel));

            if (channel is not TEvent eventChannel)
            {
                throw new ArgumentException($"イベント [{channel.GetType().Name}] は [{typeof(TEvent)}] を実装していません。");
            }

            if (_gameEvents.TryAdd(typeof(TEvent), eventChannel))
            {
                UsefulLogger.Log($"[{channel.GetType().Name}] を [{typeof(TEvent).Name}] として登録しました。", this);
                OnRegisterdEvent<TEvent>();
            }
            else
            {
                throw new InvalidOperationException($"イベント [{typeof(TEvent)}] はすでに登録されています");
            }
        }

        /// <summary>
        /// シーンアンロードまで破棄されないイベントを登録するメソッド。
        /// </summary>
        /// <param name="channel">登録するイベントチャンネル</param>
        /// <param name="sceneId">依存するシーンID</param>
        /// <typeparam name="TEvent">チャンネルの購読インターフェース</typeparam>
        /// <exception cref="ArgumentNullException">channelがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">すでにそのイベントが登録されているときに出力</exception>
        /// <exception cref="ArgumentException">指定されたチャンネルがTEventを実装していないときに出力</exception>
        public void RegisterSceneEvent<TEvent>(IEvent channel, int sceneId) where TEvent : IEvent
        {
            if (channel is null) throw new ArgumentNullException(nameof(channel));

            if (sceneId < 0)
            {
                UsefulLogger.LogWarning(
                    $"シーンID{sceneId}はビルドに含まれない開発専用シーンです。" +
                    $"イベント [{typeof(TEvent).Name}] をこのシーンスコープのまま登録します。", this);
            }

            if (channel is not TEvent eventChannel)
            {
                throw new ArgumentException($"イベント [{channel.GetType().Name}] は [{typeof(TEvent)}] を実装していません。");
            }

            if (_sceneEvents.TryAdd(typeof(TEvent), (eventChannel, sceneId)))
            {
                UsefulLogger.Log($"[{channel.GetType().Name}] を [{typeof(TEvent).Name}] として登録しました。", this);
                OnRegisterdEvent<TEvent>();
            }
            else
            {
                throw new InvalidOperationException($"イベント [{typeof(TEvent)}] はすでに登録されています");
            }
        }

        /// <summary>
        /// 登録解除可能なイベントを登録する。
        /// </summary>
        /// <param name="channel">登録するイベントチャンネル</param>
        /// <typeparam name="TEvent">チャンネルの購読インターフェース</typeparam>
        /// <returns>登録解除のためのインターフェース</returns>
        /// <exception cref="ArgumentNullException">channelがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">すでにそのイベントが登録されているときに出力</exception>
        /// <exception cref="ArgumentException">指定されたチャンネルがTEventを実装していないときに出力</exception>
        public IDisposable RegisterUnRegistableEvent<TEvent>(IEvent channel) where TEvent : IEvent
        {
            if (channel is null) throw new ArgumentNullException(nameof(channel));

            if (channel is not TEvent eventChannel)
            {
                throw new ArgumentException($"イベント [{channel.GetType().Name}] は [{typeof(TEvent)}] を実装していません。");
            }

            if (_unRegistableEvents.TryAdd(typeof(TEvent), eventChannel))
            {
                UsefulLogger.Log($"[{channel.GetType().Name}] を [{typeof(TEvent).Name}] として登録しました。", this);
                OnRegisterdEvent<TEvent>();
                return new BoardDispose(() => _unRegistableEvents.Remove(typeof(TEvent)));
            }

            throw new InvalidOperationException($"イベント [{typeof(TEvent)}] はすでに登録されています");
        }

        #endregion

        #region 取得用メソッド

        public bool TryGetGameEvent<TEvent>(out TEvent channel) where TEvent : IEvent
        {
            var result = _gameEvents.TryGetValue(typeof(TEvent), out var eventChannel);

            if (result)
            {
                channel = (TEvent)eventChannel;
            }
            else
            {
                channel = default;
            }

            return result;
        }

        public bool TryGetSceneEvent<TEvent>(out TEvent channel, out int sceneId) where TEvent : IEvent
        {
            var result = _sceneEvents.TryGetValue(typeof(TEvent), out var eventChannel);

            if (result)
            {
                channel = (TEvent)eventChannel.Event;
                sceneId = eventChannel.SceneId;
            }
            else
            {
                sceneId = SceneState.NoSceneId;
                channel = default;
            }

            return result;
        }

        public bool TryGetUnRegistableEvent<TEvent>(out TEvent channel) where TEvent : IEvent
        {
            var result = _unRegistableEvents.TryGetValue(typeof(TEvent), out var eventChannel);

            if (result)
            {
                channel = (TEvent)eventChannel;
            }
            else
            {
                channel = default;
            }

            return result;
        }

        #endregion

        #region Utilityメソッド

        public bool CheckRegisterGameEvent<TEvent>() where TEvent : IEvent
        {
            return _gameEvents.ContainsKey(typeof(TEvent));
        }

        public bool CheckRegisterSceneEvent<TEvent>() where TEvent : IEvent
        {
            return _sceneEvents.ContainsKey(typeof(TEvent));
        }

        public bool CheckRegisterUnRegistableEvent<TEvent>() where TEvent : IEvent
        {
            return _unRegistableEvents.ContainsKey(typeof(TEvent));
        }

        /// <summary>
        /// 特定のイベントが登録された際に実行されるActionを登録するメソッド
        /// GameEventに紐づけて登録する場合、基本的にGameEventは初期化時に登録されるため、実行されない可能性があります。
        /// その場合はinvokeIfRegisteredをtrueにすることで、登録済みならこの場で1回発火させられます。
        /// </summary>
        /// <param name="action">登録するAction</param>
        /// <param name="invokeIfRegistered">trueの場合、TEventがすでに登録済みならその場で1回発火する</param>
        /// <typeparam name="TEvent">チャンネルの購読インターフェース</typeparam>
        /// <exception cref="ArgumentNullException">actionがnullのときに出力</exception>
        public IDisposable SubscribeEventRegister<TEvent>(Action action, bool invokeIfRegistered = false)
            where TEvent : IEvent
        {
            if (action is null) throw new ArgumentNullException(nameof(action));

            if (!_availability.TryGetValue(typeof(TEvent), out var list))
            {
                list = new List<Action>();
                _availability[typeof(TEvent)] = list;
            }

            list.Add(action);

            // 待受を始める前に登録が済んでいたケースを拾うための即時発火
            if (invokeIfRegistered && IsRegistered<TEvent>())
            {
                action.Invoke();
            }

            return new BoardDispose(() => list.Remove(action));
        }

        #endregion


        /// <summary>
        /// シーン変更時にそのシーンのイベントの登録を解除するためのメソッド。
        /// イベントは値を保持しないが、チャンネルの実体はこのボードが握り続けるため、
        /// 発行元ごと破棄されたシーンのチャンネルはここで取り除く必要がある。
        /// </summary>
        /// <param name="sceneId">アンロードされたシーンID</param>
        internal void OnSceneChanged(int sceneId)
        {
            var keys = _sceneEvents
                .Where(x => x.Value.SceneId == sceneId)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in keys)
            {
                _sceneEvents.Remove(key);
            }
        }

        private bool IsRegistered<TEvent>() where TEvent : IEvent
        {
            var type = typeof(TEvent);
            return _gameEvents.ContainsKey(type)
                   || _sceneEvents.ContainsKey(type)
                   || _unRegistableEvents.ContainsKey(type);
        }

        private void OnRegisterdEvent<TEvent>()
        {
            if (_availability.TryGetValue(typeof(TEvent), out var list))
            {
                foreach (var action in list.ToArray())
                {
                    action?.Invoke();
                }
            }
        }
    }
}
