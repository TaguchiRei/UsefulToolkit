using System.Collections.Generic;
using System.Linq;
using System;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEngine;
using UsefulToolkit.External.Scene;

namespace UsefulToolkit.Editor.SceneFlowGraph
{
    /// <summary>
    /// シーン遷移図のキャンバス。ノードの配置とエッジ(=NextNodeIds)の編集を受け取り、
    /// SceneFlowGraphSerializer経由でアセットへ書き戻す。
    ///
    /// ノードの追加/削除やNodeIdの変更はSceneNodeData配列のインデックスをずらすため、
    /// そのたびにグラフを丸ごと作り直す。座標やエッジの変更だけなら作り直さない。
    /// </summary>
    internal sealed class SceneFlowGraphView : GraphView
    {
        private readonly Dictionary<int, SceneFlowNodeView> _nodeViewsById = new();

        private SceneFlowGraphSerializer _serializer;
        private SceneFlowBootNodeView _bootNodeView;
        private bool _isRebuilding;

        /// <summary> グラフの内容が変わったときに通知する。検証結果の再表示に使う </summary>
        public event Action GraphChanged;

        public SceneFlowGraphView()
        {
            style.flexGrow = 1f;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            grid.StretchToParentSize();
            Insert(0, grid);

            graphViewChanged = OnGraphViewChanged;
        }

        /// <summary> 編集対象を差し替えてグラフを組み直す。serializerがnullなら空のキャンバスになる </summary>
        public void Load(SceneFlowGraphSerializer serializer)
        {
            _serializer = serializer;
            Rebuild();
        }

        /// <summary> アセットの現在の内容からグラフを組み直す </summary>
        public void Rebuild()
        {
            _isRebuilding = true;

            try
            {
                DeleteElements(graphElements.ToList());
                _nodeViewsById.Clear();
                _bootNodeView = null;

                if (_serializer == null || !_serializer.IsValid) return;

                _serializer.Refresh();

                if (_serializer.HasBootNode)
                {
                    _bootNodeView = new SceneFlowBootNodeView(_serializer);
                    AddElement(_bootNodeView);
                }

                var nodeViews = new List<SceneFlowNodeView>(_serializer.NodeCount);

                for (var i = 0; i < _serializer.NodeCount; i++)
                {
                    var nodeView = new SceneFlowNodeView(_serializer, i, RequestRebuild);
                    AddElement(nodeView);
                    nodeViews.Add(nodeView);

                    // NodeIdが重複している不正な状態でも落ちないよう、先勝ちで登録する
                    _nodeViewsById.TryAdd(nodeView.NodeId, nodeView);
                }

                foreach (var nodeView in nodeViews)
                {
                    foreach (var nextNodeId in _serializer.GetNextNodeIds(nodeView.NodeIndex))
                    {
                        // 対応するノードがない遷移先は線を引かず、検証メッセージ側で知らせる
                        if (!_nodeViewsById.TryGetValue(nextNodeId, out var targetView)) continue;

                        AddElement(nodeView.OutputPort.ConnectTo(targetView.InputPort));
                    }
                }

                // Bootノードから起動ノードへの線。対応するノードがなければ引かず、検証メッセージ側で知らせる
                if (_bootNodeView != null && _nodeViewsById.TryGetValue(_serializer.GetEntryNodeId(), out var entryView))
                {
                    AddElement(_bootNodeView.OutputPort.ConnectTo(entryView.InputPort));
                }

                // ノード内のシーングループ編集UI(PropertyField)をアセットに結びつける
                this.Bind(_serializer.SerializedObject);
            }
            finally
            {
                _isRebuilding = false;
            }

            GraphChanged?.Invoke();
        }

        /// <summary> 指定座標に通常ノードを追加する </summary>
        public void AddNodeAt(Vector2 position)
        {
            if (_serializer == null || !_serializer.IsValid) return;

            _serializer.AddNode(position);
            Rebuild();
        }

        /// <summary> 指定座標にシンプルノード(Main+Additionalのみのノード)を追加する </summary>
        public void AddSimpleNodeAt(Vector2 position)
        {
            if (_serializer == null || !_serializer.IsValid) return;

            _serializer.AddSimpleNode(position);
            Rebuild();
        }

