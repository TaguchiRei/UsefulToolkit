using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using UsefulToolkit.Framework.BlackBoard;

namespace UsefulToolkit.Framework.EngineService
{
    /// <summary>
    /// SceneManagerを直接扱う唯一のクラス。SceneBoardへ自身のロードメソッドを登録し、
    /// Application側からのリクエストに応じて、現在読み込み済みのシーン(このサービス自身が
    /// 管理下に置いたものに限る)と目標シーン集合を差分比較して、不要な分だけUnload、
    /// 不足分だけAdditiveでLoadする。
    /// forceReloadが指定された場合は差分比較を行わず、管理下のシーンをすべて読み直す。
    ///
    /// LoadSceneMode.Singleは使わない——System/Bootのような、このサービスが管理していない
    /// 常駐シーンまで巻き込んで消してしまうため。同じ理由で、起動時に開かれているシーンを
    /// 一括で管理下に取り込むこともしない。管理下に入るのは、シーングループに書かれて
    /// 実際にリクエストされたシーンだけ。
    ///
    /// Inspectorのフィールドも毎フレームの更新も必要ないため、MonoBehaviourではなく素のクラス。
    /// </summary>
    public sealed class SceneLoadService : IDisposable
    {
        private readonly IBlackBoard _blackBoard;

        /// <summary> 管理下のシーン名。Unloadを読み込みの逆順で行えるよう、読み込み順で保持する </summary>
        private readonly List<string> _loadedSceneNames = new();

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

        private async UniTask LoadScenesAsync(
            IReadOnlyList<string> scenesToLoad, bool forceReload, IProgress<float> progress)
        {
            var targetNames = BuildTargetNames(scenesToLoad);

            // Unloadは読み込んだ逆順に行う。シーングループはLightingを先頭に並べる規約なので、
            // 逆順にすることでLightingが最後に落ちる。
            var toUnload = new List<string>(_loadedSceneNames.Count);
            for (var i = _loadedSceneNames.Count - 1; i >= 0; i--)
            {
                var sceneName = _loadedSceneNames[i];
                if (forceReload || !targetNames.Contains(sceneName)) toUnload.Add(sceneName);
            }

            // ロードはtargetNamesの順を保つ。Lighting→Content→Logic→Additionalという
            // シーングループ側の並びが、そのまま読み込み順になる。
            var toLoad = new List<string>(targetNames.Count);
            var toAdopt = new List<string>();

            foreach (var sceneName in targetNames)
            {
                var isManaged = _loadedSceneNames.Contains(sceneName);

                // 管理下にあってUnload対象でもない = 読み込まれたまま残るので何もしない
                if (isManaged && !toUnload.Contains(sceneName)) continue;

                // 管理外なのにすでに開かれているシーン(Bootなど)。Additiveで読むと同名シーンが
                // 二重に開かれてしまうため、実ロードはせず記録だけして管理下に置く。
                // forceReloadでもここは読み直さない——自分が読み込んでいないシーンを
                // 落としてよいとは限らないため。次回以降は管理下なので通常どおり読み直される。
                if (!isManaged && SceneManager.GetSceneByName(sceneName).isLoaded)
                {
                    toAdopt.Add(sceneName);
                    continue;
                }

                toLoad.Add(sceneName);
            }

            _loadedSceneNames.AddRange(toAdopt);

            var totalSteps = toUnload.Count + toLoad.Count;

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

        /// <summary>
        /// 目標シーン名の一覧を、並び順を保ったまま重複だけ取り除いて複製する。
        /// SceneGroupは生成時に重複を除いているが、SceneBoard経由で任意のリストが
        /// 渡ってくる可能性があるためここでも確認する。
        /// </summary>
        private static List<string> BuildTargetNames(IReadOnlyList<string> scenesToLoad)
        {
            var names = new List<string>(scenesToLoad.Count);

            foreach (var sceneName in scenesToLoad)
            {
                if (!names.Contains(sceneName)) names.Add(sceneName);
            }

            return names;
        }
    }
}
