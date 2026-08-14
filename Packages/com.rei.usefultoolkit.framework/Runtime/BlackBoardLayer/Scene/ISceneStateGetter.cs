using System.Collections.Generic;
using UsefulToolkit.Application.StateManagement;

namespace UsefulToolkit.Framework.BlackBoard
{
    /// <summary>
    /// 現在いるシーンノードと、そこから遷移できるノードを公開するGetterインターフェース。
    /// SceneNodeそのものではなくNodeIdだけを扱うのは、BlackBoardLayerがExternalLayerを
    /// 参照しないため——ノードの中身が必要な側(SceneFlowを持っているApplication側)が自分で引く。
    /// </summary>
    public interface ISceneStateGetter : IStateGetter
    {
        /// <summary> 現在のシーンノードID。まだ一度も遷移していない場合は-1 </summary>
        int CurrentNodeId { get; }

        /// <summary> 現在のノードから遷移できるノードのID一覧 </summary>
        IReadOnlyList<int> NextNodeIds { get; }

        /// <summary> ノードに入ったときに、そのNodeIdを通知するチャンネル </summary>
        IActionChannel<int> Entered { get; }

        /// <summary> ノードから出たときに、そのNodeIdを通知するチャンネル </summary>
        IActionChannel<int> Exited { get; }
    }
}
