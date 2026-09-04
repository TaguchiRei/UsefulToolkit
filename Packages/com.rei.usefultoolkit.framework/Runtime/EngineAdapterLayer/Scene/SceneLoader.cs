using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UsefulToolkit.Initialization;
using UsefulToolkit.BlackBoard.Logger;

namespace UsefulToolkit.EngineAdapter
{
    /// <summary>
    /// SceneManagerを使って、渡されたシーンIDのロード/アンロードを行う。
    /// 処理中は進捗を報告し、完了すると成否を返す。
    /// どのシーンを対象にするかは呼び出し側が決める。
    /// </summary>
    public class SceneLoader : InitializableMonoBehaviour
    {
        /// <summary>
        /// 指定したシーンをアディティブでロードし、最後にアクティブシーンを設定する。
        /// 一つでもロードに失敗した場合は、その時点で中断してfalseを返す。
        /// </summary>
        /// <param name="sceneIds">ロードするシーンID</param>
        /// <param name="activeSceneId">アクティブシーンにするシーンID。負数ならアクティブシーンを変更しない</param>
        /// <param name="progress">ロード進捗の報告先</param>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        /// <returns>要求した全てのシーンをロードできたか</returns>
        public async UniTask<bool> LoadScenesAsync(IReadOnlyList<int> sceneIds, int activeSceneId,
            IProgress<float> progress, CancellationToken cancellationToken)
        {
            var sceneCount = sceneIds?.Count ?? 0;

            for (int i = 0; i < sceneCount; i++)
            {
                var operation = SceneManager.LoadSceneAsync(sceneIds[i], LoadSceneMode.Additive);
                if (operation == null)
                {
                    UsefulLogger.LogError($"シーンID{sceneIds[i]}はビルド設定に含まれていない可能性があります。", this);
                    return false;
                }

                await AwaitOperationAsync(operation, i, sceneCount, progress, cancellationToken);
            }

            progress?.Report(1f);

            if (activeSceneId >= 0)
            {
                SetActiveScene(activeSceneId);
            }

            return true;
        }

        /// <summary>
        /// 指定したシーンをアンロードする。
        /// 一つでもアンロードに失敗した場合は、その時点で中断してfalseを返す。
        /// </summary>
        /// <param name="sceneIds">アンロードするシーンID</param>
        /// <param name="progress">アンロード進捗の報告先</param>
        /// <param name="cancellationToken">アンロードの中断に使う</param>
        /// <returns>要求した全てのシーンをアンロードできたか</returns>
        public async UniTask<bool> UnLoadScenesAsync(IReadOnlyList<int> sceneIds, IProgress<float> progress,
            CancellationToken cancellationToken)
        {
            var sceneCount = sceneIds?.Count ?? 0;

            for (int i = 0; i < sceneCount; i++)
            {
                var operation = SceneManager.UnloadSceneAsync(sceneIds[i]);
                if (operation == null)
                {
                    // ロードされていない、または最後の一枚を消そうとした場合はnullが返る
                    UsefulLogger.LogError($"シーンID{sceneIds[i]}をアンロードできませんでした。", this);
                    return false;
                }

                await AwaitOperationAsync(operation, i, sceneCount, progress, cancellationToken);
            }

            progress?.Report(1f);
            return true;
        }

        /// <summary>
        /// AsyncOperationの完了を待ちながら、全体の進捗を報告する。
        /// </summary>
        /// <param name="operation">待機する処理</param>
        /// <param name="completedCount">この処理より前に完了した数</param>
        /// <param name="totalCount">今回処理する総数</param>
        /// <param name="progress">進捗の報告先</param>
        /// <param name="cancellationToken">中断に使う</param>
        private static async UniTask AwaitOperationAsync(AsyncOperation operation, int completedCount, int totalCount,
            IProgress<float> progress, CancellationToken cancellationToken)
        {
            while (!operation.isDone)
            {
                // SceneManagerのAsyncOperationは完了直前まで0.9で頭打ちになるため、進捗はあくまで目安
                progress?.Report((completedCount + operation.progress) / totalCount);
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        /// <summary>
        /// 指定したシーンをアクティブシーンにする。
        /// そのシーンがロードされていない場合は、警告ログを出して何もしない。
        /// </summary>
        /// <param name="sceneId">アクティブにするシーンID</param>
        private void SetActiveScene(int sceneId)
        {
            var scene = SceneManager.GetSceneByBuildIndex(sceneId);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}がロードされていない為、アクティブシーンにできません。", this);
                return;
            }

            SceneManager.SetActiveScene(scene);
        }
    }
}
