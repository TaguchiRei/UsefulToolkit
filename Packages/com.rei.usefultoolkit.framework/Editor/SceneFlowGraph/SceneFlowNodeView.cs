using System;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// シーン遷移図の1ノードの見た目。
    /// SceneNodeDataの1要素と1対1で対応し、対応先はNodeIndexで指す。
    /// 構造が変わる操作(ノードの追加/削除、NodeIdの変更)をしたあとはインデックスがずれるため、
    /// SceneFlowGraphViewがグラフごと作り直す。
    /// </summary>
    internal sealed class SceneFlowNodeView : Node
    {
        private readonly SceneFlowGraphSerializer _serializer;
        private readonly Action _structureChanged;

        /// <summary> _nodes配列上のインデックス </summary>
        public int NodeIndex { get; }

        /// <summary> このノードのNodeId。エッジの張り直しに使う </summary>
        public int NodeId { get; private set; }

        /// <summary> 遷移元になる側のポート </summary>
        public Port OutputPort { get; }

        /// <summary> 遷移先になる側のポート </summary>
        public Port InputPort { get; }

        public SceneFlowNodeView(SceneFlowGraphSerializer serializer, int nodeIndex, Action structureChanged)
        {
            _serializer = serializer;
            _structureChanged = structureChanged;
            NodeIndex = nodeIndex;
            NodeId = serializer.GetNodeId(nodeIndex);

            style.width = 320f;

            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            InputPort.portName = "遷移元";
            inputContainer.Add(InputPort);

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            OutputPort.portName = "遷移先";
            outputContainer.Add(OutputPort);

            BuildBody();

            RefreshExpandedState();
            RefreshPorts();
            UpdateTitle(serializer.GetDisplayName(nodeIndex));
            SetPosition(new Rect(serializer.GetPosition(nodeIndex), Vector2.zero));
        }

        private void BuildBody()
        {
            var idField = new IntegerField("NodeId") { value = NodeId };
            idField.tooltip = "遷移先の指定に使うID。変更すると他ノードの遷移先も自動で追従する";
            idField.RegisterValueChangedCallback(evt => OnNodeIdChanged(idField, evt.newValue));
            extensionContainer.Add(idField);

            var nameField = new TextField("表示名") { value = _serializer.GetDisplayName(NodeIndex) };
            nameField.tooltip = "グラフ上とログでの表示名。遷移の挙動には影響しない";
            nameField.RegisterValueChangedCallback(evt =>
            {
                _serializer.SetDisplayName(NodeIndex, evt.newValue);
                UpdateTitle(evt.newValue);
            });
            extensionContainer.Add(nameField);

            // シーンenumの型引数を知らなくても、PropertyFieldに任せればenumのドロップダウンまで出せる
            var groupsField = new PropertyField(_serializer.GetGroupsProperty(NodeIndex), "シーングループ");
            groupsField.tooltip = "このノードで選べるシーンの組み合わせ。TransitionToのgroupIndexはこの並び順";
            extensionContainer.Add(groupsField);
        }

        /// <summary>
        /// 重複するNodeIdは受け付けない。弾いた場合は入力欄を元の値へ戻す。
        /// </summary>
        private void OnNodeIdChanged(IntegerField idField, int newId)
        {
            if (newId == NodeId) return;

            if (!_serializer.TryChangeNodeId(NodeIndex, newId))
            {
                Debug.LogWarning($"[UsefulToolkit] NodeId [{newId}] はすでに他のノードが使っています。");
                idField.SetValueWithoutNotify(NodeId);
                return;
            }

            NodeId = newId;
            UpdateTitle(_serializer.GetDisplayName(NodeIndex));
            _structureChanged?.Invoke();
        }

        private void UpdateTitle(string displayName)
        {
            title = string.IsNullOrWhiteSpace(displayName) ? $"Node {NodeId}" : $"{displayName} ({NodeId})";
        }
    }
}
