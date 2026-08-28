using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UsefulToolkit.BlackBoard.Logger;

namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary>
    /// シーンのロード/アンロード要求を受け付ける。
    /// 要求されたシーンのうち実際に操作が必要なものを算出し、登録された処理へ渡して実行させ、
    /// 完了後の状態をSceneStateへ反映する。
    /// </summary>
    internal sealed class SceneLoadRequester
    {
        private readonly SceneState _sceneState;

        private SceneLoadFunc _loadFunc;
        private SceneUnLoadFunc _unLoadFunc;

        public SceneLoadRequester(SceneState sceneState)
        {
            _sceneState = sceneState;
        }

        /// <summary>
        /// 実際にシーンを操作する処理を登録する。登録できるのは一度だけ。
        /// </summary>
        /// <param name="loadFunc">ロードを実行する処理</param>
        /// <param name="unLoadFunc">アンロードを実行する処理</param>
        /// <exception cref="ArgumentNullException">処理が指定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">既に登録済みのときに出力</exception>
        public void RegisterSceneLoader(SceneLoadFunc loadFunc, SceneUnLoadFunc unLoadFunc)
        {
            if (loadFunc == null)
            {
                throw new ArgumentNullException(nameof(loadFunc));
            }

            if (unLoadFunc == null)
            {
                throw new ArgumentNullException(nameof(unLoadFunc));
            }

            if (_loadFunc != null || _unLoadFunc != null)
            {
                throw new InvalidOperationException("シーンローダーはすでに登録されています。");
            }

            _loadFunc = loadFunc;
            _unLoadFunc = unLoadFunc;
        }

        /// <summary>
        /// シーンのロードを要求する。
        /// ロード済みのシーンは読み直さず、アクティブシーンの切り替えだけが必要な場合もそのまま実行する。
        /// ローダーが未登録の場合と、他のロード/アンロードが進行中の場合はfalseを返す。
        /// </summary>
        /// <param name="mainSceneId">アクティブシーンにするシーンID</param>
        /// <param name="subSceneIds">共にロードするシーンID</param>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        /// <returns>要求した全てのシーンがロードされStateへ反映されたか</returns>
        public async UniTask<bool> RequestLoadAsync(int mainSceneId, IReadOnlyList<int> subSceneIds,
            CancellationToken cancellationToken)
        {
            if (_loadFunc == null)
            {
                UsefulLogger.LogError("シーンローダーが登録されていない為、ロードを要求できません。", this);
                return false;
            }

            if (!_sceneState.TryBeginLoad())
            {
                return false;
            }

            try
            {
                var targets = CollectLoadTargets(mainSceneId, subSceneIds, out var loadMainScene);

                // ロード対象が無い場合も、アクティブシーンの切り替えのために呼ぶ
                if (!await _loadFunc(targets, mainSceneId, _sceneState, cancellationToken))
                {
                    return false;
                }

                return ApplyLoaded(mainSceneId, targets, loadMainScene);
            }
            finally
            {
                _sceneState.EndPhase();
            }
        }

        /// <summary>
        /// シーンのアンロードを要求する。
        /// ロードされていないシーンとアクティブシーンは対象から外れ、対象が無い場合はtrueを返す。
        /// ローダーが未登録の場合と、他のロード/アンロードが進行中の場合はfalseを返す。
        /// </summary>
        /// <param name="sceneIds">アンロードするシーンID</param>
        /// <param name="cancellationToken">アンロードの中断に使う</param>
        /// <returns>対象の全てのシーンがアンロードされStateへ反映されたか</returns>
        public async UniTask<bool> RequestUnLoadAsync(IReadOnlyList<int> sceneIds,
            CancellationToken cancellationToken)
        {
            if (_unLoadFunc == null)
            {
                UsefulLogger.LogError("シーンローダーが登録されていない為、アンロードを要求できません。", this);
                return false;
            }

            var targets = CollectUnLoadTargets(sceneIds);
            if (targets.Length == 0)
            {
                return true;
            }

            if (!_sceneState.TryBeginUnLoad())
            {
                return false;
            }

            try
            {
                if (!await _unLoadFunc(targets, _sceneState, cancellationToken))
                {
                    return false;
                }

                return _sceneState.UnLoadAdditiveScenes(targets);
            }
            finally
            {
                _sceneState.EndPhase();
            }
        }

        /// <summary>
        /// 実際にロードが必要なシーンを算出する。
        /// ロード済みのシーンと重複するシーンは除かれ、新規にロードするアクティブシーンは先頭へ入る。
        /// </summary>
        /// <param name="mainSceneId">アクティブシーンにするシーンID</param>
        /// <param name="subSceneIds">共にロードするシーンID</param>
        /// <param name="loadMainScene">アクティブシーンにするシーンを新規にロードするか</param>
        private int[] CollectLoadTargets(int mainSceneId, IReadOnlyList<int> subSceneIds, out bool loadMainScene)
        {
            loadMainScene = mainSceneId != SceneState.NoSceneId && !_sceneState.IsLoaded(mainSceneId);

            var targets = new List<int>((subSceneIds?.Count ?? 0) + 1);
            if (loadMainScene)
            {
                targets.Add(mainSceneId);
            }

            if (subSceneIds == null)
            {
                return targets.ToArray();
            }

            for (int i = 0; i < subSceneIds.Count; i++)
            {
                var sceneId = subSceneIds[i];
                if (sceneId == mainSceneId || _sceneState.IsLoaded(sceneId) || targets.Contains(sceneId))
                {
                    continue;
                }

                targets.Add(sceneId);
            }

            return targets.ToArray();
        }

        /// <summary>
        /// 実際にアンロードできるシーンを算出する。
        /// アクティブシーンと未ロードのシーンは警告ログを出して除外する。
        /// </summary>
        /// <param name="sceneIds">アンロードを要求されたシーンID</param>
        private int[] CollectUnLoadTargets(IReadOnlyList<int> sceneIds)
        {
            if (sceneIds == null || sceneIds.Count == 0)
            {
                return Array.Empty<int>();
            }

            var targets = new List<int>(sceneIds.Count);
            for (int i = 0; i < sceneIds.Count; i++)
            {
                var sceneId = sceneIds[i];
                if (targets.Contains(sceneId))
                {
                    continue;
                }

                if (sceneId == _sceneState.ActiveScene)
                {
                    UsefulLogger.LogWarning($"シーンID{sceneId}はアクティブシーンの為、アンロードできません。", this);
                    continue;
                }

                if (!_sceneState.IsLoaded(sceneId))
                {
                    UsefulLogger.LogWarning($"シーンID{sceneId}はロードされていない為、アンロードできません。", this);
                    continue;
                }

                targets.Add(sceneId);
            }

            return targets.ToArray();
        }

        /// <summary>
        /// ロードした結果をStateへ反映する。
        /// アクティブシーンにするシーンが元からロード済みだった場合は、ロードではなく昇格として反映する。
        /// </summary>
        /// <param name="mainSceneId">アクティブシーンにするシーンID</param>
        /// <param name="loadedSceneIds">今回ロードしたシーンID。loadedMainSceneがtrueなら先頭がアクティブシーン</param>
        /// <param name="loadedMainScene">アクティブシーンにするシーンを新規にロードしたか</param>
        /// <returns>全てStateへ反映されたか</returns>
        private bool ApplyLoaded(int mainSceneId, int[] loadedSceneIds, bool loadedMainScene)
        {
            if (loadedMainScene)
            {
                return _sceneState.LoadMultiScene(mainSceneId, loadedSceneIds.AsSpan(1));
            }

            var applied = _sceneState.LoadAdditiveScenes(loadedSceneIds);

            if (mainSceneId != SceneState.NoSceneId && _sceneState.ActiveScene != mainSceneId)
            {
                applied &= _sceneState.ChangeActiveScene(mainSceneId);
            }

            return applied;
        }
    }
}
