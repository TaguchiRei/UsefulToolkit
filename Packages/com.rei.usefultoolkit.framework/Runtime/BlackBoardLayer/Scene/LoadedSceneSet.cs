using System;
using System.Collections.Generic;
using UsefulToolkit.BlackBoard.Logger;

namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary>
    /// ロード済みのシーンIDを保持する。
    /// ロード/アンロードを反映できるかを判定し、できる場合だけ集合を更新して結果を返す。
    /// 反映できない場合は警告ログを出してfalseを返す。Actionの実行は行わない。
    /// </summary>
    internal sealed class LoadedSceneSet
    {
        private const int NoSceneId = SceneState.NoSceneId;

        /// <summary> 現在のアクティブシーン。未設定なら<see cref="SceneState.NoSceneId"/> </summary>
        public int ActiveScene { get; private set; } = NoSceneId;

        /// <summary> アクティブシーン以外にロードされているシーン </summary>
        public IReadOnlyList<int> AdditiveScenes => _additiveScenes;

        private readonly List<int> _additiveScenes = new();

        /// <summary>
        /// 指定したシーンがアクティブ/アディティブのいずれかでロード済みか。
        /// </summary>
        /// <param name="sceneId">確認するシーンID</param>
        public bool IsLoaded(int sceneId)
        {
            return ActiveScene == sceneId || _additiveScenes.Contains(sceneId);
        }

        /// <summary>
        /// アクティブシーンのロードを反映する。
        /// </summary>
        /// <param name="sceneId">ロードするシーンID</param>
        /// <param name="previousActiveScene">反映前のアクティブシーン。未設定だった場合は<see cref="SceneState.NoSceneId"/></param>
        /// <returns>反映されたか</returns>
        public bool TryLoadActiveScene(int sceneId, out int previousActiveScene)
        {
            previousActiveScene = ActiveScene;

            if (ActiveScene == sceneId)
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はアクティブシーンとしてロード済みです", this);
                return false;
            }

            if (_additiveScenes.Contains(sceneId))
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はアディティブシーンとしてロード済みの為、アクティブシーンとしてロードできません", this);
                return false;
            }

            ActiveScene = sceneId;
            return true;
        }

        /// <summary>
        /// アディティブシーンのロードを反映する。
        /// </summary>
        /// <param name="sceneId">ロードするシーンID</param>
        /// <returns>反映されたか</returns>
        public bool TryLoadAdditiveScene(int sceneId)
        {
            if (_additiveScenes.Contains(sceneId))
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はアディティブシーンとしてロード済みです。", this);
                return false;
            }

            if (ActiveScene == sceneId)
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はアクティブシーンとしてロード済みの為、アディティブシーンとして読み込めません。", this);
                return false;
            }

            _additiveScenes.Add(sceneId);
            return true;
        }

        /// <summary>
        /// 複数のアディティブシーンのロードを反映する。
        /// </summary>
        /// <param name="additiveScenes">ロードするシーンID</param>
        /// <param name="loadedScenes">実際に反映されたシーンIDの追加先</param>
        public void LoadAdditiveScenes(ReadOnlySpan<int> additiveScenes, List<int> loadedScenes)
        {
            foreach (var additiveScene in additiveScenes)
            {
                if (TryLoadAdditiveScene(additiveScene))
                {
                    loadedScenes.Add(additiveScene);
                }
            }
        }

        /// <summary>
        /// アディティブシーンのアンロードを反映する。
        /// </summary>
        /// <param name="sceneId">アンロードするシーンID</param>
        /// <returns>反映されたか</returns>
        public bool TryUnLoadAdditiveScene(int sceneId)
        {
            if (_additiveScenes.Remove(sceneId))
            {
                return true;
            }

            if (ActiveScene == sceneId)
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はアクティブシーンの為、アディティブシーンとしてアンロードできません。", this);
            }
            else
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はアディティブシーンとしてロードされていません。", this);
            }

            return false;
        }

        /// <summary>
        /// ロード済みのアディティブシーンをアクティブシーンへ昇格させ、それまでのアクティブシーンをアディティブシーンへ降格させる。
        /// </summary>
        /// <param name="newActiveSceneId">アクティブシーンへ昇格させるシーンID</param>
        /// <returns>反映されたか</returns>
        public bool TryChangeActiveScene(int newActiveSceneId)
        {
            if (ActiveScene == newActiveSceneId)
            {
                UsefulLogger.LogWarning($"シーンID{newActiveSceneId}は既にアクティブシーンです。", this);
                return false;
            }

            if (!_additiveScenes.Remove(newActiveSceneId))
            {
                UsefulLogger.LogWarning($"シーンID{newActiveSceneId}はアディティブシーンとしてロードされていない為、アクティブシーンへ変更できません。", this);
                return false;
            }

            // 旧アクティブシーンはアンロードされる訳ではないのでアディティブシーンとして残す
            if (ActiveScene != NoSceneId)
            {
                _additiveScenes.Add(ActiveScene);
            }

            ActiveScene = newActiveSceneId;
            return true;
        }

        /// <summary>
        /// 現在のアディティブシーンを配列へ複製する。
        /// </summary>
        /// <param name="buffer">複製先。アディティブシーン数以上の長さが必要</param>
        /// <returns>複製した数</returns>
        public int CopyAdditiveScenesTo(int[] buffer)
        {
            _additiveScenes.CopyTo(buffer, 0);
            return _additiveScenes.Count;
        }
    }
}
