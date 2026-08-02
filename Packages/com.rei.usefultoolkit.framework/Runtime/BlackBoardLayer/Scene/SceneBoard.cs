using UsefulToolkit.BlackBoard;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// シーン管理システムが公開するChildBoard。SceneServiceが自身の管理するSceneStateを
    /// このBoardへ登録する。Initialization層が生成し、BlackBoardへ一度だけ登録する想定
    /// (InputBoardと同じ立て付け)。
    /// </summary>
    public sealed class SceneBoard : ChildStateBoardBase
    {
    }
}
