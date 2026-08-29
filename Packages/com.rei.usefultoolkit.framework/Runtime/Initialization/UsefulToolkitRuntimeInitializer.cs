using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.BlackBoard.Scene;
using UsefulToolkit.EngineService;

namespace UsefulToolkit.Initialization
{
    /// <summary>
    /// Toolkitのランタイム機能を初期化するInitializer。
    /// SceneStateを生成してSceneBoardへ登録し、SceneLoaderをシーンの操作役として繋ぐ。
    /// GameCompositerがBlackBoardを構築した直後、他のどのInitializerよりも先に実行する。
    /// </summary>
    public sealed class UsefulToolkitRuntimeInitializer : InitializerBase
    {
        [SerializeField] private SceneLoader _sceneLoader;

        /// <summary>
        /// シーンシステムを初期化する。
        /// SceneLoaderが設定されていない場合は、エラーログを出してSceneStateの登録だけを行う。
        /// その場合、ロード/アンロードの要求はエラーログを出して失敗する。
        /// </summary>
        /// <param name="blackBoard">SceneStateの登録先</param>
        public override void Initialize(IBlackBoard blackBoard)
        {
            var sceneState = new SceneState(blackBoard);
            blackBoard.GetSceneBoard().RegisterGameState<ISceneState>(sceneState);

            if (_sceneLoader == null)
            {
                UsefulLogger.LogError("SceneLoaderが設定されていない為、シーンの操作を行えません。", this);
                base.Initialize(blackBoard);
                return;
            }

            _sceneLoader.Initialize();
            sceneState.RegisterSceneLoader(_sceneLoader.LoadScenesAsync, _sceneLoader.UnLoadScenesAsync);

            base.Initialize(blackBoard);
        }
    }
}
