using System.Collections.Generic;
using System;
using Cysharp.Threading.Tasks;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Scene;
using UsefulToolkit.External.Scene;

namespace UsefulToolkit.Application.Scene
{
    /// <summary>
    /// シーン遷移の基底クラス。利用側はこれを継承して自分のシーン管理クラスを作り、
    /// ゲームの語彙に合わせたメソッド(GoToStageなど)からTransitionToを呼ぶ。
    /// TransitionToがprotectedなので、遷移を起動できるのは派生したこのクラスだけになる。
    /// <code>
    /// public sealed class GameSceneManager : SceneFlowControllerBase
    /// {
    ///     public GameSceneManager(SceneFlow flow, SceneBoard sceneBoard) : base(flow, sceneBoard) { }
    ///     public UniTask GoToStage(int stageNodeId) => TransitionTo(stageNodeId, 0);
    /// }
    /// </code>
    /// 生成すると、遷移状態を保持するSceneStateがSceneBoardへ登録される。
    /// </summary>
    public abstract class SceneFlowControllerBase : IProgress<float>
    {
        private readonly SceneFlow _flow;
        private readonly SceneState _sceneState;
        private readonly ActionChannel<float> _progress = new();

        /// <summary> シーン読み込みの進捗(0..1)を通知するチャンネル </summary>
        public IActionChannel<float> Progress => _progress;

        /// <summary> 現在読み込んでいるシーングループ。未遷移または遷移失敗時はSceneGroupId.None </summary>
        protected SceneGroupId CurrentGroup => _sceneState.CurrentGroup;

        /// <summary> 現在のグループから遷移できるシーングループの一覧 </summary>
        protected IReadOnlyList<SceneGroupId> NextGroups => _sceneState.NextGroups;

        /// <summary> 遷移の進行状況 </summary>
        protected SceneTransitionPhase Phase => _sceneState.Phase;

        /// <summary> 遷移中かどうか。TransitionToを例外なしで見送りたい場合はこれで確認する </summary>
        protected bool IsTransitioning => _sceneState.IsTransitioning;

        /// <summary> Bootノードで指定した起動時の遷移先。未設定ならSceneFlow.NoEntryNodeId </summary>
        protected int EntryNodeId => _flow.EntryNodeId;

        /// <summary> Bootノードで指定した起動時のシーングループ </summary>
        protected int EntryGroupIndex => _flow.EntryGroupIndex;

        /// <exception cref="ArgumentNullException">flowまたはsceneBoardがnullのときに出力</exception>
        protected SceneFlowControllerBase(SceneFlow flow, SceneBoard sceneBoard)
        {
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            if (sceneBoard is null) throw new ArgumentNullException(nameof(sceneBoard));

            _sceneState = new SceneState(_flow.PersistentScenes);

            // 読み取り面と登録面を型で分けて公開するため、同じインスタンスを2つの型で登録する
            sceneBoard.RegisterGameState<ISceneStateGetter>(_sceneState);
            sceneBoard.RegisterGameState<ISceneLoaderRegister>(_sceneState);
        }

        /// <summary>
        /// Bootノードから線を引いた起動ノードへ遷移する。起動直後に一度だけ呼ぶ想定。
        /// </summary>
        /// <exception cref="InvalidOperationException">起動ノードが未設定、またはすでに遷移中のときに出力</exception>
        protected UniTask TransitionToEntry()
        {
            if (!_flow.HasEntry)
            {
                throw new InvalidOperationException(
                    "Bootノードに起動時の遷移先が設定されていません。ノードエディタでBootノードから線を引いてください。");
            }

            return TransitionTo(_flow.EntryNodeId, _flow.EntryGroupIndex);
        }

        /// <summary>
        /// 指定したノード・シーングループへ遷移する。遷移元と共通のシーンは読み直されない
        /// (読み直したい場合は遷移先のSceneGroupのForceReloadをオンにする)。
        /// 完了するとCurrentGroupが更新され、登録済みのActionが実行される。
        ///
        /// 遷移が例外で中断した場合はCurrentGroupがSceneGroupId.None・PhaseがFailedになり、
        /// 例外はそのまま呼び出し元へ投げる。
        /// </summary>
        /// <param name="nodeId">遷移先のノードID</param>
        /// <param name="groupIndex">そのノードのGroupsのインデックス</param>
        /// <exception cref="InvalidOperationException">すでに遷移中のときに出力</exception>
        /// <exception cref="ArgumentOutOfRangeException">ノードまたはグループが存在しないときに出力</exception>
        protected async UniTask TransitionTo(int nodeId, int groupIndex)
        {
            if (_sceneState.IsTransitioning)
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

            _sceneState.BeginTransition();

            try
            {
                await _sceneState.RequestTransitionAsync(
                    group.Scenes, group.MainScene, group.ForceReload, this);
            }
            catch
            {
                _sceneState.FailTransition();
                throw;
            }

            _sceneState.CompleteTransition(new SceneGroupId(nodeId, groupIndex), BuildNextGroups(node));
        }

        /// <summary>
        /// NextNodeIdsが指すノードの全シーングループをSceneGroupIdへ展開する。
        /// BlackBoardLayerはSceneNodeを参照できないため、展開はSceneFlowを持つここで行う。
        /// </summary>
        private List<SceneGroupId> BuildNextGroups(SceneNode node)
        {
            var nextGroups = new List<SceneGroupId>();

            foreach (var nextNodeId in node.NextNodeIds)
            {
                if (!_flow.TryGetNode(nextNodeId, out var nextNode)) continue;

                for (var groupIndex = 0; groupIndex < nextNode.Groups.Count; groupIndex++)
                {
                    nextGroups.Add(new SceneGroupId(nextNodeId, groupIndex));
                }
            }

            return nextGroups;
        }

        void IProgress<float>.Report(float value) => _progress.Invoke(value);
    }
}
