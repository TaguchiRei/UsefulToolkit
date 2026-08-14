using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UsefulToolkit.Framework.BlackBoard;
using UsefulToolkit.Framework.External;

namespace UsefulToolkit.Framework.Application
{
    /// <summary>
    /// シーン遷移のUsecase基底クラス。利用側はこれを継承して自分のシーン管理クラスを作り、
    /// ゲームの語彙に合わせたメソッド(GoToStageなど)からTransitionToを呼ぶ。
    /// <code>
    /// public sealed class GameSceneManager : SceneFlowControllerBase
    /// {
    ///     public GameSceneManager(SceneFlow flow, IBlackBoard blackBoard) : base(flow, blackBoard) { }
    ///     public UniTask GoToStage(int stageNodeId) => TransitionTo(stageNodeId, 0);
    /// }
    /// </code>
    /// TransitionToをprotectedにしているのは、遷移を起動できるクラスを
    /// 「派生した1つのシーン管理クラスだけ」に型で限定するため。
    /// </summary>
    public abstract class SceneFlowControllerBase : IProgress<float>
    {
        private readonly SceneFlow _flow;
        private readonly SceneBoard _sceneBoard;
        private readonly SceneState _sceneState;
        private readonly ActionChannel<float> _progress = new();

        private bool _isTransitioning;

        /// <summary> シーン読み込みの進捗(0..1)を通知するチャンネル </summary>
        public IActionChannel<float> Progress => _progress;

        /// <summary> 現在のシーンノードID。まだ一度も遷移していない場合はSceneState.NoneNodeId </summary>
        protected int CurrentNodeId => _sceneState.CurrentNodeId;

        /// <summary> 現在のノードから遷移できるノードのID一覧 </summary>
        protected IReadOnlyList<int> NextNodeIds => _sceneState.NextNodeIds;

        /// <exception cref="ArgumentNullException">flowまたはblackBoardがnullのときに出力</exception>
        protected SceneFlowControllerBase(SceneFlow flow, IBlackBoard blackBoard)
        {
            if (blackBoard is null) throw new ArgumentNullException(nameof(blackBoard));

            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _sceneBoard = blackBoard.SceneBoard;

            _sceneState = new SceneState();
            _sceneBoard.RegisterGameState<ISceneStateGetter>(_sceneState);
        }

        /// <summary>
        /// 指定したノード・シーングループへ遷移する。
        /// 実ロードが終わってからSceneStateを更新するため、遷移中にCurrentNodeIdが
        /// 中途半端な値になることはない。
        /// </summary>
        /// <param name="nodeId">遷移先のノードID</param>
        /// <param name="groupIndex">そのノードのSceneGroupsのインデックス</param>
        /// <exception cref="InvalidOperationException">すでに遷移中のときに出力</exception>
        /// <exception cref="ArgumentOutOfRangeException">ノードまたはグループが存在しないときに出力</exception>
        protected async UniTask TransitionTo(int nodeId, int groupIndex)
        {
            if (_isTransitioning)
            {
                throw new InvalidOperationException(
                    $"シーン遷移中です。前の遷移が完了するまでノード [{nodeId}] への遷移は開始できません。");
            }

            if (!_flow.TryGetNode(nodeId, out var node))
            {
                throw new ArgumentOutOfRangeException(nameof(nodeId), $"ノード [{nodeId}] はSceneFlowに存在しません。");
            }

            if (!node.TryGetGroup(groupIndex, out var group))
            {
                throw new ArgumentOutOfRangeException(nameof(groupIndex),
                    $"ノード [{nodeId}] にシーングループ [{groupIndex}] は存在しません。");
            }

            _isTransitioning = true;

            try
            {
                await _sceneBoard.RequestTransitionAsync(group.Scenes, this);
                _sceneState.SetCurrentNode(node);
            }
            finally
            {
                // 遷移が例外で落ちても次の遷移を試せるよう、必ずフラグを戻す
                _isTransitioning = false;
            }
        }

        void IProgress<float>.Report(float value) => _progress.Invoke(value);
    }
}