        /// <summary> 表示中のキャンバス中央の座標。ツールバーからノードを足すときの配置先に使う </summary>
        public Vector2 GetViewCenter()
        {
            return contentViewContainer.WorldToLocal(this.LocalToWorld(contentRect.center));
        }

        /// <summary>
        /// 遷移の向きが合っていて、まだつながっていないポートだけを接続候補にする。
        /// 自分自身への接続は許可する——同じノードでシーングループだけ切り替える遷移があるため。
        /// </summary>
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var connected = new HashSet<Port>();

            foreach (var edge in startPort.connections)
            {
                connected.Add(startPort.direction == Direction.Output ? edge.input : edge.output);
            }

            return ports.ToList()
                .Where(port => port.direction != startPort.direction && !connected.Contains(port))
                .ToList();
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (_serializer is { IsValid: true } && evt.target == this)
            {
                var position = contentViewContainer.WorldToLocal(evt.mousePosition);
                evt.menu.AppendAction("ノードを追加", _ => AddNodeAt(position));
                evt.menu.AppendAction("シンプルノードを追加", _ => AddSimpleNodeAt(position));
                evt.menu.AppendSeparator();
            }

            base.BuildContextualMenu(evt);
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_isRebuilding || _serializer == null || !_serializer.IsValid) return change;

            var structureChanged = false;

            if (change.elementsToRemove != null)
            {
                // ノードを消すとインデックスがずれるので、エッジの削除を先に済ませる
                foreach (var edge in change.elementsToRemove.OfType<Edge>())
                {
                    RemoveLinkOf(edge);
                }

                var removedIndices = change.elementsToRemove
                    .OfType<SceneFlowNodeView>()
                    .Select(nodeView => nodeView.NodeIndex)
                    .ToList();

                if (removedIndices.Count > 0)
                {
                    _serializer.RemoveNodes(removedIndices);
                    structureChanged = true;
                }
            }

            if (change.edgesToCreate != null)
            {
                foreach (var edge in change.edgesToCreate)
                {
                    if (edge.input?.node is not SceneFlowNodeView to) continue;

                    // Bootノードからの線は遷移ではなく起動ノードの指定
                    if (edge.output?.node is SceneFlowBootNodeView)
                    {
                        _serializer.SetEntryNodeId(to.NodeId);
                        continue;
                    }

                    if (edge.output?.node is not SceneFlowNodeView from) continue;

                    _serializer.AddLink(from.NodeIndex, to.NodeId);
                }
            }

            if (change.movedElements != null)
            {
                var positions = change.movedElements
                    .OfType<SceneFlowNodeView>()
                    .Select(nodeView => (nodeView.NodeIndex, nodeView.GetPosition().position))
                    .ToList();

                var bootPosition = change.movedElements.OfType<SceneFlowBootNodeView>().Any()
                    ? _bootNodeView?.GetPosition().position
                    : null;

                _serializer.SetPositions(positions, bootPosition);
            }

            if (structureChanged)
            {
                RequestRebuild();
            }
            else
            {
                GraphChanged?.Invoke();
            }

            return change;
        }

        private void RemoveLinkOf(Edge edge)
        {
            if (edge.input?.node is not SceneFlowNodeView to) return;

            // Bootノードからの線を外すと起動ノードが未設定に戻る。
            // 線を張り替えたときは古い線の削除と新しい線の作成が同時に来るので、
            // 今の設定先が外そうとしている線と一致するときだけ消す。
            if (edge.output?.node is SceneFlowBootNodeView)
            {
                if (_serializer.GetEntryNodeId() == to.NodeId)
                {
                    _serializer.SetEntryNodeId(SceneFlow.NoEntryNodeId);
                }

                return;
            }

            if (edge.output?.node is not SceneFlowNodeView from) return;

            _serializer.RemoveLink(from.NodeIndex, to.NodeId);
        }

        /// <summary> コールバックの最中に作り直すと不整合が出るため、1フレーム遅らせる </summary>
        private void RequestRebuild()
        {
            schedule.Execute(Rebuild).ExecuteLater(0);
        }
    }
}
