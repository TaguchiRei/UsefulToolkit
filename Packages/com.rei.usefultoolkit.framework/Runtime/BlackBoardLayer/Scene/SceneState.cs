using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UnityEngine.Pool;
using UsefulToolkit.BlackBoard.Logger;

namespace UsefulToolkit.BlackBoard.Scene
{
    [RegisterBoard(typeof(SceneBoard))]
    public class SceneState : GameStateBase, ISceneState, IProgress<float>
    {
        /// <summary> アクティブシーンが未設定であることを表すシーンID </summary>
        public const int NoSceneId = -1;

        public float LoadProgress { get; private set; } = 1;

        /// <summary> ロード/アンロードの進行状況。Noneの間だけ次の開始を受け付ける </summary>
        public SceneLoadPhase Phase { get; private set; } = SceneLoadPhase.None;

        public bool IsLoading => Phase == SceneLoadPhase.Loading;
        public int ActiveScene { get; private set; } = NoSceneId;
        public IReadOnlyList<int> AdditiveScenes => _additiveScenes;
        private readonly List<int> _additiveScenes = new();

        private readonly Dictionary<int, List<ActionEntry>> _loadedActions = new();
        private readonly Dictionary<int, List<ActionEntry>> _unLoadedActions = new();
        private readonly List<ActionEntry> _anySceneLoadedActions = new();
        private readonly List<ActionEntry> _activeSceneChangedActions = new();

        #region ISceneState実装

        public bool IsLoaded(int sceneId)
        {
            if (_additiveScenes.Contains(sceneId) || ActiveScene == sceneId)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 指定したシーンがロードされた時に実行するアクションを登録する。
        /// </summary>
        /// <param name="sceneId">対象のシーンID</param>
        /// <param name="loadedAction">登録するアクション</param>
        /// <param name="invokeOnAlreadyLoaded">trueなら、既にロード済みの場合はその場で実行する</param>
        /// <exception cref="ArgumentNullException">loadedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable RegisterEventOnLoad(int sceneId, ActionEntry loadedAction,
            bool invokeOnAlreadyLoaded = false)
        {
            _loadedActions.TryGetValue(sceneId, out var registeredActions);
            ThrowIfCannotRegister(registeredActions, loadedAction, nameof(loadedAction));

            if (invokeOnAlreadyLoaded && IsLoaded(sceneId))
            {
                loadedAction.Invoke();
                if (loadedAction.DisposeOnUsed)
                {
                    // 登録せずに終わるため、アクションリストは作らない
                    return BoardDispose.Empty;
                }
            }

            var list = GetOrCreateActionList(_loadedActions, sceneId);
            list.Add(loadedAction);
            return new BoardDispose(() => list.Remove(loadedAction));
        }

        /// <summary>
        /// 指定したシーンがアンロードされた時に実行するアクションを登録する。
        /// </summary>
        /// <param name="sceneId">対象のシーンID</param>
        /// <param name="unloadedAction">登録するアクション</param>
        /// <exception cref="ArgumentNullException">unloadedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable RegisterEventOnUnload(int sceneId, ActionEntry unloadedAction)
        {
            _unLoadedActions.TryGetValue(sceneId, out var registeredActions);
            ThrowIfCannotRegister(registeredActions, unloadedAction, nameof(unloadedAction));

            var list = GetOrCreateActionList(_unLoadedActions, sceneId);
            list.Add(unloadedAction);
            return new BoardDispose(() => list.Remove(unloadedAction));
        }

        /// <summary>
        /// どのシーンがロードされた時でも実行するアクションを登録する。
        /// </summary>
        /// <param name="loadedAction">登録するアクション</param>
        /// <exception cref="ArgumentNullException">loadedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable RegisterEventAnySceneLoaded(ActionEntry loadedAction)
        {
            ThrowIfCannotRegister(_anySceneLoadedActions, loadedAction, nameof(loadedAction));

            _anySceneLoadedActions.Add(loadedAction);
            return new BoardDispose(() => _anySceneLoadedActions.Remove(loadedAction));
        }

