using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力に関するStateを登録するChildBoard。
    /// 入力の読み取り面と操作面は全て<see cref="InputState"/>が持つため、このボード自体は空になる。
    /// </summary>
    public sealed class InputBoard : ChildStateBoardBase
    {
    }
}
