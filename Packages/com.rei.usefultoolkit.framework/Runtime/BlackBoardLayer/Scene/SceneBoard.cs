using UsefulToolkit.BlackBoard;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// シーン管理システムが公開するChildStateBoard。SceneFlowControllerが生成した
    /// SceneStateをISceneStateGetter経由で登録する(現在/次シーンノードの参照用)。
    /// 実際のシーン読み込みのトリガーはSceneChangeBoard(ChildEventBoard)側の責務。
    /// </summary>
    public sealed class SceneBoard : ChildStateBoardBase
    {
    }
}
