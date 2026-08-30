using System;
using System.Collections.Generic;
using UsefulToolkit.BlackBoard.Logger;

namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary>
    /// ロード済みのシーンIDを保持する。
    /// ロード/アンロードを反映できるかを判定し、できる場合だけ集合を更新して結果を返す。
    /// 反映できない場合は警告ログを出してfalseを返す。Actionの実行は行わない。
    ///
    /// 常駐シーンはこの集合とは別に保持し、アクティブシーンにはできず、アンロードや降格の対象にもならない。
    /// 常に「ロード済み」として扱う。
    /// </summary>
    internal sealed class LoadedSceneSet
    {
        private const int NoSceneId = SceneState.NoSceneId;

        /// <summary> 現在のアクティブシーン。未設定なら<see cref="SceneState.NoSceneId"/> </summary>
        public int ActiveScene { get; private set; } = NoSceneId;

        /// <summary> アクティブシーン以外にロードされているシーン(常駐シーンは含まない) </summary>
        public IReadOnlyList<int> AdditiveScenes => _additiveScenes;

        private readonly List<int> _additiveScenes = new();

        /// <summary> 常駐シーン。アクティブ化・アンロード・降格のいずれもされない </summary>
        private readonly HashSet<int> _persistentScenes = new();

        /// <param name="persistentSceneIds">常駐シーンのビルドインデックス。nullや負値は無視する</param>
        public LoadedSceneSet(IReadOnlyList<int> persistentSceneIds = null)
        {
            if (persistentSceneIds == null)
            {
                return;
            }

            for (int i = 0; i < persistentSceneIds.Count; i++)
            {
                var sceneId = persistentSceneIds[i];
                if (sceneId >= 0)
                {
                    _persistentScenes.Add(sceneId);
                }
            }
        }

        /// <summary>
        /// 指定したシーンが常駐シーンか。
        /// </summary>
        /// <param name="sceneId">確認するシーンID</param>
        public bool IsPersistent(int sceneId)
        {
            return _persistentScenes.Contains(sceneId);
        }

        /// <summary>
        /// 指定したシーンをアクティブシーンとしてロード/昇格できるか。
        /// 負値と常駐シーンは不可。ロード要求を実際に処理する前のチェックに使う。
        /// </summary>
        /// <param name="sceneId">確認するシーンID</param>
        public bool CanBeActiveScene(int sceneId)
        {
            return sceneId >= 0 && !_persistentScenes.Contains(sceneId);
        }

        /// <summary>
        /// 指定したシーンがアクティブ/アディティブ/常駐のいずれかでロード済みか。
        /// </summary>
        /// <param name="sceneId">確認するシーンID</param>
        public bool IsLoaded(int sceneId)
        {
            return ActiveScene == sceneId
                   || _additiveScenes.Contains(sceneId)
                   || _persistentScenes.Contains(sceneId);
        }

        /// <summary>
        /// アクティブシーンのロードを反映する。
        /// それまでのアクティブシーンはアンロードされず、アディティブシーンへ降格する。
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

            if (_persistentScenes.Contains(sceneId))
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}は常駐シーンの為、アクティブシーンにできません", this);
                return false;
            }

            if (_additiveScenes.Contains(sceneId))
            {
                UsefulLogger.LogWarning(
                    $"シーンID{sceneId}はアディティブシーンとしてロード済みです。アクティブシーンへの変更はTryChangeActiveSceneを使ってください", this);
                return false;
            }

            // 旧アクティブシーンはアンロードされる訳ではないのでアディティブシーンとして残す
            if (ActiveScene != NoSceneId)
            {
                _additiveScenes.Add(ActiveScene);
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
            if (_persistentScenes.Contains(sceneId))
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}は常駐シーンの為、アディティブシーンとして読み込む必要はありません。", this);
                return false;
            }

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
        /// アディティブシーンのアンロードを反映する。常駐シーンは対象にできない。
        /// </summary>
        /// <param name="sceneId">アンロードするシーンID</param>
        /// <returns>反映されたか</returns>
        public bool TryUnLoadAdditiveScene(int sceneId)
        {
            if (_persistentScenes.Contains(sceneId))
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}は常駐シーンの為、アンロードできません。", this);
                return false;
            }

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
        /// 上書きロードのために、指定したシーンを集合から取り除く。
        /// アクティブシーンが対象に含まれる場合はアクティブシーンを未設定へ戻す。
        /// 常駐シーンと、ロードされていないシーンは黙って対象から外す。
        /// </summary>
        /// <param name="sceneIds">取り除くシーンID</param>
        /// <param name="removedScenes">実際に取り除かれたシーンIDの追加先</param>
        public void RemoveScenes(ReadOnlySpan<int> sceneIds, List<int> removedScenes)
        {
            foreach (var sceneId in sceneIds)
            {
                if (_persistentScenes.Contains(sceneId))
                {
                    continue;
                }

                if (ActiveScene == sceneId)
                {
                    ActiveScene = NoSceneId;
                    removedScenes.Add(sceneId);
                    continue;
                }

                if (_additiveScenes.Remove(sceneId))
                {
                    removedScenes.Add(sceneId);
                }
            }
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

            if (_persistentScenes.Contains(newActiveSceneId))
            {
                UsefulLogger.LogWarning($"シーンID{newActiveSceneId}は常駐シーンの為、アクティブシーンへ変更できません。", this);
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

        /// <summary>
        /// 現在ロード中の管理シーン(アクティブシーン + アディティブシーン、常駐シーンは除く)を複製する。
        /// </summary>
        /// <param name="buffer">複製先</param>
        public void CopyLoadedScenesTo(List<int> buffer)
        {
            if (ActiveScene != NoSceneId)
            {
                buffer.Add(ActiveScene);
            }

            buffer.AddRange(_additiveScenes);
        }
    }
}