        /// <summary>
        /// アクティブシーンが切り替わった時に実行するアクションを登録する。
        /// </summary>
        /// <param name="changedAction">登録するアクション</param>
        /// <exception cref="ArgumentNullException">changedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable RegisterEventOnActiveSceneChanged(ActionEntry changedAction)
        {
            ThrowIfCannotRegister(_activeSceneChangedActions, changedAction, nameof(changedAction));

            _activeSceneChangedActions.Add(changedAction);
            return new BoardDispose(() => _activeSceneChangedActions.Remove(changedAction));
        }

        #endregion

        #region ロード/アンロードの進行状況

        /// <summary>
        /// ロードの開始をStateへ反映する。
        /// ロード/アンロードが進行中の場合は開始できない。
        /// </summary>
        /// <returns>開始できたか</returns>
        public bool TryBeginLoad()
        {
            if (Phase != SceneLoadPhase.None)
            {
                UsefulLogger.LogWarning($"{Phase}が進行中の為、ロードを開始できません。", this);
                return false;
            }

            Phase = SceneLoadPhase.Loading;
            return true;
        }

        /// <summary>
        /// アンロードの開始をStateへ反映する。
        /// ロード/アンロードが進行中の場合は開始できない。
        /// </summary>
        /// <returns>開始できたか</returns>
        public bool TryBeginUnLoad()
        {
            if (Phase != SceneLoadPhase.None)
            {
                UsefulLogger.LogWarning($"{Phase}が進行中の為、アンロードを開始できません。", this);
                return false;
            }

            Phase = SceneLoadPhase.UnLoading;
            return true;
        }

        /// <summary>
        /// 進行中のロード/アンロードの終了をStateへ反映する。
        /// LoadProgressの計算は外部に任せているため、完了の判断はこの呼び出しで行う。
        /// </summary>
        public void EndPhase()
        {
            if (Phase == SceneLoadPhase.None)
            {
                UsefulLogger.LogWarning("進行中のロード/アンロードがない為、終了できません。", this);
                return;
            }

            Phase = SceneLoadPhase.None;
        }

        /// <summary>
        /// ロードをStateへ反映してよいか。アンロードが終わるまでロードはできない。
        /// </summary>
        private bool CanApplyLoad()
        {
            if (Phase == SceneLoadPhase.UnLoading)
            {
                UsefulLogger.LogWarning("アンロードが進行中の為、ロードできません。", this);
                return false;
            }

            return true;
        }

        /// <summary>
        /// アンロードをStateへ反映してよいか。ロードが終わるまでアンロードはできない。
        /// </summary>
        private bool CanApplyUnLoad()
        {
            if (Phase == SceneLoadPhase.Loading)
            {
                UsefulLogger.LogWarning("ロードが進行中の為、アンロードできません。", this);
                return false;
            }

            return true;
        }

        #endregion

        #region 実体を保持するクラスが利用できるメソッド

        /// <summary>
        /// 複数のシーンを一気にロードする。
        /// </summary>
        /// <param name="activeScene">アクティブシーンにするシーンID</param>
        /// <param name="additiveScenes">追加でロードするシーンID</param>
        /// <returns>指定した全てのシーンがStateへ反映されたか</returns>
        public bool LoadMultiScene(int activeScene, int[] additiveScenes)
        {
            if (!CanApplyLoad())
            {
                return false;
            }

            var loadedScenes = ListPool<int>.Get();
            try
            {
                var activeSceneChanged = TryApplyLoadActiveScene(activeScene, out var previousActiveScene);
                if (activeSceneChanged)
                {
                    loadedScenes.Add(activeScene);
                }

                ApplyLoadAdditiveScenes(additiveScenes, loadedScenes);

                if (activeSceneChanged && previousActiveScene != NoSceneId)
                {
                    CheckUnLoadedActions(previousActiveScene);
                }

                NotifyLoadedScenes(loadedScenes);

                if (activeSceneChanged)
                {
                    NotifyActiveSceneChanged();
                }

                return activeSceneChanged && loadedScenes.Count == additiveScenes.Length + 1;
            }
            finally
            {
                ListPool<int>.Release(loadedScenes);
            }
        }

