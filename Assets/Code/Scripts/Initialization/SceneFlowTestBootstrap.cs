using System.Collections.Generic;
using System;
using Cysharp.Threading.Tasks;
using FrameworkBlackBoard = UsefulToolkit.BlackBoard.BlackBoard.BlackBoard;
using Sandbox.Application;
using UnityEngine;
using UsefulToolkit.Application.Scene;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Scene;
using UsefulToolkit.EngineService.Scene;
using UsefulToolkit.External.Scene;

// 型名BlackBoardは、将来Sandbox.BlackBoard名前空間にクラスが増えると
// 単純名では名前空間側と衝突するため、別名で確定させておく

namespace Sandbox.Initialization
{
    /// <summary>
    /// シーン遷移システムを組み立てて動かすための、テスト用のInitialization層。
    /// フレームワーク側のInitialization層がまだ空なので、組み立てをここで手書きしている。
    ///
    /// このコンポーネントは常駐シーンに置き、そのシーンをSceneFlowアセットのBootノードの
    /// 「常駐シーン」に指定しておくこと。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class SceneFlowTestBootstrap : MonoBehaviour
    {
        [Header("ノードエディタで組んだSceneFlowアセット")]
        [SerializeField] private SceneFlowAssetBase _flowAsset;

        [Header("起動時にBootノードの起動ノードへ遷移するか")]
        [SerializeField] private bool _transitionOnStart = true;

        private readonly List<IDisposable> _subscriptions = new();

        private SceneLoadService _sceneLoadService;
        private IBlackBoard _blackBoard;

        /// <summary> GUIなどテスト用の表示側から掴むための参照 </summary>
        public static SceneFlowTestBootstrap Current { get; private set; }

        /// <summary> SceneFlowAssetから生成した実行時のシーン遷移図 </summary>
        public SceneFlow Flow { get; private set; }

        /// <summary> 遷移を起動できる唯一の型 </summary>
        public SandboxSceneController Controller { get; private set; }

        /// <summary> 直近に通知されたロード進捗(0..1) </summary>
        public float LastProgress { get; private set; }

        /// <summary> 現在読み込んでいるシーングループ。まだ構築できていない場合はSceneGroupId.None </summary>
        public SceneGroupId CurrentGroup => Controller?.CurrentGroup ?? SceneGroupId.None;

        /// <summary> BlackBoard経由で取得したシーンStateの読み取り面。まだ登録されていなければnull </summary>
        public ISceneStateGetter SceneState =>
            _blackBoard != null && _blackBoard.TryGetSceneState(out var sceneState) ? sceneState : null;

        private void Awake()
        {
            if (Current != null && Current != this)
            {
                Debug.LogWarning($"[SceneFlowTest] {nameof(SceneFlowTestBootstrap)} が複数あります。後から来た方を無効化します。", this);
                enabled = false;
                return;
            }

            if (_flowAsset == null)
            {
                Debug.LogError($"[SceneFlowTest] SceneFlowアセットが設定されていません。", this);
                enabled = false;
                return;
            }

            Current = this;

            // SceneBoardだけはBlackBoardのコンストラクタに渡す特別扱いなので、先に作る
            var sceneBoard = new SceneBoard();
            var blackBoard = new FrameworkBlackBoard(sceneBoard);
            _blackBoard = blackBoard;

            // SceneLoadServiceとControllerはどちらを先に作っても動く
            _sceneLoadService = new SceneLoadService(blackBoard, sceneBoard);

            Flow = _flowAsset.Build();
            Controller = new SandboxSceneController(Flow, sceneBoard);

            _subscriptions.Add(Controller.Progress.Register(OnProgress));

            // 通知が飛んでいるかをログで確認するための購読
            var sceneState = SceneState;
            if (sceneState != null)
            {
                _subscriptions.Add(sceneState.RegisterOnPhaseChanged(OnPhaseChanged));
                _subscriptions.Add(sceneState.RegisterOnCurrentGroupChanged(OnCurrentGroupChanged));
            }

            Debug.Log($"[SceneFlowTest] SceneFlowを構築しました。ノード数: {Flow.Nodes.Count}");
        }

        private async void Start()
        {
            if (!_transitionOnStart || Controller == null) return;

            await TransitionToEntryAsync();
        }

        /// <summary>
        /// Bootノードから線を引いた起動ノードへ遷移する。TransitionAsyncと同じく例外はログに出す。
        /// </summary>
        public async UniTask TransitionToEntryAsync()
        {
            if (Controller == null) return;

            try
            {
                Debug.Log($"[SceneFlowTest] 起動ノードへ遷移開始 node[{Controller.EntryNode}] group[{Controller.EntryGroup}]");
                await Controller.TransitionToEntryAsync();
                Debug.Log($"[SceneFlowTest] 起動ノードへの遷移完了 {Controller.CurrentGroup}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[SceneFlowTest] 起動ノードへの遷移に失敗しました\n{exception}", this);
            }
        }

        private void OnDestroy()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();

            _sceneLoadService?.Dispose();
            _sceneLoadService = null;
            _blackBoard = null;

            if (Current == this) Current = null;
        }

        /// <summary> 遷移を起動し、例外はログに出して握り潰す </summary>
        public async UniTask TransitionAsync(int nodeId, int groupIndex)
        {
            if (Controller == null) return;

            try
            {
                Debug.Log($"[SceneFlowTest] 遷移開始 node[{nodeId}] group[{groupIndex}]");
                await Controller.TransitionToAsync(nodeId, groupIndex);
                Debug.Log($"[SceneFlowTest] 遷移完了 {Controller.CurrentGroup}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[SceneFlowTest] 遷移に失敗しました node[{nodeId}] group[{groupIndex}]\n{exception}", this);
            }
        }

        /// <summary> 遷移完了の通知を受けたハンドラの中から、そのまま次の遷移を開始する </summary>
        public UniTask ChainTransitionAsync(int firstNodeId, int firstGroupIndex, int secondNodeId, int secondGroupIndex)
        {
            var sceneState = SceneState;
            if (sceneState == null) return UniTask.CompletedTask;

            IDisposable subscription = null;

            void OnGroupChanged(StateContext<SceneGroupId> context)
            {
                // 1回だけ反応すればよいので、その場で解除してから次の遷移へ進む
                subscription?.Dispose();
                subscription = null;

                Debug.Log("[SceneFlowTest] 完了通知の中から次の遷移を開始します");
                TransitionAsync(secondNodeId, secondGroupIndex).Forget();
            }

            subscription = sceneState.RegisterOnCurrentGroupChanged(OnGroupChanged);

            return TransitionAsync(firstNodeId, firstGroupIndex);
        }

        private void OnProgress(float progress)
        {
            LastProgress = progress;
            Debug.Log($"[SceneFlowTest] 進捗 {progress:P0}");
        }

        private void OnPhaseChanged(StateContext<SceneTransitionPhase> context)
        {
            Debug.Log($"[SceneFlowTest] Phase {context.OldValue} → {context.NewValue}");
        }

        private void OnCurrentGroupChanged(StateContext<SceneGroupId> context)
        {
            Debug.Log($"[SceneFlowTest] CurrentGroup {context.OldValue} → {context.NewValue}");
        }

        /// <summary> ログ表示用にノードの表示名を引く </summary>
        public string GetNodeLabel(int nodeId)
        {
            if (Flow != null && Flow.TryGetNode(nodeId, out var node) && !string.IsNullOrEmpty(node.DisplayName))
            {
                return node.DisplayName;
            }

            return $"(node {nodeId})";
        }
    }
}
