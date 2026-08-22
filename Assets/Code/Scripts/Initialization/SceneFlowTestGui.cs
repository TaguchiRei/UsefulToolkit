using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using UnityEngine;
using UsefulToolkit.Application.Scene;

namespace Sandbox.Initialization
{
    /// <summary>
    /// シーン遷移テスト用の操作パネル。プレイモードで画面左上に出る。
    /// SceneFlowアセットに組まれたノード・シーングループをそのままボタンにするので、
    /// ノードエディタで組み替えたらそのまま反映される。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SceneFlowTestBootstrap))]
    public sealed class SceneFlowTestGui : MonoBehaviour
    {
        private readonly StringBuilder _stringBuilder = new();

        private Vector2 _scrollPosition;
        private GUIStyle _richLabelStyle;

        private void OnGUI()
        {
            var bootstrap = SceneFlowTestBootstrap.Current;
            if (bootstrap == null || bootstrap.Flow == null) return;

            GUILayout.BeginArea(new Rect(10f, 10f, 380f, Screen.height - 20f), GUI.skin.box);
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            DrawStatus(bootstrap);
            GUILayout.Space(8f);
            DrawLoadedScenes();
            GUILayout.Space(8f);
            DrawTransitionButtons(bootstrap);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawStatus(SceneFlowTestBootstrap bootstrap)
        {
            GUILayout.Label("<b>SceneFlow テスト</b>", RichLabelStyle);

            var currentGroup = bootstrap.CurrentGroup;
            GUILayout.Label($"現在グループ: {currentGroup} {bootstrap.GetNodeLabel(currentGroup.NodeId)}");

            var sceneState = bootstrap.SceneState;
            GUILayout.Label(sceneState != null
                ? $"Phase: {sceneState.Phase}"
                : "Phase: (State未登録)");

            var controller = bootstrap.Controller;
            if (controller != null)
            {
                GUILayout.Label($"遷移先候補: [{string.Join(", ", controller.NextGroups)}]");
            }

            GUILayout.Label($"直近の進捗: {bootstrap.LastProgress:P0}");

            var flow = bootstrap.Flow;
            var persistentScenes = flow.PersistentScenes.Count > 0
                ? string.Join(" + ", flow.PersistentScenes)
                : "(なし)";

            GUILayout.Label($"常駐シーン: {persistentScenes}");
            GUILayout.Label(flow.HasEntry
                ? $"起動ノード: {flow.EntryNodeId} {bootstrap.GetNodeLabel(flow.EntryNodeId)} / group {flow.EntryGroupIndex}"
                : "起動ノード: 未設定");
        }

        private void DrawLoadedScenes()
        {
            _stringBuilder.Clear();
            _stringBuilder.Append("読み込み済みシーン:");

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                _stringBuilder.Append("\n  ").Append(SceneManager.GetSceneAt(i).name);
            }

            GUILayout.Label(_stringBuilder.ToString());
        }

        private void DrawTransitionButtons(SceneFlowTestBootstrap bootstrap)
        {
            GUILayout.Label("<b>遷移</b>", RichLabelStyle);

            if (bootstrap.Flow.HasEntry && GUILayout.Button("起動ノードへ遷移"))
            {
                bootstrap.TransitionToEntryAsync().Forget();
            }

            foreach (var node in bootstrap.Flow.Nodes)
            {
                var nodeLabel = string.IsNullOrEmpty(node.DisplayName) ? $"node {node.NodeId}" : node.DisplayName;
                GUILayout.Label($"{nodeLabel} (id: {node.NodeId})");

                if (node.Groups.Count == 0)
                {
                    GUILayout.Label("  シーングループがありません");
                    continue;
                }

                for (var groupIndex = 0; groupIndex < node.Groups.Count; groupIndex++)
                {
                    var group = node.Groups[groupIndex];
                    var forceReloadMark = group.ForceReload ? " [ForceReload]" : string.Empty;
                    var buttonLabel = $"  group {groupIndex}: {string.Join(" + ", group.Scenes)}{forceReloadMark}";

                    if (GUILayout.Button(buttonLabel))
                    {
                        bootstrap.TransitionAsync(node.NodeId, groupIndex).Forget();
                    }
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("<b>異常系</b>", RichLabelStyle);

            if (GUILayout.Button("遷移中に再入する (InvalidOperationException が出れば正常)"))
            {
                var firstNode = bootstrap.Flow.Nodes.Count > 0 ? bootstrap.Flow.Nodes[0] : null;
                if (firstNode != null && firstNode.Groups.Count > 0)
                {
                    // 1回目をawaitせずに2回目を投げる
                    bootstrap.TransitionAsync(firstNode.NodeId, 0).Forget();
                    bootstrap.TransitionAsync(firstNode.NodeId, 0).Forget();
                }
            }

            if (GUILayout.Button("存在しないノードへ遷移する (ArgumentOutOfRangeException が出れば正常)"))
            {
                bootstrap.TransitionAsync(int.MinValue, 0).Forget();
            }

            if (GUILayout.Button("完了通知の中から次の遷移を始める (例外が出なければ正常)"))
            {
                var nodes = bootstrap.Flow.Nodes;
                if (nodes.Count >= 2 && nodes[0].Groups.Count > 0 && nodes[1].Groups.Count > 0)
                {
                    bootstrap.ChainTransitionAsync(nodes[0].NodeId, 0, nodes[1].NodeId, 0).Forget();
                }
                else
                {
                    Debug.LogWarning("[SceneFlowTest] このテストにはシーングループを持つノードが2つ以上必要です");
                }
            }
        }

        /// <summary> GUI.skinはOnGUIの中でしか触れないので、初回のOnGUIで作って使い回す </summary>
        private GUIStyle RichLabelStyle => _richLabelStyle ??= new GUIStyle(GUI.skin.label) { richText = true };
    }
}
