using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using UsefulToolkit.Framework.BlackBoard;

namespace UsefulToolkit.Framework.EngineService
{
    /// <summary>
    /// シーンの読み込み・破棄を実行するサービス。生成するとSceneStateへ自身のロード処理を預け、
    /// 以降のシーン遷移を担当する。
    ///
    /// 遷移のたびに、現在の管理下のシーンと目標のシーン集合を比較して差分だけを処理する——
    /// 遷移元と共通のシーンはUnloadもLoadもされず状態が保たれる(ForceReloadで無効化できる)。
    /// 管理下に入るのはこのサービスがリクエストを受けて読み込んだシーンだけで、
    /// 常駐シーンや起動時に開かれているシーンを勝手にUnloadすることはない。
    ///
    /// 常駐シーンは最初のリクエストで一度だけ読み込み、以降はUnloadも読み直しもしない。
    /// </summary>
    public sealed class SceneLoadService : IDisposable
    {
        private readonly IBlackBoard _blackBoard;
        private readonly SceneBoard _sceneBoard;

        /// <summary> 管理下のシーン名。Unloadを読み込みの逆順で行えるよう、読み込み順で保持する </summary>
        private readonly List<string> _loadedSceneNames = new();

        private bool _persistentScenesReady;

        private ISceneLoaderRegister _sceneState;
        private IDisposable _stateSubscription;
        private IDisposable _loaderRegistration;

        /// <param name="blackBoard">シーンUnload時にOnSceneChangedを呼ぶために使う</param>
        /// <param name="sceneBoard">ロード処理を預ける先のSceneStateを取得するために使う</param>
        /// <exception cref="ArgumentNullException">blackBoardまたはsceneBoardがnullのときに出力</exception>
        public SceneLoadService(IBlackBoard blackBoard, SceneBoard sceneBoard)
        {
            _blackBoard = blackBoard ?? throw new ArgumentNullException(nameof(blackBoard));
            _sceneBoard = sceneBoard ?? throw new ArgumentNullException(nameof(sceneBoard));

            // SceneStateを生成するのはApplication側なので、登録を待ち受けて預ける。
            // これによりシーン管理クラスとの構築順が問われなくなる
            _stateSubscription = _sceneBoard.SubscribeStateRegister<ISceneLoaderRegister>(
                RegisterLoader, invokeIfRegistered: true);
        }

        public void Dispose()
        {
            _loaderRegistration?.Dispose();
            _loaderRegistration = null;

            _stateSubscription?.Dispose();
            _stateSubscription = null;

            _sceneState = null;
        }

        private void RegisterLoader()
        {
            if (_loaderRegistration != null) return;
            if (!_sceneBoard.TryGetGameState<ISceneLoaderRegister>(out var sceneState)) return;

            _sceneState = sceneState;
            _loaderRegistration = sceneState.RegisterSceneLoader(LoadScenesAsync);
        }

