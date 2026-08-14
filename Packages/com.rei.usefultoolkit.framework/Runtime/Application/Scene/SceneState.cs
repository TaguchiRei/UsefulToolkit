using System;
using System.Collections.Generic;
using UsefulToolkit.Application.StateManagement;
using UsefulToolkit.Framework.BlackBoard;
using UsefulToolkit.Framework.External;

namespace UsefulToolkit.Framework.Application
{
    /// <summary>
    /// 現在どのシーンノードにいるかを保持するState。
    /// SceneFlowControllerBaseがSingle Writerとして生成・保持し、SceneBoardへ登録する。
    /// 書き込み口のSetCurrentNodeはinternalなので、同一アセンブリのSceneFlowControllerBase
    /// からしか呼べない——Single Writerを規約ではなくコンパイラで縛っている。
    /// </summary>
    public sealed class SceneState : GameStateBase, ISceneStateGetter
    {
        /// <summary> まだ一度も遷移していないことを表すNodeId </summary>
        public const int NoneNodeId = -1;

        private readonly ActionChannel<int> _entered = new();
        private readonly ActionChannel<int> _exited = new();

        private int _currentNodeId = NoneNodeId;
        private IReadOnlyList<int> _nextNodeIds = Array.Empty<int>();

        public int CurrentNodeId => _currentNodeId;

        public IReadOnlyList<int> NextNodeIds => _nextNodeIds;

        public IActionChannel<int> Entered => _entered;

        public IActionChannel<int> Exited => _exited;

        /// <summary>
        /// 現在のノードを更新する(Single Writer)。旧ノードのExited、新ノードのEnteredの順に発火する。
        ///
        /// 同じノードへ遷移した場合も、実際に読み込まれたシーンが変化したかどうかに関係なく
        /// Exited→Enteredの両方を発火する。「遷移を要求したのに通知が来ない」状態を作らないため、
        /// 発火の抑制は行わない。
        /// </summary>
        /// <exception cref="ArgumentNullException">nodeがnullのときに出力</exception>
        internal void SetCurrentNode(SceneNode node)
        {
            if (node is null) throw new ArgumentNullException(nameof(node));

            var previousNodeId = _currentNodeId;

            _currentNodeId = node.NodeId;
            _nextNodeIds = node.NextNodeIds;

            if (previousNodeId != NoneNodeId)
            {
                _exited.Invoke(previousNodeId);
            }

            _entered.Invoke(node.NodeId);
        }

        public override string GetLog()
        {
            return $"CurrentNodeId: {_currentNodeId} / NextNodeIds: [{string.Join(", ", _nextNodeIds)}]";
        }
    }
}