        /// <summary>
        /// 複数のアディティブシーンを一気にロードする。
        /// </summary>
        /// <param name="additiveScenes">ロードするシーンID</param>
        /// <returns>指定した全てのシーンがStateへ反映されたか</returns>
        public bool LoadAdditiveScenes(ReadOnlySpan<int> additiveScenes)
        {
            if (!CanApplyLoad())
            {
                return false;
            }

            var loadedScenes = ListPool<int>.Get();
            try
            {
                ApplyLoadAdditiveScenes(additiveScenes, loadedScenes);
                NotifyLoadedScenes(loadedScenes);

                return loadedScenes.Count == additiveScenes.Length;
            }
            finally
            {
                ListPool<int>.Release(loadedScenes);
            }
        }

        /// <summary>
        /// 複数のアディティブシーンを一気にロードする。
        /// </summary>
        /// <param name="additiveScenes">ロードするシーンID</param>
        /// <returns>指定した全てのシーンがStateへ反映されたか</returns>
        public bool LoadAdditiveScenes(int[] additiveScenes)
        {
            return LoadAdditiveScenes(additiveScenes.AsSpan());
        }

        /// <summary>
        /// 複数のアディティブシーンを一気にアンロードする。
        /// </summary>
        /// <param name="additiveScenes">アンロードするシーンID</param>
        /// <returns>指定した全てのシーンがStateへ反映されたか</returns>
        public bool UnLoadAdditiveScenes(ReadOnlySpan<int> additiveScenes)
        {
            if (!CanApplyUnLoad())
            {
                return false;
            }

            var unloadedScenes = ListPool<int>.Get();
            try
            {
                foreach (var additiveScene in additiveScenes)
                {
                    if (TryApplyUnLoadAdditiveScene(additiveScene))
                    {
                        unloadedScenes.Add(additiveScene);
                    }
                }

                for (int i = 0; i < unloadedScenes.Count; i++)
                {
                    CheckUnLoadedActions(unloadedScenes[i]);
                }

                return unloadedScenes.Count == additiveScenes.Length;
            }
            finally
            {
                ListPool<int>.Release(unloadedScenes);
            }
        }

        /// <summary>
        /// 複数のアディティブシーンを一気にアンロードする。
        /// </summary>
        /// <param name="additiveScenes">アンロードするシーンID</param>
        /// <returns>指定した全てのシーンがStateへ反映されたか</returns>
        public bool UnLoadAdditiveScenes(int[] additiveScenes)
        {
            return UnLoadAdditiveScenes(additiveScenes.AsSpan());
        }

        /// <summary>
        /// すべてのアディティブシーンを一気にアンロードする
        /// </summary>
        /// <returns>全てのアディティブシーンがStateへ反映されたか</returns>
        public bool ClearAdditiveScenes()
        {
            var additiveSceneCount = _additiveScenes.Count;
            if (additiveSceneCount == 0)
            {
                return true;
            }

            // アンロード中に_additiveScenesが変化するため、対象を先に複製しておく
            int[] loadedAdditiveSceneIds = ArrayPool<int>.Shared.Rent(additiveSceneCount);
            try
            {
                _additiveScenes.CopyTo(loadedAdditiveSceneIds, 0);
                return UnLoadAdditiveScenes(loadedAdditiveSceneIds.AsSpan(0, additiveSceneCount));
            }
            finally
            {
                ArrayPool<int>.Shared.Return(loadedAdditiveSceneIds);
            }
        }

        /// <summary>
        /// アクティブシーンをロードする
        /// </summary>
        /// <param name="sceneId">ロードするシーンID</param>
        /// <returns>Stateへ反映されたか</returns>
        public bool LoadActiveScene(int sceneId)
        {
            if (!CanApplyLoad())
            {
                return false;
            }

            if (!TryApplyLoadActiveScene(sceneId, out var previousActiveScene))
            {
                return false;
            }

            if (previousActiveScene != NoSceneId)
            {
                CheckUnLoadedActions(previousActiveScene);
            }

            CheckLoadedActions(sceneId);
            NotifyActiveSceneChanged();

            return true;
        }

        /// <summary>
        /// アディティブシーンをロードする
        /// </summary>
        /// <param name="sceneId">ロードするシーンID</param>
        /// <returns>Stateへ反映されたか</returns>
        public bool LoadAdditiveScene(int sceneId)
        {
            if (!CanApplyLoad())
            {
                return false;
            }

            if (!TryApplyLoadAdditiveScene(sceneId))
            {
                return false;
            }

            CheckLoadedActions(sceneId);

            return true;
        }

