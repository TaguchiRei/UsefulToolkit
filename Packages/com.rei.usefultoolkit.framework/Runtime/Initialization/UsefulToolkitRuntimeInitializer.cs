using System.Collections.Generic;
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

        [SerializeField]
        [Tooltip("このInitializerが置かれているシーン以外にも常駐扱いにしたいシーンがあれば、そのビルドインデックスを指定する。")]
        private int[] _additionalPersistentSceneIndices;

        /// <summary>
        /// シーンシステムを初期化する。
        /// SceneLoaderが設定されていない場合は、エラーログを出してSceneStateの登録だけを行う。
        /// その場合、ロード/アンロードの要求はエラーログを出して失敗する。
        /// </summary>
        /// <param name="blackBoard">SceneStateの登録先</param>
        public override void Initialize(IBlackBoard blackBoard)
        {
            var sceneState = new SceneState(blackBoard, CollectPersistentSceneIds());
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

        /// <summary>
        /// 常駐シーンのビルドインデックスを集める。
        /// このInitializerが動いているシーン自身と、Inspectorで指定された追加分を含む。
        /// </summary>
        private List<int> CollectPersistentSceneIds()
        {
            var ids = new List<int>();

            var ownSceneIndex = gameObject.scene.buildIndex;
            if (ownSceneIndex >= 0)
            {
                ids.Add(ownSceneIndex);
            }
            else
            {
                UsefulLogger.LogWarning(
                    "常駐シーンがBuildSettingsに登録されていない為、このシーンの常駐保護ができません。", this);
            }

            if (_additionalPersistentSceneIndices != null)
            {
                foreach (var index in _additionalPersistentSceneIndices)
                {
                    if (index >= 0 && !ids.Contains(index))
                    {
                        ids.Add(index);
                    }
                }
            }

            return ids;
        }
    }
}
