using System;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力の操作面。ActionMapの切り替え、入力の有効・無効、外部入力の書き込みを行う。
    ///
    /// この型は BlackBoard には載せない。InputState を変更できるのはそれを生成した
    /// Application のクラス(<see cref="UsefulToolkit.Application.Input.IInputManager"/> の実装)だけであり、
    /// その操作面は Compositor の DI コンテナ経由で <c>IInjectable&lt;IInputController&gt;</c> として配る。
    /// BlackBoard から取得できるのは読み取り面の <see cref="IInputState"/> のみ。
    ///
    /// 型定義がこの層にあるのは、Application と EngineAdapter の双方から参照できる位置が
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
        /// 外部入力スロットへ値を書き込む。書き込まれた値は仮想デバイスを経由して
        /// 通常の InputAction として発火するため、受け取る側は入力源を区別しない。
        ///
        /// タッチの意味づけやAIの入力など、InputSystem の外で作った値を入力として流す経路。
        /// slot には <c>UsefulToolkit/Input/Generate External Input Device</c> が生成した
        /// ExternalInputs のenumを渡す。
        /// </summary>
        /// <param name="slot">書き込み先のスロットを表すenum</param>
        /// <param name="value">書き込む値。スロットの宣言と型が一致していること</param>
        void WriteExternalInput<TValue>(Enum slot, TValue value) where TValue : unmanaged;
    }
}