        /// <summary>
        /// ロード済みのアディティブシーンをアクティブシーンへ昇格させ、それまでのアクティブシーンをアディティブシーンへ降格させる。
        /// ロードでもアンロードでもないため、進行状況に関わらず実行できる。
        /// </summary>
        /// <param name="newActiveSceneId">アクティブシーンへ昇格させるシーンID</param>
        /// <returns>Stateへ反映されたか</returns>
        public bool ChangeActiveScene(int newActiveSceneId)
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

            NotifyActiveSceneChanged();

            return true;
        }

        /// <summary>
        /// アディティブシーンをアンロードする
        /// </summary>
        /// <param name="sceneId">アンロードするシーンID</param>
        /// <returns>Stateへ反映されたか</returns>
        public bool UnLoadAdditiveScene(int sceneId)
        {
            if (!CanApplyUnLoad())
            {
                return false;
            }

            if (!TryApplyUnLoadAdditiveScene(sceneId))
            {
                return false;
            }

            CheckUnLoadedActions(sceneId);

            return true;
        }

        #endregion

        public override string GetLog()
        {
            var allList = new List<int>(_additiveScenes);
            allList.Insert(0, ActiveScene);
            return string.Join("\n", allList);
        }

        #region Stateの更新

        /// <summary>
        /// アクティブシーンのロードをStateへ反映する。通知は行わない。
        /// </summary>
        /// <param name="sceneId">ロードするシーンID</param>
        /// <param name="previousActiveScene">反映前のアクティブシーン。未設定だった場合はNoSceneId</param>
        /// <returns>Stateが更新されたか</returns>
        private bool TryApplyLoadActiveScene(int sceneId, out int previousActiveScene)
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
        /// アディティブシーンのロードをStateへ反映する。通知は行わない。
        /// </summary>
        /// <param name="sceneId">ロードするシーンID</param>
        /// <returns>Stateが更新されたか</returns>
        private bool TryApplyLoadAdditiveScene(int sceneId)
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
        /// アディティブシーンのアンロードをStateへ反映する。通知は行わない。
        /// </summary>
        /// <param name="sceneId">アンロードするシーンID</param>
        /// <returns>Stateが更新されたか</returns>
        private bool TryApplyUnLoadAdditiveScene(int sceneId)
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
        /// 複数のアディティブシーンのロードをStateへ反映する。通知は行わない。
        /// </summary>
        /// <param name="additiveScenes">ロードするシーンID</param>
        /// <param name="loadedScenes">実際にStateへ反映されたシーンIDの追加先</param>
        private void ApplyLoadAdditiveScenes(ReadOnlySpan<int> additiveScenes, List<int> loadedScenes)
        {
            foreach (var additiveScene in additiveScenes)
            {
                if (TryApplyLoadAdditiveScene(additiveScene))
                {
                    loadedScenes.Add(additiveScene);
                }
            }
        }

        #endregion

        #region アクションの実行

        /// <summary>
        /// 指定シーンのアクションリストを取得する。無ければ作成して辞書へ登録する。
        /// </summary>
        private static List<ActionEntry> GetOrCreateActionList(Dictionary<int, List<ActionEntry>> actions, int sceneId)
        {
            if (!actions.TryGetValue(sceneId, out var list))
            {
                list = new List<ActionEntry>();
                actions[sceneId] = list;
            }

            return list;
        }

