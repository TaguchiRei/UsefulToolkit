using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力に関するStateを登録するChildBoard。
    /// 入力の読み取り面は全て<see cref="InputState"/>が持つため、このボード自体は空になる。
    /// 操作面(<see cref="IInputController"/>)はここには載らず、DIコンテナ経由で配られる。
    /// </summary>
    public sealed class InputBoard : ChildStateBoardBase
    {
    }
}
