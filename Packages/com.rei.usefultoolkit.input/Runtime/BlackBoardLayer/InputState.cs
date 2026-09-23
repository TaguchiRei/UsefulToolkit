using System;
using System.Collections.Generic;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Utility;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力の状態そのもの。有効なActionMapと、入力の有効・無効を保持する。
    ///
    /// BlackBoardへは<see cref="IInputState"/>(読み取り面)としてのみ登録する。
    /// 状態を変更する操作はこの具象型にしか無く、その具象型を保持するのはStateを生成した
    /// Applicationのクラスだけになる。外部へはそのクラスが<see cref="IInputController"/>として
    /// DIコンテナ経由で公開する。
    ///
    /// 入力コールバックの登録簿はこのクラスでは持たず、<see cref="IInputEngineBridge.Subscribe{TValue}"/>
    /// への転送だけを行う。発火元(エンジン側のAction)が既に多重登録の受け口である為、
    /// ここで抱え直すと二重管理になる。Stateが登録簿を持たないので、読み取り面であるという
    /// 性質もハンドラ登録によって崩れない。
    ///
    /// map / action は公開APIの境界でenumから名前の文字列へ変換し、内部では文字列で扱う。
    /// nullの扱いは、状態を変える操作は例外、値を読むだけの問い合わせは安全な既定値、で統一している。
    /// </summary>
    [RegisterBoard(typeof(InputBoard))]
    public sealed class InputState : GameStateBase, IInputState
    {
        private readonly List<string> _activeActionMaps = new();

        private readonly ActionEntryList<StateContext<bool>> _inputEnabledChangedActions = new();
        private readonly ActionEntryList _activeActionMapsChangedActions = new();

        private IInputEngineBridge _engine;

        private IExternalInputDeviceBridge _externalInputDevice;

        public bool InputEnabled { get; private set; } = true;

        public IReadOnlyList<string> ActiveActionMaps => _activeActionMaps;

        public override string GetLog()
        {
            string maps = _activeActionMaps.Count == 0 ? "なし" : string.Join(", ", _activeActionMaps);

            return $"InputEnabled : {InputEnabled} / ActiveActionMaps : {maps} / " +
                   $"エンジン接続 : {(_engine != null ? "あり" : "なし")}";
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

        /// <summary>
        /// 外部入力を仮想デバイスへ書き込む橋渡しを繋ぐ。
        /// このメソッドは<see cref="IInputState"/>には無いため、具象型を保持する生成元だけが呼べる。
        ///
        /// 外部入力を使わない場合は繋がなくてよい。その場合
        /// <see cref="WriteExternalInput{TValue}"/>は警告を出して何もしない。
        /// </summary>
        /// <param name="externalInputDevice">繋ぐ橋渡し</param>
        /// <exception cref="ArgumentNullException">externalInputDeviceがnullのときに出力</exception>
        public void RegisterExternalInputDevice(IExternalInputDeviceBridge externalInputDevice)
        {
            _externalInputDevice = externalInputDevice
                                   ?? throw new ArgumentNullException(nameof(externalInputDevice));
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
                UsefulLogger.LogWarning($"エンジンが繋がっていない為、[{map}.{action}] の値を読み出せません。", this);
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
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            if (_engine == null)
            {
                UsefulLogger.LogWarning(
                    $"エンジンが繋がっていない為、[{map}.{action}] のコールバックを登録できません。", this);

                return BoardDispose.Empty;
            }

            return _engine.Subscribe(map, action, handler);
        }

        #endregion

        #region 操作面 : 具象型を保持しているクラスだけが呼べる

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

        /// <summary>
        /// 外部入力スロットへ値を書き込む。実際の書き込みと型の検査は橋渡しの実装が行う。
        /// </summary>
        /// <param name="slot">書き込み先のスロットを表すenum</param>
        /// <param name="value">書き込む値</param>
        /// <exception cref="ArgumentNullException">slotがnullのときに出力</exception>
        public void WriteExternalInput<TValue>(Enum slot, TValue value) where TValue : unmanaged
        {
            if (slot == null) throw new ArgumentNullException(nameof(slot));

            if (_externalInputDevice == null)
            {
                UsefulLogger.LogWarning(
                    $"外部入力デバイスが繋がっていない為、[{slot}] へ書き込めません。" +
                    "UsefulToolkit/Input/Generate External Input Device で生成してください。", this);

                return;
            }

            _externalInputDevice.TryWrite(slot, value);
        }

        #endregion

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
    }
}
