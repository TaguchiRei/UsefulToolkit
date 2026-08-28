using System.Collections.Generic;
using UsefulToolkit.BlackBoard.Scene;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// ChildBoardを型ごとに登録・取得する最上位のBlackBoard本体のインターフェース。
    /// ApplicationとEngineServiceLayerはこのインターフェース経由でのみChildBoardへ到達する。
    /// </summary>
    public interface IBlackBoard
    {
        bool TryGetStateBoard<T>(out T childBoard) where T : ChildStateBoardBase;
        bool TryRegisterStateBoard<T>(T childBoard) where T : ChildStateBoardBase;

        bool TryGetEventBoard<T>(out T childBoard) where T : ChildEventBoardBase;
        bool TryRegisterEventBoard<T>(T childBoard) where T : ChildEventBoardBase;

        /// <summary>
        /// 登録済みの全ChildBoardへOnSceneChangedをfan-outする。シーン管理システムが
        /// 指定シーンのUnload時に呼び、そのシーンがRegisterSceneState/RegisterSceneEventで
        /// 登録したStateとイベントチャンネルだけを、ChildBoardの種類をまたいで一括Unregisterする。
        /// </summary>
        void OnSceneChanged(List<int> sceneIds);
    }
}