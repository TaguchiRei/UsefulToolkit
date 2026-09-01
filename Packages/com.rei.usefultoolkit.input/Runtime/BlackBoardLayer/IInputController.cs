using System;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力の操作面。ActionMapの切り替え、入力の有効・無効、入力ソースの接続を行う。
    ///
    /// この型は BlackBoard には載せない。InputState を変更できるのはそれを生成した
    /// Application のクラス(<see cref="UsefulToolkit.Application.Input.IInputManager"/> の実装)だけであり、
    /// その操作面は Compositor の DI コンテナ経由で <c>IInjectable&lt;IInputController&gt;</c> として配る。
    /// BlackBoard から取得できるのは読み取り面の <see cref="IInputState"/> のみ。
    ///
    /// 型定義がこの層にあるのは、Application と EngineService の双方から参照できる位置が
    /// BlackBoardLayer だけであるため(<see cref="IInputEngineBridge"/> と同じ理由)。
    ///
    /// map / action の指定にはInputActionEnumGeneratorが生成したActionMaps・XxxActionsのenumを渡す。
    /// 内部では名前の文字列として扱うため、このパッケージはどのenum型にも依存しない。
    /// </summary>
    public interface IInputController
    {
        /// <summary>
        /// 指定したActionMapだけを有効にする。他の有効なActionMapは全て無効になる。
        /// </summary>
        /// <param name="map">有効にするActionMapを表すenum</param>
        /// <exception cref="ArgumentNullException">mapがnullのときに出力</exception>
        void SwitchActionMap(Enum map);

        /// <summary>
        /// 指定したActionMapを、現在有効なものへ追加で有効にする。
        /// </summary>
        /// <param name="map">有効にするActionMapを表すenum</param>
        /// <exception cref="ArgumentNullException">mapがnullのときに出力</exception>
        void EnableActionMap(Enum map);

        /// <summary>
        /// 指定したActionMapを無効にする。
        /// </summary>
        /// <param name="map">無効にするActionMapを表すenum</param>
        /// <exception cref="ArgumentNullException">mapがnullのときに出力</exception>
        void DisableActionMap(Enum map);

        /// <summary> 入力全体を有効にする。 </summary>
        void EnableInput();

        /// <summary> 入力全体を無効にする。有効なActionMapの内容は保持される。 </summary>
        void DisableInput();

        /// <summary>
        /// 指定したActionを、エンジン側の入力ソースとしてチャンネルへ繋ぐ。
        /// 生成されたActionMaps・XxxActionsのenumに依存するため、利用側のInitializerから呼ぶ。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <exception cref="ArgumentNullException">map・actionがnullのときに出力</exception>
        void Bind<TValue>(Enum map, Enum action) where TValue : unmanaged;

        /// <summary>
        /// 指定したActionへ、エンジン以外の入力ソースを繋ぐ。
        /// 入力ソースはチャンネルへの参照を持たず、値の流し込みはこのメソッドが張るブリッジだけが行う。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <param name="source">登録する入力ソース</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        /// <exception cref="ArgumentNullException">map・action・sourceがnullのときに出力</exception>
        IDisposable RegisterExternalInputSource<TValue>(Enum map, Enum action,
            IExternalInputSource<TValue> source) where TValue : unmanaged;
    }
}
