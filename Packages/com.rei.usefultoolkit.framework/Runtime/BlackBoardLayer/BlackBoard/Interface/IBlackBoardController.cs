using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// BlackBoard本体の管理面のインターフェース。ChildBoardの登録と、シーン単位の一括解除を行う。
    /// ChildBoardを登録するRootGameCompositorと、シーンのアンロードを通知するSceneStateだけが保持する。
    /// </summary>
    public interface IBlackBoardController
    {
        bool TryRegisterStateBoard<T>(T childBoard) where T : ChildStateBoardBase;

        bool TryRegisterEventBoard<T>(T childBoard) where T : ChildEventBoardBase;

        /// <summary>
        /// 登録済みの全ChildBoardへOnSceneChangedをfan-outする。シーン管理システムが
        /// 指定シーンのUnload時に呼び、そのシーンがRegisterSceneState/RegisterSceneEventで
        /// 登録したStateとイベントチャンネルだけを、ChildBoardの種類をまたいで一括Unregisterする。
        /// </summary>
        void OnSceneChanged(List<int> sceneIds);
    }
}
