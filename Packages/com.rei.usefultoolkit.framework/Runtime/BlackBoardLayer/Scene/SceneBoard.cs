using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary>
    /// シーン管理に関するStateを登録するための子ボード。
    /// BlackBoardのコンストラクタへ渡す特別扱いのボードで、常に存在する。
    /// </summary>
    public sealed class SceneBoard : ChildStateBoardBase
    {
    }
}