        private async UniTask LoadScenesAsync(
            IReadOnlyList<string> scenesToLoad, string activeScene, bool forceReload, IProgress<float> progress)
        {
            var persistentSceneNames = _sceneState.PersistentScenes;
            var persistentToLoad = CollectPersistentScenesToLoad(persistentSceneNames);
            var targetNames = BuildTargetNames(scenesToLoad, persistentSceneNames);

            // Unloadは読み込んだ逆順に行う。シーングループはMainを先頭に並べる規約なので、
            // 逆順にすることでMainが最後に落ちる
            var toUnload = new List<string>(_loadedSceneNames.Count);
            for (var i = _loadedSceneNames.Count - 1; i >= 0; i--)
            {
                var sceneName = _loadedSceneNames[i];
                if (forceReload || !targetNames.Contains(sceneName)) toUnload.Add(sceneName);
            }

            var toLoad = new List<string>(targetNames.Count);

            foreach (var sceneName in targetNames)
            {
                var isManaged = _loadedSceneNames.Contains(sceneName);

                // 管理下にあってUnload対象でもない = 読み込まれたまま残るので何もしない
                if (isManaged && !toUnload.Contains(sceneName)) continue;

                // 管理外なのにすでに開かれているシーン(Bootなど)。Additiveで読むと二重に開かれて
                // しまうため、実ロードはせず管理下に置くだけにする
                if (!isManaged && SceneManager.GetSceneByName(sceneName).isLoaded)
                {
                    _loadedSceneNames.Add(sceneName);
                    continue;
                }

                toLoad.Add(sceneName);
            }

            var totalSteps = persistentToLoad.Count + toUnload.Count + toLoad.Count;

            if (totalSteps == 0)
            {
                // 読み込むものが無い = 常駐シーンも揃っている
                _persistentScenesReady = true;

                NormalizeLoadedOrder(targetNames);
                ApplyActiveScene(activeScene);
                progress?.Report(1f);
                return;
            }

            var completedSteps = 0;

            // 常駐シーンには常駐システムが載っているので、何より先に読み込む
            foreach (var sceneName in persistentToLoad)
            {
                await LoadSceneAsync(sceneName);

                completedSteps++;
                progress?.Report((float)completedSteps / totalSteps);
            }

            _persistentScenesReady = true;

            foreach (var sceneName in toUnload)
            {
                var operation = SceneManager.UnloadSceneAsync(sceneName);
                if (operation != null) await operation.ToUniTask();

                _loadedSceneNames.Remove(sceneName);

                // このシーンのスコープで登録されたState/イベントチャンネルを一括解除する
                _blackBoard.OnSceneChanged(sceneName);

                completedSteps++;
                progress?.Report((float)completedSteps / totalSteps);
            }

            foreach (var sceneName in toLoad)
            {
                await LoadSceneAsync(sceneName);

                _loadedSceneNames.Add(sceneName);
                completedSteps++;
                progress?.Report((float)completedSteps / totalSteps);
            }

            NormalizeLoadedOrder(targetNames);
            ApplyActiveScene(activeScene);
        }

        private static async UniTask LoadSceneAsync(string sceneName)
        {
            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            if (operation is null)
            {
                throw new InvalidOperationException(
                    $"シーン [{sceneName}] を読み込めません。Build Settingsに登録されているか確認してください。");
            }

            await operation.ToUniTask();
        }

        /// <summary>
        /// 管理下のシーンの並びを目標シーンの順に揃え直す。
        /// 途中で採用したシーンが末尾に寄ってしまうと、次回のUnloadの逆順が崩れるため。
        /// </summary>
        private void NormalizeLoadedOrder(List<string> targetNames)
        {
            _loadedSceneNames.Clear();
            _loadedSceneNames.AddRange(targetNames);
        }

        /// <summary> 指定シーンが読み込まれていればアクティブシーンにする </summary>
        private void ApplyActiveScene(string activeSceneName)
        {
            if (string.IsNullOrEmpty(activeSceneName)) return;

            var scene = SceneManager.GetSceneByName(activeSceneName);

            if (!scene.isLoaded)
            {
                UsefulLogger.LogWarning($"アクティブシーンにする [{activeSceneName}] が読み込まれていません。", this);
                return;
            }

            SceneManager.SetActiveScene(scene);
        }

        /// <summary>
        /// 常駐シーンのうち、これから読み込む必要があるものを集める。
        /// 2回目以降のリクエストと、すでに開かれているシーンは対象外。
        /// </summary>
        private List<string> CollectPersistentScenesToLoad(IReadOnlyList<string> persistentSceneNames)
        {
            var names = new List<string>();

            if (_persistentScenesReady) return names;

            foreach (var sceneName in persistentSceneNames)
            {
                if (names.Contains(sceneName)) continue;
                if (SceneManager.GetSceneByName(sceneName).isLoaded) continue;

                names.Add(sceneName);
            }

            return names;
        }

        /// <summary>
        /// 目標シーン名の一覧から、重複と常駐シーンを取り除いて複製する。
        /// シーングループに常駐シーンが書かれていても管理下には入れない。
        /// </summary>
        private static List<string> BuildTargetNames(
            IReadOnlyList<string> scenesToLoad, IReadOnlyList<string> persistentSceneNames)
        {
            var names = new List<string>(scenesToLoad.Count);

            foreach (var sceneName in scenesToLoad)
            {
                if (names.Contains(sceneName)) continue;
                if (persistentSceneNames.Contains(sceneName)) continue;

                names.Add(sceneName);
            }

            return names;
        }
    }
}
