using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UsefulToolkit.Attributes;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.BlackBoard.Scene;
using UsefulToolkit.Utility;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace UsefulToolkit.Initialization
{
    /// <summary>
    /// 常駐シーンの合成ルート。<see cref="GameCompositor{TSelf}"/> の責務 (Inject / Initialize) に加えて、
    /// ゲーム全体で唯一の BlackBoard の構築、Toolkit ランタイム機能の初期化、
    /// プロジェクト全 ChildBoard の登録、そして初期化完了後の開始シーンへの遷移を担う。
    ///
    /// ChildBoard の登録をこの Root だけに集約するのが本設計の方針。非 Root のシーン Compositor は
    /// この Root が構築・登録済みの共有 BlackBoard を読むだけで、自分では何も登録しない。
    ///
    /// DI コンテナ上では、この Root のスコープが全 Compositor 共通のフォールバック先になる。
    /// 常駐シーンの Initializer が登録した実体は、後からロードされたどのシーンからでも Inject で受け取れる。
    /// </summary>
    [DefaultExecutionOrder(InitializeOrderConst.Compositor)]
    public abstract class RootGameCompositor<TSelf> : GameCompositor<TSelf>
        where TSelf : RootGameCompositor<TSelf>
    {
        /// <summary>
        /// Toolkit のランタイム機能を初期化する Initializer。
        /// 他の Initializer の Awake より先に初期化するため、直接参照して呼ぶ。
        ///
        /// この Initializer だけは base.Awake (_instance の設定と収集フェーズ開始) より前に走る。
        /// その為、TryRegisterContent でのコンテナへの登録も、RegisterChildBoards 後にしか
        /// 存在しない ChildBoard の取得もできない。触れるのは BlackBoard 本体と SceneBoard のみ。
        /// </summary>
        [SerializeField] private UsefulToolkitRuntimeInitializer _runtimeInitializer;

        [SerializeField]
        [ScenePopup]
        [Tooltip("初期化完了後に自動で遷移する単一シーン。空欄なら遷移しない。シーングループには対応しない。")]
        private string _startScene;

        // この個体が共有 BlackBoard を構築した本人か。破棄時に、他の Root が構築した
        // 共有 BlackBoard まで巻き込んで解放しないためのフラグ。
        private bool _ownsSharedBlackBoard;

        protected override void Awake()
        {
            // UsefulToolkit.BlackBoard が名前空間として解決されてしまうため完全修飾する。
            var blackBoard = new UsefulToolkit.BlackBoard.BlackBoard.BlackBoard(new SceneBoard());

            if (!TrySetSharedBlackBoard(blackBoard))
            {
                enabled = false;
                return;
            }

            _ownsSharedBlackBoard = true;

            // 他のシーンの Compositor が依存を解決する際のフォールバック先になる。
            if (!TrySetAsRootScope())
            {
                UsefulLogger.LogError(
                    "別の Root Compositor が既に Root スコープを占有しています。", this);

                // 共有 BlackBoard と Root スコープは常に一緒に設定・解放する。
                // 中身が空のまま共有スロットに残ると、他シーンの Compositor が初期化を続行してしまう。
                ClearSharedBlackBoard();
                _ownsSharedBlackBoard = false;
                enabled = false;
                return;
            }

            // 他の Initializer の Awake から既にシーンシステムを使えるよう、ここで真っ先に初期化する。
            if (_runtimeInitializer != null)
            {
                _runtimeInitializer.Initialize(blackBoard);
            }
            else
            {
                UsefulLogger.LogError(
                    "UsefulToolkitRuntimeInitializer が設定されていない為、シーンシステムは初期化されません。", this);
            }

            base.Awake();

            // base.Awake が停止・中断した場合 (共有 BlackBoard の未設定など) はボード登録へ進まない。
            if (CurrentPhase != InitializePhase.Collection) return;

            RegisterChildBoards(blackBoard);
        }

        protected override void Start()
        {
            base.Start();

            // base.Start が Inject / Initialize を完走した場合のみ Initialize フェーズになる。
            if (CurrentPhase != InitializePhase.Initialize) return;

            TransitionToStartSceneAsync().Forget();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_ownsSharedBlackBoard)
            {
                ClearSharedBlackBoard();
                ClearRootScope();
                _ownsSharedBlackBoard = false;
            }
        }

        /// <summary>
        /// このゲームで使う ChildBoard を全て共有 BlackBoard へ登録する。
        /// SceneBoard は BlackBoard のコンストラクタが受け取るため、ここでは登録しない。
        /// 生成された派生クラスがプロジェクト全体の ChildBoard を列挙して override する。
        /// </summary>
        protected abstract void RegisterChildBoards(IBlackBoard blackBoard);

        /// <summary>
        /// <see cref="_startScene"/> が指定されていれば、そのシーンへ単発で遷移する。
        /// Build 登録済みなら SceneState 経由で上書きロードし、未登録なら Editor 専用ロードで開く。
        /// </summary>
        private async UniTaskVoid TransitionToStartSceneAsync()
        {
            if (string.IsNullOrEmpty(_startScene)) return;

            if (!SharedBlackBoard.GetSceneBoard().TryGetGameState<ISceneState>(out var sceneState))
            {
                UsefulLogger.LogError("ISceneState を取得できない為、開始シーンへ遷移できません。", this);
                return;
            }

            int buildIndex = SceneUtility.GetBuildIndexByScenePath(_startScene);

            if (buildIndex >= 0)
            {
                await sceneState.RequestOverwriteLoadAsync(buildIndex, Array.Empty<int>());
                return;
            }

#if UNITY_EDITOR
            UsefulLogger.LogWarning(
                $"開始シーン [{_startScene}] は Build Settings 未登録の為、Editor 専用ロードで開きます。" +
                "このシーンは SceneState には登録されません。", this);

            var operation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                _startScene, new LoadSceneParameters(LoadSceneMode.Additive));

            if (operation == null) return;

            await operation.ToUniTask();

            var loaded = SceneManager.GetSceneByPath(_startScene);
            if (loaded.IsValid())
            {
                SceneManager.SetActiveScene(loaded);
            }
#else
            UsefulLogger.LogError(
                $"開始シーン [{_startScene}] は Build Settings 未登録の為、ロードできません。", this);
#endif
        }
    }
}
