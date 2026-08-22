using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UsefulToolkit.Application.Scene;
using UsefulToolkit.BlackBoard.Scene;
using UsefulToolkit.External.Scene;

namespace Sandbox.Application
{
    /// <summary>
    /// シーン遷移テスト用のシーン管理クラス。本来はGoToTitle()のようなゲームの語彙のメソッドを
    /// 生やす場所だが、ここではノードエディタで組んだ任意のグラフをそのまま叩けるよう、
    /// nodeId/groupIndexを受け取るだけの薄いメソッドを公開している。
    /// </summary>
    public sealed class SandboxSceneController : SceneFlowControllerBase
    {
        public SandboxSceneController(SceneFlow flow, SceneBoard sceneBoard) : base(flow, sceneBoard)
        {
        }

        public new SceneGroupId CurrentGroup => base.CurrentGroup;

        public new IReadOnlyList<SceneGroupId> NextGroups => base.NextGroups;

        public new SceneTransitionPhase Phase => base.Phase;

        public new bool IsTransitioning => base.IsTransitioning;

        public int EntryNode => EntryNodeId;

        public int EntryGroup => EntryGroupIndex;

        public UniTask TransitionToAsync(int nodeId, int groupIndex) => TransitionTo(nodeId, groupIndex);

        public UniTask TransitionToEntryAsync() => TransitionToEntry();
    }
}
