using System;
using System.Collections.Generic;
using System.Linq;
using UsefulToolkit.Application.StateManagement;
using UsefulToolkit.BlackBoard;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// 現在のシーンノードおよび次に遷移可能なシーンノードの取得、
    /// シーンノードへの出入りタイミングでのAction登録を行うGetterインターフェース。
    /// 実際のシーン読み込みのトリガーは<see cref="ISceneChangeEvent{T}"/>(EventBoard側)の責務。
    /// </summary>
    public interface ISceneStateGetter<T> : IStateGetter where T : Enum
    {
        /// <summary>
        /// 現在のシーンノードIDを取得する
        /// </summary>
        int GetCurrentSceneNodeId();

        /// <summary>
        /// 遷移可能なシーンノードIDを取得する
        /// </summary>
        int[] GetNextSceneNodeId();

        /// <summary>
        /// 現在のシーンノードを取得する
        /// </summary>
        SceneNode<T> GetCurrentSceneNode();

        /// <summary>
        /// 特定のシーンノードに入った際に呼び出されるAction。
        /// </summary>
        /// <param name="id">登録するシーンノードのID</param>
        /// <param name="action">登録するアクション。</param>
        /// <returns>Disposeを実行すると実行リストから除去される</returns>
        IDisposable RegisterSceneNodeLoadedAction(int id, Action<int> action);

        /// <summary>
        /// 特定のシーンがアンロードされたときに呼び出されるAction
        /// </summary>
        IDisposable RegisterSceneNodeUnloadedAction(int id, Action<int> action);
    }

    /// <summary>
    /// 現在のシーンおよび次のシーンの取得、シーン変更時の条件取得などを行うクラス。
    /// SceneFlowController&lt;T&gt;がSingle Writerとして生成・保持し、SceneBoardへ登録する。
    /// </summary>
    public sealed class SceneState<T> : GameStateBase, ISceneStateGetter<T> where T : Enum
    {
        private readonly SceneFlowBase<T> _flow;
        private int _currentNodeId = -1;

        private readonly Dictionary<int, EventChannel<int>> _loadedChannels = new();
        private readonly Dictionary<int, EventChannel<int>> _unloadedChannels = new();

        public SceneState(SceneFlowBase<T> flow)
        {
            _flow = flow;
        }

        public int GetCurrentSceneNodeId() => _currentNodeId;

        public int[] GetNextSceneNodeId() =>
            GetCurrentSceneNode()?.NextScenes.Select(n => n.NodeId).ToArray() ?? Array.Empty<int>();

        public SceneNode<T> GetCurrentSceneNode() =>
            _flow.SceneNodes.FirstOrDefault(n => n.NodeId == _currentNodeId);

        public IDisposable RegisterSceneNodeLoadedAction(int id, Action<int> action) =>
            GetOrCreateChannel(_loadedChannels, id).Register(action);

        public IDisposable RegisterSceneNodeUnloadedAction(int id, Action<int> action) =>
            GetOrCreateChannel(_unloadedChannels, id).Register(action);

        /// <summary>
        /// 現在のシーンノードを更新する(Single Writer)。旧ノードのUnloaded、新ノードのLoadedを発火する。
        /// Getterインターフェースには出さず、同一アセンブリ(Application)内のSceneFlowController&lt;T&gt;からのみ呼ばれる想定。
        /// </summary>
        internal void SetCurrentNodeId(int nodeId)
        {
            var previous = _currentNodeId;
            _currentNodeId = nodeId;

            if (previous >= 0 && _unloadedChannels.TryGetValue(previous, out var unloadedChannel))
                unloadedChannel.Publish(previous);

            if (_loadedChannels.TryGetValue(nodeId, out var loadedChannel))
                loadedChannel.Publish(nodeId);
        }

        private static EventChannel<int> GetOrCreateChannel(Dictionary<int, EventChannel<int>> channels, int id)
        {
            if (!channels.TryGetValue(id, out var channel))
            {
                channel = new EventChannel<int>();
                channels[id] = channel;
            }

            return channel;
        }
    }
}
