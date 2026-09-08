using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Utility;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力の状態そのもの。有効なActionMap、入力の有効・無効、Actionごとのコールバックを保持する。
    ///
    /// BlackBoardへは<see cref="IInputState"/>(読み取り面)としてのみ登録する。
    /// 状態を変更する操作はこの具象型にしか無く、その具象型を保持するのはStateを生成した
    /// Applicationのクラスだけになる。外部へはそのクラスが<see cref="IInputController"/>として
    /// DIコンテナ経由で公開する。
    ///
    /// map / action は公開APIの境界でenumから名前の文字列へ変換し、内部では文字列で扱う。
    /// nullの扱いは、状態を変える操作は例外、値を読むだけの問い合わせは安全な既定値、で統一している。
    /// </summary>
    [RegisterBoard(typeof(InputBoard))]
    public sealed class InputState : GameStateBase, IInputState
    {
        /// <summary>
        /// (ActionMap名, Action名)ごとのコールバック。値はActionChannel&lt;InputContext&lt;TValue&gt;&gt;
        ///
        /// ハンドラが空になっても取り除かない。Bindした入力ソースはこのチャンネルを掴んでおり、
        /// 作り直すと発火先が失われる為。件数は(map, action)の組み合わせの数で頭打ちになる。
        /// </summary>
        private readonly Dictionary<(string Map, string Action), object> _channels = new();

        /// <summary>
        /// 入力ソースが繋がっている(ActionMap名, Action名)と、その本数。
        /// 同じActionへエンジンとタッチなど複数の入力ソースを繋げるため、
        /// 1本Disposeしただけで「未接続」に戻らないよう本数で持つ。
        /// </summary>
        private readonly Dictionary<(string Map, string Action), int> _boundSourceCounts = new();

        /// <summary> エンジン側の入力ソースを繋いだ(ActionMap名, Action名)と、その解除ハンドル </summary>
        private readonly Dictionary<(string Map, string Action), IDisposable> _engineBindings = new();

        /// <summary> 入力ソースが繋がった際に実行するアクション </summary>
        private readonly KeyedActionEntryList<(string Map, string Action)> _sourceBoundActions = new();

        private readonly List<string> _activeActionMaps = new();

        private readonly ActionEntryList<StateContext<bool>> _inputEnabledChangedActions = new();
        private readonly ActionEntryList _activeActionMapsChangedActions = new();

        private IInputEngineBridge _engine;

        public bool InputEnabled { get; private set; } = true;

        public IReadOnlyList<string> ActiveActionMaps => _activeActionMaps;

        public override string GetLog()
        {
            string maps = _activeActionMaps.Count == 0 ? "なし" : string.Join(", ", _activeActionMaps);

            return $"InputEnabled : {InputEnabled} / ActiveActionMaps : {maps} / " +
                   $"チャンネル数 : {_channels.Count} / 入力ソース数 : {_boundSourceCounts.Count}";
        }

        /// <summary>
        /// 入力をエンジンへ反映する橋渡しを繋ぎ、現在の内容をその場で反映する。
        /// このメソッドは<see cref="IInputState"/>には無いため、具象型を保持する生成元だけが呼べる。
        /// </summary>
        /// <param name="engine">繋ぐ橋渡し</param>
        /// <exception cref="ArgumentNullException">engineがnullのときに出力</exception>
        public void RegisterInputEngine(IInputEngineBridge engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));

            // エンジンをStateの写しに保つ責務はState側にあるため、接続の時点で押し込む。
            // 接続前のエンジンのActionMapの有効状態は不定なので、全リセットしてから反映する
            _engine.ApplyExclusive(InputEnabled, _activeActionMaps);
        }

        #region IInputState実装 : 状態の確認

        public bool IsActionMapActive(Enum map)
        {
            return map != null && _activeActionMaps.Contains(EnumNameCache.GetName(map));
        }

        public bool IsActionMapActive<TMap>(TMap map) where TMap : struct, Enum
        {
            return _activeActionMaps.Contains(EnumNameCache<TMap>.GetName(map));
        }

        public InputContext<TValue> ReadValue<TValue>(Enum map, Enum action) where TValue : unmanaged
        {
            if (map == null || action == null) return new InputContext<TValue>(InputPhase.Disabled, default);

            if (_engine == null)
            {
                UsefulLogger.LogWarning($"入力ソースが繋がっていない為、[{map}.{action}] の値を読み出せません。", this);
                return new InputContext<TValue>(InputPhase.Disabled, default);
            }

            return _engine.ReadValue<TValue>(map, action);
        }

        public IDisposable RegisterEventOnInputEnabledChanged(ActionEntry<StateContext<bool>> changedAction)
        {
            return _inputEnabledChangedActions.Register(changedAction, nameof(changedAction));
        }

        public IDisposable RegisterEventOnActiveActionMapsChanged(ActionEntry changedAction)
        {
            return _activeActionMapsChangedActions.Register(changedAction, nameof(changedAction));
        }

        #endregion

        #region IInputState実装 : コールバック登録

        public IDisposable RegisterInput<TValue>(Enum map, Enum action, Action<InputContext<TValue>> handler)
            where TValue : unmanaged
        {
            var key = ToKey(map, action);

            if (handler is null) throw new ArgumentNullException(nameof(handler));

            if (!TryGetOrCreateChannel<TValue>(key, out var channel)) return BoardDispose.Empty;

            // エンジン側にActionがあれば、この時点で入力ソースを繋ぐ。既に繋がっている場合は何もしない
            TryBindEngineSource<TValue>(map, action, key, false);

            return channel.Register(handler);
        }

        public async UniTask<IDisposable> RegisterInputAsync<TValue>(Enum map, Enum action,
            Action<InputContext<TValue>> handler, float? timeoutSeconds = null,
            CancellationToken cancellationToken = default) where TValue : unmanaged
        {
            var key = ToKey(map, action);

            if (handler is null) throw new ArgumentNullException(nameof(handler));

            // 先に遅延Bindを試す。エンジン側にActionがあればここで繋がり、待機せずに済む
            TryBindEngineSource<TValue>(map, action, key, false);

            if (!_boundSourceCounts.ContainsKey(key))
            {
                var completion = new UniTaskCompletionSource();

                // 一度繋がれば用済みなのでDisposeOnUsedで登録する
                using var waiting = _sourceBoundActions.Register(
                    key, new ActionEntry(true, () => completion.TrySetResult()), nameof(handler));

                float seconds = timeoutSeconds ?? UsefulToolkitConst.DefaultTimeoutSeconds;

                int winIndex = await UniTask.WhenAny(
                    completion.Task,
                    UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: cancellationToken));

                if (winIndex != 0)
                {
                    UsefulLogger.LogWarning(
                        $"[{map}.{action}] へ入力ソースが繋がらないまま {seconds} 秒が経過した為、登録を打ち切りました。", this);
                    return BoardDispose.Empty;
                }
            }

            return RegisterInput(map, action, handler);
        }

        #endregion

        #region 操作面 : 具象型を保持しているクラスだけが呼べる

        /// <summary>
        /// 指定したActionを、エンジン側の入力ソースとしてチャンネルへ繋ぐ。
        /// 同じActionへ2回目を呼んだ場合は警告を出して何もしない。
        ///
        /// <see cref="RegisterInput{TValue}"/>が同じ橋渡しを自動で行う為、通常は呼ぶ必要が無い。
        /// エンジン側の入力ソースだけを先に繋いでおきたい場合に使う。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <exception cref="ArgumentNullException">map・actionがnullのときに出力</exception>
        public void Bind<TValue>(Enum map, Enum action) where TValue : unmanaged
        {
            TryBindEngineSource<TValue>(map, action, ToKey(map, action), true);
        }

        /// <summary>
        /// 指定したActionへ入力ソースを繋ぐ。
        /// 入力ソースはチャンネルへの参照を持たず、値の流し込みはここが張るブリッジだけが行う。
        /// 同じActionへ複数の入力ソースを繋いでよく、その場合はどれが発火しても同じチャンネルへ流れる。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <param name="source">登録する入力ソース</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        /// <exception cref="ArgumentNullException">map・action・sourceがnullのときに出力</exception>
        public IDisposable RegisterExternalInputSource<TValue>(Enum map, Enum action,
            IExternalInputSource<TValue> source) where TValue : unmanaged
        {
            var key = ToKey(map, action);

            if (source is null) throw new ArgumentNullException(nameof(source));

            if (!TryGetOrCreateChannel<TValue>(key, out var channel)) return BoardDispose.Empty;

            void Handler(InputContext<TValue> context) => channel.Invoke(context);

            source.RegisterAction(Handler);
            IncrementBoundSource(key);

            // 待機中のRegisterInputAsyncを再開させる
            _sourceBoundActions.Invoke(key);

            return new BoardDispose(() =>
            {
                source.UnRegisterAction(Handler);
                DecrementBoundSource(key);
            });
        }

        /// <summary>
        /// 指定したActionMapだけを有効にする。他の有効なActionMapは全て無効になる。
        /// </summary>
        /// <param name="map">有効にするActionMapを表すenum</param>
        /// <exception cref="ArgumentNullException">mapがnullのときに出力</exception>
        public void SwitchActionMap(Enum map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            string name = EnumNameCache.GetName(map);

            if (_activeActionMaps.Count == 1 && _activeActionMaps[0] == name) return;

            _activeActionMaps.Clear();
            _activeActionMaps.Add(name);
            OnActiveActionMapSwitched();
        }

        /// <summary>
        /// 指定したActionMapを、現在有効なものへ追加で有効にする。
        /// </summary>
        /// <param name="map">有効にするActionMapを表すenum</param>
        /// <exception cref="ArgumentNullException">mapがnullのときに出力</exception>
        public void EnableActionMap(Enum map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            string name = EnumNameCache.GetName(map);

            if (_activeActionMaps.Contains(name)) return;

            _activeActionMaps.Add(name);
            OnActiveActionMapsChanged();
        }

        /// <summary>
        /// 指定したActionMapを無効にする。
        /// </summary>
        /// <param name="map">無効にするActionMapを表すenum</param>
        /// <exception cref="ArgumentNullException">mapがnullのときに出力</exception>
        public void DisableActionMap(Enum map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            if (!_activeActionMaps.Remove(EnumNameCache.GetName(map))) return;

            OnActiveActionMapsChanged();
        }

        /// <summary> 入力全体を有効にする。 </summary>
        public void EnableInput() => SetInputEnabled(true);

        /// <summary> 入力全体を無効にする。有効なActionMapの内容は保持される。 </summary>
        public void DisableInput() => SetInputEnabled(false);

        #endregion

        /// <summary>
        /// 指定したActionへエンジン側の入力ソースを繋ぐ。
        /// エンジンが未接続、既に橋渡し済み、エンジン側に対応するActionが無い場合は何もしない。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <param name="key">(ActionMap名, Action名)</param>
        /// <param name="warnOnSkip">エンジン未接続・橋渡し済みで見送った場合に警告を出すか</param>
        private void TryBindEngineSource<TValue>(Enum map, Enum action, (string Map, string Action) key,
            bool warnOnSkip) where TValue : unmanaged
        {
            if (_engine == null)
            {
                if (warnOnSkip)
                {
                    UsefulLogger.LogWarning($"入力ソースが繋がっていない為、[{map}.{action}] を橋渡しできません。", this);
                }

                return;
            }

            // 2本張るとstarted/performed/canceledが二重に流れ、全ハンドラが2回発火する
            if (_engineBindings.ContainsKey(key))
            {
                if (warnOnSkip)
                {
                    UsefulLogger.LogWarning($"[{map}.{action}] は既に橋渡し済みの為、Bindを無視しました。", this);
                }

                return;
            }

            if (!_engine.TryCreateInputSource<TValue>(map, action, out var source)) return;

            // 型が食い違う等でチャンネルを確保できない場合は、RegisterExternalInputSourceが
            // 何も解除しないハンドルを返す。それを_engineBindingsへ入れると以降の正しい型での
            // Bindが「既に橋渡し済み」で弾かれ続けるため、先に確保できるか確かめる
            if (!TryGetOrCreateChannel<TValue>(key, out _)) return;

            _engineBindings[key] = RegisterExternalInputSource(map, action, source);
        }

        private void SetInputEnabled(bool enabled)
        {
            if (InputEnabled == enabled) return;

            bool oldValue = InputEnabled;
            InputEnabled = enabled;

            // 購読側が読み取る前にエンジンへ反映しておく
            _engine?.Apply(InputEnabled, _activeActionMaps);
            _inputEnabledChangedActions.Invoke(new StateContext<bool>(oldValue, enabled));
        }

        private void OnActiveActionMapsChanged()
        {
            _engine?.Apply(InputEnabled, _activeActionMaps);
            _activeActionMapsChangedActions.Invoke();
        }

        /// <summary>
        /// ActionMapを作り直す形の変更をエンジンへ反映し、変更通知を実行する。
        /// 進行中の入力を打ち切りたいSwitchActionMap専用。追加・削除は<see cref="OnActiveActionMapsChanged"/>。
        /// </summary>
        private void OnActiveActionMapSwitched()
        {
            _engine?.ApplyExclusive(InputEnabled, _activeActionMaps);
            _activeActionMapsChangedActions.Invoke();
        }

        private void IncrementBoundSource((string Map, string Action) key)
        {
            _boundSourceCounts[key] = _boundSourceCounts.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        private void DecrementBoundSource((string Map, string Action) key)
        {
            if (!_boundSourceCounts.TryGetValue(key, out int count)) return;

            if (count <= 1)
            {
                _boundSourceCounts.Remove(key);
                return;
            }

            _boundSourceCounts[key] = count - 1;
        }

        /// <summary>
        /// enumの組をチャンネルのキーへ変換する。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <exception cref="ArgumentNullException">map・actionがnullのときに出力</exception>
        private static (string Map, string Action) ToKey(Enum map, Enum action)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (action == null) throw new ArgumentNullException(nameof(action));

            return (EnumNameCache.GetName(map), EnumNameCache.GetName(action));
        }

        /// <summary>
        /// 指定したキーのチャンネルを取得する。無ければ作る。
        /// 既存のチャンネルと値型が食い違う場合は、エラーログを出してfalseを返す。
        /// </summary>
        /// <param name="key">(ActionMap名, Action名)</param>
        /// <param name="channel">取得したチャンネル。失敗時はnull</param>
        private bool TryGetOrCreateChannel<TValue>(
            (string Map, string Action) key, out ActionChannel<InputContext<TValue>> channel)
            where TValue : unmanaged
        {
            if (_channels.TryGetValue(key, out var raw))
            {
                channel = raw as ActionChannel<InputContext<TValue>>;

                if (channel != null) return true;

                // 差し替えると、既存のチャンネルを掴んでいる入力ソースの発火先が失われて
                // 新しく登録したハンドラが一切呼ばれなくなるため、作り直さずに拒否する
                UsefulLogger.LogError(
                    $"[{key.Map}.{key.Action}] は既に {ValueTypeNameOf(raw)} 型として登録されている為、" +
                    $"{typeof(TValue).Name} 型では扱えません。BindとRegisterInputで同じ型を指定してください。", this);

                return false;
            }

            channel = new ActionChannel<InputContext<TValue>>();
            _channels[key] = channel;
            return true;
        }

        /// <summary>
        /// ActionChannel&lt;InputContext&lt;TValue&gt;&gt; から、TValueの型名を取り出す。
        /// </summary>
        /// <param name="channel">型名を調べるチャンネル</param>
        private static string ValueTypeNameOf(object channel)
        {
            var channelType = channel.GetType();

            if (!channelType.IsGenericType) return channelType.Name;

            var contextType = channelType.GetGenericArguments()[0];

            return contextType.IsGenericType ? contextType.GetGenericArguments()[0].Name : contextType.Name;
        }
    }
}
