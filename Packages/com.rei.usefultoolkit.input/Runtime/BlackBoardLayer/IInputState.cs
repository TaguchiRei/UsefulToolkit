using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力の読み取り面。BlackBoardへ載せるのはこの型だけになる。
    ///
    /// 公開するのは現在の状態の確認と、入力に対するコールバックの登録の2つに限る。
    /// ActionMapの切り替えや入力の有効・無効といった状態を変更する操作は
    /// <see cref="IInputController"/> にあり、そちらはDIコンテナ経由でのみ配られる。
    /// </summary>
    public interface IInputState : IStateGetter
    {
        /// <summary> 入力全体を受け付けるか </summary>
        bool InputEnabled { get; }

        /// <summary> 現在有効なActionMap名 </summary>
        IReadOnlyList<string> ActiveActionMaps { get; }

        /// <summary>
        /// 指定したActionMapが有効か。mapがnullの場合はfalseを返す。
        /// </summary>
        /// <param name="map">確認するActionMapを表すenum</param>
        bool IsActionMapActive(Enum map);

        /// <summary>
        /// 指定したActionの現在値を読み出す。
        /// 入力ソースが繋がっていない場合はPhaseがDisabledのInputContextを返す。
        /// 毎フレーム呼ばれうる経路のため、map・actionがnullでも例外は投げずDisabledを返す。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        InputContext<TValue> ReadValue<TValue>(Enum map, Enum action) where TValue : unmanaged;

        /// <summary>
        /// 指定したActionの入力コールバックを登録する。
        /// started / performed / canceled は1本のコールバックへまとめて届くため、
        /// 区別が必要な場合は<see cref="InputContext{TValue}.Phase"/>で判定する。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <param name="handler">入力時に実行するハンドラ</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        /// <exception cref="ArgumentNullException">map・action・handlerがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">同じハンドラが既に登録されているときに出力</exception>
        IDisposable RegisterInput<TValue>(Enum map, Enum action, Action<InputContext<TValue>> handler)
            where TValue : unmanaged;

        /// <summary>
        /// 指定したActionへ入力ソースが登録されるのを待ってから、入力コールバックを登録する。
        /// 入力ソースが既に登録済みならその場で登録する。
        /// 返したIDisposableの解放が登録側の責任になる点は<see cref="RegisterInput{TValue}"/>と同じ。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <param name="handler">入力時に実行するハンドラ</param>
        /// <param name="timeoutSeconds">待機の打ち切り秒数。nullならUsefulToolkitConst.DefaultTimeoutSeconds</param>
        /// <param name="cancellationToken">待機の中断に使う</param>
        /// <returns>Disposeすると登録を解除できる。タイムアウトした場合は何も解除しないハンドル</returns>
        /// <exception cref="ArgumentNullException">map・action・handlerがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">同じハンドラが既に登録されているときに出力</exception>
        UniTask<IDisposable> RegisterInputAsync<TValue>(Enum map, Enum action,
            Action<InputContext<TValue>> handler, float? timeoutSeconds = null,
            CancellationToken cancellationToken = default) where TValue : unmanaged;

        /// <summary>
        /// 入力の有効・無効が変わった際に実行するアクションを登録する。
        /// </summary>
        /// <param name="changedAction">変化時に実行するアクション。引数に変更前後の値が入る</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        /// <exception cref="ArgumentNullException">changedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションが既に登録されているときに出力</exception>
        IDisposable RegisterEventOnInputEnabledChanged(ActionEntry<StateContext<bool>> changedAction);

        /// <summary>
        /// 有効なActionMapが変わった際に実行するアクションを登録する。
        /// </summary>
        /// <param name="changedAction">変化時に実行するアクション</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        /// <exception cref="ArgumentNullException">changedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションが既に登録されているときに出力</exception>
        IDisposable RegisterEventOnActiveActionMapsChanged(ActionEntry changedAction);
    }
}
