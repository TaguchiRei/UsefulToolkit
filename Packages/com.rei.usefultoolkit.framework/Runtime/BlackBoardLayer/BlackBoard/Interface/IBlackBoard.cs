using UsefulToolkit.BlackBoard.Scene;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// ChildBoardを型ごとに登録・取得する最上位のBlackBoard本体のインターフェース。
    /// ApplicationとEngineServiceLayerはこのインターフェース経由でのみChildBoardへ到達する。
    /// </summary>
    public interface IBlackBoard
    {
        /// <summary>
        /// シーン管理システムの外部公開面。公開するのは現在/遷移先のシーングループと
        /// グループ読み込み時のAction登録口のみで、遷移の起動(TransitionTo)は含まない。
        /// 遷移を行うクラスはSceneFlowControllerBase(の派生)を直接保持して呼び出す想定。
        /// SceneStateはSceneFlowControllerBaseの構築時に登録されるため、それより前は false。
        /// </summary>
        bool TryGetSceneState(out ISceneStateGetter sceneState);

        bool TryGetStateBoard<T>(out T childBoard) where T : ChildStateBoardBase;
        bool TryRegisterStateBoard<T>(T childBoard) where T : ChildStateBoardBase;

        bool TryGetEventBoard<T>(out T childBoard) where T : ChildEventBoardBase;
        bool TryRegisterEventBoard<T>(T childBoard) where T : ChildEventBoardBase;

        /// <summary>
        /// 登録済みの全ChildBoardへOnSceneChangedをfan-outする。シーン管理システムが
        /// 指定シーンのUnload時に呼び、そのシーンがRegisterSceneState/RegisterSceneEventで
        /// 登録したStateとイベントチャンネルだけを、ChildBoardの種類をまたいで一括Unregisterする。
        /// </summary>
        void OnSceneChanged(string sceneName);
    }
}