using UsefulToolkit.BlackBoard.Scene;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// ChildBoardを型ごとに取得するBlackBoard本体の取得面のインターフェース。
    /// ApplicationとEngineAdapterLayerはこのインターフェース経由でのみChildBoardへ到達する。
    /// ChildBoardの登録とシーン単位の一括解除は<see cref="IBlackBoardController"/>が持つ。
    /// </summary>
    public interface IBlackBoard
    {
        SceneBoard GetSceneBoard();

        bool TryGetStateBoard<T>(out T childBoard) where T : ChildStateBoardBase;

        bool TryGetEventBoard<T>(out T childBoard) where T : ChildEventBoardBase;
    }
}
