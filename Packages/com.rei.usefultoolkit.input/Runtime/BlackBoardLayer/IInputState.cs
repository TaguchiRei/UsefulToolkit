using System;
using System.Collections.Generic;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力の読み取り面。有効なActionMap、入力の有効・無効、Actionの現在値を公開する。
    /// コールバックの登録やActionMapの切り替えは<see cref="Dispatcher"/>から行う。
    /// </summary>
    public interface IInputState : IStateGetter
    {
        /// <summary> 入力の操作面 </summary>
        IInputDispatcher Dispatcher { get; }

        /// <summary> 入力全体を受け付けるか </summary>
        bool InputEnabled { get; }

        /// <summary> 現在有効なActionMap名 </summary>
        IReadOnlyList<string> ActiveActionMaps { get; }

        /// <summary>
        /// 指定したActionMapが有効か。
        /// </summary>
        /// <param name="map">確認するActionMapを表すenum</param>
        bool IsActionMapActive(Enum map);

        /// <summary>
        /// 指定したActionの現在値を読み出す。
        /// 入力ソースが繋がっていない場合はPhaseがDisabledのInputContextを返す。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        InputContext<TValue> ReadValue<TValue>(Enum map, Enum action) where TValue : unmanaged;

        /// <summary>
        /// 入力の有効・無効が変わった際に実行するアクションを登録する。
        /// </summary>
        /// <param name="changedAction">変化時に実行するアクション。引数に変更前後の値が入る</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        IDisposable RegisterEventOnInputEnabledChanged(ActionEntry<StateContext<bool>> changedAction);

        /// <summary>
        /// 有効なActionMapが変わった際に実行するアクションを登録する。
        /// </summary>
        /// <param name="changedAction">変化時に実行するアクション</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        IDisposable RegisterEventOnActiveActionMapsChanged(ActionEntry changedAction);
    }
}