        /// <summary>
        /// 登録できないActionEntryを弾く。
        /// </summary>
        /// <param name="registeredActions">登録先のリスト。まだ作られていない場合はnull</param>
        /// <param name="entry">登録しようとしているActionEntry</param>
        /// <param name="paramName">例外に含める引数名</param>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        private static void ThrowIfCannotRegister(List<ActionEntry> registeredActions, ActionEntry entry,
            string paramName)
        {
            if (!entry.HasAction)
            {
                throw new ArgumentNullException(paramName, "ActionEntryに実行するActionが設定されていません。");
            }

            if (registeredActions != null && registeredActions.Contains(entry))
            {
                throw new InvalidOperationException($"アクション [{entry.ActionName}] はすでに登録されています。");
            }
        }

        private void CheckLoadedActions(int sceneId)
        {
            if (_loadedActions.TryGetValue(sceneId, out var loadAction))
            {
                InvokeActionEntries(loadAction);
            }

            if (_anySceneLoadedActions.Count != 0)
            {
                InvokeActionEntries(_anySceneLoadedActions);
            }
        }

        private void CheckUnLoadedActions(int unloadedSceneId)
        {
            if (_unLoadedActions.TryGetValue(unloadedSceneId, out var unLoadAction))
            {
                InvokeActionEntries(unLoadAction);
            }
        }

        /// <summary>
        /// Stateへ反映済みのシーンについて、ロード時のアクションをまとめて実行する。
        /// </summary>
        /// <param name="loadedScenes">ロードされたシーンID</param>
        private void NotifyLoadedScenes(List<int> loadedScenes)
        {
            for (int i = 0; i < loadedScenes.Count; i++)
            {
                CheckLoadedActions(loadedScenes[i]);
            }
        }

        private void NotifyActiveSceneChanged()
        {
            if (_activeSceneChangedActions.Count != 0)
            {
                InvokeActionEntries(_activeSceneChangedActions);
            }
        }

        private void InvokeActionEntries(List<ActionEntry> actions)
        {
            List<ActionEntry> temporaryList = CollectionPool<List<ActionEntry>, ActionEntry>.Get();
            try
            {
                temporaryList.Clear();
                temporaryList.AddRange(actions);

                // 実行中にロードが入れ子で走った際に使い捨てのアクションが二重実行されないように実行する前にリストから取り除いておく
                for (int i = actions.Count - 1; i >= 0; i--)
                {
                    if (actions[i].DisposeOnUsed)
                    {
                        actions.RemoveAt(i);
                    }
                }

                for (int i = 0; i < temporaryList.Count; i++)
                {
                    temporaryList[i].Invoke();
                }
            }
            finally
            {
                CollectionPool<List<ActionEntry>, ActionEntry>.Release(temporaryList);
            }
        }

        #endregion

        /// <summary>
        /// ロードの進捗を受け取る。valueは0~1
        /// </summary>
        /// <param name="value">進捗</param>
        public void Report(float value)
        {
            LoadProgress = Mathf.Clamp01(value);
        }
    }

    public interface ISceneState : IStateGetter
    {
        public float LoadProgress { get; }

        /// <summary> ロード/アンロードの進行状況 </summary>
        public SceneLoadPhase Phase { get; }

        /// <summary> ロードが進行中か。PhaseがLoadingであることと同じ </summary>
        public bool IsLoading { get; }

        /// <summary> ロード済みの現在のアクティブシーン </summary>
        public int ActiveScene { get; }

        public IReadOnlyList<int> AdditiveScenes { get; }

        public bool IsLoaded(int sceneId);

        /// <summary>
        /// 特定のシーンがロードされたときに実行されるActionを登録する
        /// </summary>
        /// <param name="sceneId">対象のシーンID</param>
        /// <param name="loadedAction">ロードされた際に実行されるAction</param>
        /// <param name="invokeOnAlreadyLoaded">
        /// 登録時にすでにロード済みだった際に実行するかどうか。
        /// 実行しても登録は維持されるかはActionEntryに依存する。
        /// </param>
        /// <returns>Disposeすると登録を解除できる</returns>
        public IDisposable RegisterEventOnLoad(int sceneId, ActionEntry loadedAction,
            bool invokeOnAlreadyLoaded = false);

        /// <summary>
        /// 特定のシーンがアンロードされたときに実行されるActionを登録する
        /// </summary>
        /// <param name="sceneId">対象のシーンID</param>
        /// <param name="unloadedAction">アンロードされた際に実行されるAction</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        public IDisposable RegisterEventOnUnload(int sceneId, ActionEntry unloadedAction);

        /// <summary>
        /// いずれかのシーンがロードされた際に実行されるActionを登録する
        /// </summary>
        /// <param name="loadedAction">シーンロード時に実行されるAction</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        public IDisposable RegisterEventAnySceneLoaded(ActionEntry loadedAction);

        /// <summary>
        /// アクティブシーンが切り替わった際に実行されるActionを登録する。
        /// 新しいシーンのロードによる切り替えと、ロード済みシーンの昇格による切り替えの両方で実行される。
        /// </summary>
        /// <param name="changedAction">アクティブシーン切り替え時に実行されるAction</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        public IDisposable RegisterEventOnActiveSceneChanged(ActionEntry changedAction);
    }
}