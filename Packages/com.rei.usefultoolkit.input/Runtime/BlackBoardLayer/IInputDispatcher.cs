using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力の操作面。コールバックの登録、ActionMapの切り替え、入力の有効・無効の切り替えを行う。
    /// <see cref="IInputState"/>から取得する。
    ///
    /// map / action の指定にはInputActionEnumGeneratorが生成したActionMaps・XxxActionsのenumを渡す。
    /// 内部では名前の文字列として扱うため、このパッケージはどのenum型にも依存しない。
    /// </summary>
    public interface IInputDispatcher
    {
        /// <summary>
        /// 指定したActionの入力コールバックを登録する。
        /// started / performed / canceled は1本のコールバックへまとめて届くため、
        /// 区別が必要な場合は<see cref="InputContext{TValue}.Phase"/>で判定する。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <param name="handler">入力時に実行するハンドラ</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        IDisposable RegisterInput<TValue>(Enum map, Enum action, Action<InputContext<TValue>> handler)
            where TValue : unmanaged;

        /// <summary>
        /// 指定したActionへ入力ソースが登録されるのを待ってから、入力コールバックを登録する。
        /// 入力ソースが既に登録済みならその場で登録する。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <param name="handler">入力時に実行するハンドラ</param>
        /// <param name="timeoutSeconds">待機の打ち切り秒数。nullならUsefulToolkitConst.DefaultTimeoutSeconds</param>
        /// <param name="cancellationToken">待機の中断に使う</param>
        /// <returns>Disposeすると登録を解除できる。タイムアウトした場合は何も解除しないハンドル</returns>
        UniTask<IDisposable> RegisterInputAsync<TValue>(Enum map, Enum action,
            Action<InputContext<TValue>> handler, float? timeoutSeconds = null,
            CancellationToken cancellationToken = default) where TValue : unmanaged;

        /// <summary>
        /// 指定したActionMapだけを有効にする。他の有効なActionMapは全て無効になる。
        /// </summary>
        /// <param name="map">有効にするActionMapを表すenum</param>
        void SwitchActionMap(Enum map);

        /// <summary>
        /// 指定したActionMapを、現在有効なものへ追加で有効にする。
        /// </summary>
        /// <param name="map">有効にするActionMapを表すenum</param>
        void EnableActionMap(Enum map);

        /// <summary>
        /// 指定したActionMapを無効にする。
        /// </summary>
        /// <param name="map">無効にするActionMapを表すenum</param>
        void DisableActionMap(Enum map);

        /// <summary> 入力全体を有効にする。 </summary>
        void EnableInput();

        /// <summary> 入力全体を無効にする。有効なActionMapの内容は保持される。 </summary>
        void DisableInput();

        /// <summary>
        /// 指定したActionへ入力ソースを繋ぐ。EngineServiceLayerのクラスから呼ぶ。
        /// 入力ソースはチャンネルへの参照を持たず、値の流し込みはこのメソッドが張るブリッジだけが行う。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <param name="source">登録する入力ソース</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        IDisposable RegisterExternalInputSource<TValue>(Enum map, Enum action,
            IExternalInputSource<TValue> source) where TValue : unmanaged;
    }
}
