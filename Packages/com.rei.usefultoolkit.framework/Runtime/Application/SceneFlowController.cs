using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using UsefulToolkit.BlackBoard;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// シーン遷移のゲームコード向け入口となるUsecase。SceneStateを生成しSceneBoardへ、
    /// SceneChangeBoardを生成しIBlackBoardへ、それぞれ登録する。
    /// TransitionToはSceneChangeBoard経由でSceneLoadServiceの実ロードを起動し、
    /// 完了後にSceneState(Single Writer)を更新する。
    /// </summary>
    public sealed class SceneFlowController<T> : IProgress<float> where T : Enum
    {
        private readonly SceneFlowBase<T> _flow;
        private readonly SceneState<T> _sceneState;
        private readonly SceneChangeBoard<T> _changeBoard;

        /// <summary>シーン読み込みの進捗(0..1)。IProgress&lt;float&gt;.Reportから転送される。</summary>
        public event Action<float> OnProgress;

        public SceneFlowController(SceneFlowBase<T> flow, IBlackBoard blackBoard)
        {
            _flow = flow;

            _sceneState = new SceneState<T>(flow);
            if (!blackBoard.TryGetStateChildBoard<SceneBoard>(out var sceneBoard))
                throw new InvalidOperationException($"{nameof(SceneBoard)}が{nameof(IBlackBoard)}に見つかりません。");
            sceneBoard.TryRegisterState<ISceneStateGetter<T>>(_sceneState);

            _changeBoard = new SceneChangeBoard<T>();
            blackBoard.TryRegisterEventChildBoard(_changeBoard);
        }

        /// <summary>指定したシーンノード・シーングループへ遷移する。groupIdはSceneNode.SceneGroupsの配列インデックス。</summary>
        public async UniTask TransitionTo(int nodeId, int groupId)
        {
            var node = _flow.SceneNodes.First(n => n.NodeId == nodeId);
            var group = node.SceneGroups[groupId];

            await _changeBoard.RequestTransitionAsync(group.Scenes.ToArray(), this);
            _sceneState.SetCurrentNodeId(nodeId);
        }

        public SceneNode<T> GetCurrentNode() => _sceneState.GetCurrentSceneNode();

        public int[] GetNextNodeIds() => _sceneState.GetNextSceneNodeId();

        void IProgress<float>.Report(float value) => OnProgress?.Invoke(value);
    }
}
