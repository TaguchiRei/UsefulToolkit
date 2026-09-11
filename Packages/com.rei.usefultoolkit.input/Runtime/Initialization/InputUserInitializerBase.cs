using System;
using System.Collections.Generic;
using UsefulToolkit.Attributes;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Utility;

namespace UsefulToolkit.Initialization
{
    /// <summary>
    /// 入力コールバックを登録する利用者向け Initializer の基底。
    ///
    /// BlackBoard からの <see cref="IInputState"/> の取得、登録したコールバックの破棄、
    /// <see cref="InputInitializerBase"/> より後に走らせる為の実行順宣言をこのクラスが受け持つ。
    /// 派生クラスは <see cref="OnInputReady"/> の中で <see cref="RegisterInput{TValue}"/> を呼ぶだけでよい。
    ///
    /// ActionMap の切り替え(<see cref="IInputController.SwitchActionMap"/>)はゲームロジックの判断なので
    /// このクラスは面倒を見ない。必要な場合は従来通り <c>IInjectable&lt;IInputController&gt;</c> を実装する。
    ///
    /// Initializer ではない通常の MonoBehaviour から登録する場合はこのクラスを使えない。
    /// その場合は <see cref="IInputState"/> を直接扱い、戻り値の IDisposable を
    /// UniTask の <c>AddTo(this.GetCancellationTokenOnDestroy())</c> などで破棄すること。
    /// </summary>
    [InitializeOrder(InitializeOrderConst.Initializer)]
    public abstract class InputUserInitializerBase : InitializerBase
    {
        private readonly List<IDisposable> _registrations = new();

        /// <summary>
        /// 入力の読み取り面。<see cref="OnInputReady"/> 以降で参照できる。
        /// </summary>
        protected IInputState InputState { get; private set; }

        /// <summary>
        /// IInputState を取得してから <see cref="OnInputReady"/> を呼ぶ。
        /// 取得に失敗した場合は Initialized を立てずに抜ける。
        /// </summary>
        /// <param name="blackBoard">IInputState の取得元</param>
        public override void Initialize(IBlackBoard blackBoard)
        {
            if (!TryResolveInputState(blackBoard)) return;

            base.Initialize(blackBoard);

            OnInputReady();
        }

        /// <summary>
        /// IInputState が揃った後に呼ばれる。入力コールバックの登録をここに書く。
        /// </summary>
        protected abstract void OnInputReady();

        /// <summary>
        /// 指定した Action の入力コールバックを登録する。
        /// 戻り値の IDisposable はこのクラスが保持し、OnDestroy でまとめて解放する。
        /// </summary>
        /// <param name="map">ActionMap を表す enum</param>
        /// <param name="action">Action を表す enum</param>
        /// <param name="handler">入力時に実行するハンドラ</param>
        protected void RegisterInput<TValue>(Enum map, Enum action, Action<InputContext<TValue>> handler)
            where TValue : unmanaged
        {
            if (InputState == null)
            {
                UsefulLogger.LogError("IInputState が未取得の為、入力コールバックを登録できません。", this);
                return;
            }

            _registrations.Add(InputState.RegisterInput(map, action, handler));
        }

        /// <summary>
        /// 指定した Action の現在値を読み出す。
        /// IInputState が未取得の場合は Phase が Disabled の InputContext を返す。
        /// </summary>
        /// <param name="map">ActionMap を表す enum</param>
        /// <param name="action">Action を表す enum</param>
        protected InputContext<TValue> ReadValue<TValue>(Enum map, Enum action) where TValue : unmanaged
        {
            if (InputState == null) return new InputContext<TValue>(InputPhase.Disabled, default);

            return InputState.ReadValue<TValue>(map, action);
        }

        /// <summary>
        /// 登録した入力コールバックをまとめて解除する。
        /// 派生クラスで override する場合は base の呼び出しを残すこと。
        /// </summary>
        protected virtual void OnDestroy()
        {
            foreach (var registration in _registrations)
            {
                registration.Dispose();
            }

            _registrations.Clear();
        }

        /// <summary>
        /// BlackBoard から InputBoard を辿って IInputState を取得する。
        /// </summary>
        /// <param name="blackBoard">取得元</param>
        /// <returns>取得できた場合はtrue</returns>
        private bool TryResolveInputState(IBlackBoard blackBoard)
        {
            if (blackBoard == null)
            {
                UsefulLogger.LogError("BlackBoard が渡されていない為、IInputState を取得できません。", this);
                return false;
            }

            if (!blackBoard.TryGetStateBoard<InputBoard>(out var inputBoard))
            {
                UsefulLogger.LogError("InputBoard が BlackBoard へ登録されていない為、IInputState を取得できません。", this);
                return false;
            }

            if (!inputBoard.TryGetGameState<IInputState>(out var inputState))
            {
                UsefulLogger.LogError("IInputState が InputBoard へ登録されていません。" +
                                      "InputInitializer より後に初期化されているか確認してください。", this);
                return false;
            }

            InputState = inputState;
            return true;
        }
    }
}
