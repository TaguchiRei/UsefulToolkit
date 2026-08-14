using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using UsefulToolkit.Framework.BlackBoard;

namespace UsefulToolkit.Framework.EngineService
{
    /// <summary>
    /// SceneManagerを直接扱う唯一のクラス。SceneBoardへ自身のロードメソッドを登録し、
    /// Application側からのリクエストに応じて、現在読み込み済みのシーン(このサービス自身が
    /// 読み込んだものに限る)と目標シーン集合を差分比較して、不要な分だけUnload、
    /// 不足分だけAdditiveでLoadする。
    ///
    /// LoadSceneMode.Singleは使わない——System/Bootのような、このサービスが管理していない
    /// 常駐シーンまで巻き込んで消してしまうため。
    ///
    /// Inspectorのフィールドも毎フレームの更新も必要ないため、MonoBehaviourではなく素のクラス。
    /// </summary>
    public sealed class SceneLoadService : IDisposable
    {
        private readonly IBlackBoard _blackBoard;
        private readonly HashSet<string> _loadedSceneNames = new();

        private IDisposable _registration;

        /// <exception cref="ArgumentNullException">blackBoardがnullのときに出力</exception>
        public SceneLoadService(IBlackBoard blackBoard)
        {
            _blackBoard = blackBoard ?? throw new ArgumentNullException(nameof(blackBoard));
            _registration = _blackBoard.SceneBoard.RegisterSceneLoader(LoadScenesAsync);
        }

        public void Dispose()
        {
            _registration?.Dispose();
            _registration = null;
        }

        private async UniTask LoadScenesAsync(IReadOnlyList<string> scenesToLoad, IProgress<float> progress)
        {
            var targetNames = new HashSet<string>(scenesToLoad);

            var toUnload = _loadedSceneNames.Where(name => !targetNames.Contains(name)).ToArray();
            var toLoad = targetNames.Where(name => !_loadedSceneNames.Contains(name)).ToArray();

            var totalSteps = toUnload.Length + toLoad.Length;

            if (totalSteps == 0)
            {
                progress?.Report(1f);
                return;
            }

            var completedSteps = 0;

            foreach (var sceneName in toUnload)
            {
                var operation = SceneManager.UnloadSceneAsync(sceneName);
                if (operation != null) await operation.ToUniTask();

                _loadedSceneNames.Remove(sceneName);

                // このシーンのスコープで登録されたState/イベントチャンネルを一括解除する。
                // シーン管理システムがOnSceneChangedを呼ぶ唯一の場所。
                _blackBoard.OnSceneChanged(sceneName);

                completedSteps++;
                progress?.Report((float)completedSteps / totalSteps);
            }

            foreach (var sceneName in toLoad)
            {
                var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

                if (operation is null)
                {
                    throw new InvalidOperationException(
                        $"シーン [{sceneName}] を読み込めません。Build Settingsに登録されているか確認してください。");
                }

                await operation.ToUniTask();

                _loadedSceneNames.Add(sceneName);
                completedSteps++;
                progress?.Report((float)completedSteps / totalSteps);
            }
        }
    }
}
