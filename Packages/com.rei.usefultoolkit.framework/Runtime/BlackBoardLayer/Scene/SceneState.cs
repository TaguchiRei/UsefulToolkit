using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;

namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary>
    /// どのシーンがロードされているかと、ロード/アンロードの進行状況を保持するState。
    /// 状態が変わると、その変化に対して登録されているActionを実行する。
    /// シーンをアンロードした際は、BlackBoardへ通知して各ChildBoardのシーンスコープを解除させる。
    /// </summary>
    [RegisterBoard(typeof(SceneBoard))]
    public class SceneState : GameStateBase, ISceneState, IProgress<float>
    {
        /// <summary> アクティブシーンが未設定であることを表すシーンID </summary>
        public const int NoSceneId = -1;

        public float LoadProgress { get; private set; } = 1;

        /// <summary> ロード/アンロードの進行状況。Noneの間だけ次の開始を受け付ける </summary>
        public SceneLoadPhase Phase { get; private set; } = SceneLoadPhase.None;

        public bool IsLoading => Phase == SceneLoadPhase.Loading;
        public int ActiveScene => _loadedScenes.ActiveScene;
        public IReadOnlyList<int> AdditiveScenes => _loadedScenes.AdditiveScenes;

        private readonly IBlackBoard _blackBoard;
        private readonly LoadedSceneSet _loadedScenes;
        private readonly SceneLoadRequester _loadRequester;

        private readonly KeyedActionEntryList<int> _loadedActions = new();
        private readonly KeyedActionEntryList<int> _unLoadedActions = new();
        private readonly ActionEntryList<int[], bool> _anySceneLoadedActions = new();
        private readonly ActionEntryList _activeSceneChangedActions = new();
        private readonly ActionEntryList<SceneLoadPhase> _phaseChangedActions = new();

        /// <param name="blackBoard">シーンのアンロードを通知する先</param>
        /// <param name="persistentSceneIds">
        /// 常駐シーンのビルドインデックス。アクティブシーンにはできず、アンロードや降格の対象にもならない。
        /// 常に「ロード済み」として扱う。
        /// </param>
        /// <exception cref="ArgumentNullException">blackBoardがnullのときに出力</exception>
        public SceneState(IBlackBoard blackBoard, IReadOnlyList<int> persistentSceneIds = null)
        {
            _blackBoard = blackBoard ?? throw new ArgumentNullException(nameof(blackBoard));
            _loadedScenes = new LoadedSceneSet(persistentSceneIds);
            _loadRequester = new SceneLoadRequester(this);
        }

        #region ISceneState実装

        public bool IsLoaded(int sceneId)
        {
            return _loadedScenes.IsLoaded(sceneId);
        }

        /// <summary>
        /// 指定したシーンが常駐シーンか。
        /// </summary>
        /// <param name="sceneId">確認するシーンID</param>
        public bool IsPersistentScene(int sceneId)
        {
            return _loadedScenes.IsPersistent(sceneId);
        }

        /// <summary>
        /// 指定したシーンをアクティブシーンとしてロード/昇格できるか。
        /// 負値と常駐シーンは不可。ロード要求は実行前にこれで弾かれる。
        /// </summary>
        /// <param name="sceneId">確認するシーンID</param>
        public bool CanBeActiveScene(int sceneId)
        {
            return _loadedScenes.CanBeActiveScene(sceneId);
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
            _loadedActions.ThrowIfCannotRegister(sceneId, loadedAction, nameof(loadedAction));

            if (invokeOnAlreadyLoaded && IsLoaded(sceneId))
            {
                loadedAction.Invoke();
                if (loadedAction.DisposeOnUsed)
                {
                    // 使い捨てのアクションは実行済みなので登録しない
                    return BoardDispose.Empty;
                }
            }

            return _loadedActions.Register(sceneId, loadedAction, nameof(loadedAction));
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
            return _unLoadedActions.Register(sceneId, unloadedAction, nameof(unloadedAction));
        }

        /// <summary>
        /// どのシーンがロードされた時でも実行するアクションを登録する。
        /// </summary>
        /// <param name="loadedAction">登録するアクション</param>
        /// <exception cref="ArgumentNullException">loadedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable RegisterEventAnySceneLoaded(ActionEntry<int[], bool> loadedAction)
        {
            return _anySceneLoadedActions.Register(loadedAction, nameof(loadedAction));
        }

        /// <summary>
        /// アクティブシーンが切り替わった時に実行するアクションを登録する。
        /// </summary>
        /// <param name="changedAction">登録するアクション</param>
        /// <exception cref="ArgumentNullException">changedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable RegisterEventOnActiveSceneChanged(ActionEntry changedAction)
        {
            return _activeSceneChangedActions.Register(changedAction, nameof(changedAction));
        }

        /// <summary>
        /// ロード/アンロードの進行状況が変わった時に実行するアクションを登録する。
        /// </summary>
        /// <param name="changedAction">登録するアクション。引数には変化後のPhaseが入る</param>
        /// <exception cref="ArgumentNullException">changedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable RegisterEventOnPhaseChanged(ActionEntry<SceneLoadPhase> changedAction)
        {
            return _phaseChangedActions.Register(changedAction, nameof(changedAction));
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
            NotifyPhaseChanged();
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
            NotifyPhaseChanged();
            return true;
        }

        /// <summary>
        /// 進行中のロード/アンロードの終了をStateへ反映し、Phase変更のActionを実行する。
        /// 進行中のロード/アンロードが無い場合は、警告ログを出して何もしない。
        /// </summary>
        public void EndPhase()
        {
            if (Phase == SceneLoadPhase.None)
            {
                UsefulLogger.LogWarning("進行中のロード/アンロードがない為、終了できません。", this);
                return;
            }

            Phase = SceneLoadPhase.None;
            NotifyPhaseChanged();
        }

        /// <summary>
        /// ロードをStateへ反映してよいか。アンロードが進行中ならfalseを返す。
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
        /// アンロードをStateへ反映してよいか。ロードが進行中ならfalseを返す。
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

        #region シーン操作の要求

        /// <summary>
        /// 実際にシーンを操作する処理を登録する。登録できるのは一度だけ。
        /// 登録前にロード/アンロードを要求した場合は、エラーログを出してfalseが返る。
        /// </summary>
        /// <param name="loadFunc">ロードを実行する処理</param>
        /// <param name="unLoadFunc">アンロードを実行する処理</param>
        /// <exception cref="ArgumentNullException">処理が指定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">既に登録済みのときに出力</exception>
        public void RegisterSceneLoader(SceneLoadFunc loadFunc, SceneUnLoadFunc unLoadFunc)
        {
            _loadRequester.RegisterSceneLoader(loadFunc, unLoadFunc);
        }

        public UniTask<bool> RequestLoadAsync(int mainSceneId, IReadOnlyList<int> subSceneIds,
            CancellationToken cancellationToken = default)
        {
            return _loadRequester.RequestLoadAsync(mainSceneId, subSceneIds, cancellationToken);
        }

        public UniTask<bool> RequestUnLoadAsync(IReadOnlyList<int> sceneIds,
            CancellationToken cancellationToken = default)
        {
            return _loadRequester.RequestUnLoadAsync(sceneIds, cancellationToken);
        }

        public UniTask<bool> RequestOverwriteLoadAsync(int mainSceneId, IReadOnlyList<int> subSceneIds,
            CancellationToken cancellationToken = default)
        {
            return _loadRequester.RequestOverwriteLoadAsync(mainSceneId, subSceneIds, cancellationToken);
        }

        #endregion

        #region シーンのロード状況をStateへ反映するメソッド

        /// <summary>
        /// 複数のシーンを一気にロードする。
        /// </summary>
        /// <param name="activeScene">アクティブシーンにするシーンID</param>
        /// <param name="additiveScenes">追加でロードするシーンID</param>
        /// <returns>指定した全てのシーンがStateへ反映されたか</returns>
        public bool LoadMultiScene(int activeScene, ReadOnlySpan<int> additiveScenes)
        {
            return ApplyLoad(activeScene, additiveScenes);
        }

        /// <summary>
        /// アクティブシーンをロードする
        /// </summary>
        /// <param name="sceneId">ロードするシーンID</param>
        /// <returns>Stateへ反映されたか</returns>
        public bool LoadActiveScene(int sceneId)
        {
            return ApplyLoad(sceneId, ReadOnlySpan<int>.Empty);
        }

        /// <summary>
        /// アディティブシーンをロードする
        /// </summary>
        /// <param name="sceneId">ロードするシーンID</param>
        /// <returns>Stateへ反映されたか</returns>
        public bool LoadAdditiveScene(int sceneId)
        {
            Span<int> additiveScenes = stackalloc int[1];
            additiveScenes[0] = sceneId;
            return ApplyLoad(NoSceneId, additiveScenes);
        }

        /// <summary>
        /// 複数のアディティブシーンを一気にロードする。
        /// </summary>
        /// <param name="additiveScenes">ロードするシーンID</param>
        /// <returns>指定した全てのシーンがStateへ反映されたか</returns>
        public bool LoadAdditiveScenes(ReadOnlySpan<int> additiveScenes)
        {
            return ApplyLoad(NoSceneId, additiveScenes);
        }

        /// <summary>
        /// アディティブシーンをアンロードする
        /// </summary>
        /// <param name="sceneId">アンロードするシーンID</param>
        /// <returns>Stateへ反映されたか</returns>
        public bool UnLoadAdditiveScene(int sceneId)
        {
            Span<int> additiveScenes = stackalloc int[1];
            additiveScenes[0] = sceneId;
            return ApplyUnLoad(additiveScenes);
        }

        /// <summary>
        /// 複数のアディティブシーンを一気にアンロードする。
        /// </summary>
        /// <param name="additiveScenes">アンロードするシーンID</param>
        /// <returns>指定した全てのシーンがStateへ反映されたか</returns>
        public bool UnLoadAdditiveScenes(ReadOnlySpan<int> additiveScenes)
        {
            return ApplyUnLoad(additiveScenes);
        }

        /// <summary>
        /// すべてのアディティブシーンを一気にアンロードする
        /// </summary>
        /// <returns>全てのアディティブシーンがStateへ反映されたか</returns>
        public bool ClearAdditiveScenes()
        {
            var additiveSceneCount = AdditiveScenes.Count;
            if (additiveSceneCount == 0)
            {
                return true;
            }

            // 対象のアディティブシーンIDを先に複製する(アンロード中に集合が変化する)
            int[] loadedAdditiveSceneIds = ArrayPool<int>.Shared.Rent(additiveSceneCount);
            try
            {
                var copiedCount = _loadedScenes.CopyAdditiveScenesTo(loadedAdditiveSceneIds);
                return ApplyUnLoad(loadedAdditiveSceneIds.AsSpan(0, copiedCount));
            }
            finally
            {
                ArrayPool<int>.Shared.Return(loadedAdditiveSceneIds);
            }
        }

        /// <summary>
        /// ロード済みのアディティブシーンをアクティブシーンへ昇格させ、それまでのアクティブシーンをアディティブシーンへ降格させる。
        /// ロードでもアンロードでもないため、進行状況に関わらず実行できる。
        /// </summary>
        /// <param name="newActiveSceneId">アクティブシーンへ昇格させるシーンID</param>
        /// <returns>Stateへ反映されたか</returns>
        public bool ChangeActiveScene(int newActiveSceneId)
        {
            if (!_loadedScenes.TryChangeActiveScene(newActiveSceneId))
            {
                return false;
            }

            NotifyActiveSceneChanged();
            return true;
        }

        /// <summary>
        /// 上書きロードで不要になったシーンを集合から取り除き、アンロード時のActionを実行する。
        /// アクティブシーンが対象に含まれる場合はアクティブシーンを未設定へ戻す。常駐シーンは対象外。
        /// ロード進行中(Loading)から呼ばれるため、<see cref="CanApplyUnLoad"/>のガードは通さない。
        /// </summary>
        /// <param name="sceneIds">取り除くシーンID</param>
        /// <returns>指定した全てのシーンが取り除かれたか</returns>
        public bool OverwriteUnload(ReadOnlySpan<int> sceneIds)
        {
            var unloadedScenes = ListPool<int>.Get();
            try
            {
                _loadedScenes.RemoveScenes(sceneIds, unloadedScenes);

                for (int i = 0; i < unloadedScenes.Count; i++)
                {
                    _unLoadedActions.Invoke(unloadedScenes[i]);
                }

                if (unloadedScenes.Count > 0)
                {
                    // Stateに登録されたActionを実行し終えてから、各ChildBoardのシーンスコープを解除する
                    _blackBoard.OnSceneChanged(unloadedScenes);
                }

                return unloadedScenes.Count == sceneIds.Length;
            }
            finally
            {
                ListPool<int>.Release(unloadedScenes);
            }
        }

        /// <summary>
        /// 現在ロード中の管理シーン(アクティブシーン + アディティブシーン、常駐シーンは除く)をバッファへ複製する。
        /// </summary>
        /// <param name="buffer">複製先</param>
        public void CopyLoadedScenesTo(List<int> buffer)
        {
            _loadedScenes.CopyLoadedScenesTo(buffer);
        }

        #endregion

        public override string GetLog()
        {
            var allList = new List<int>(AdditiveScenes);
            allList.Insert(0, ActiveScene);
            return string.Join("\n", allList);
        }

        #region Stateの更新

        /// <summary>
        /// ロードをStateへ反映し、ロード時と切り替え時のActionを実行する。
        /// </summary>
        /// <param name="activeScene">アクティブシーンにするシーンID。<see cref="NoSceneId"/>ならアクティブシーンは変更しない</param>
        /// <param name="additiveScenes">追加でロードするシーンID</param>
        /// <returns>指定した全てのシーンがStateへ反映されたか</returns>
        private bool ApplyLoad(int activeScene, ReadOnlySpan<int> additiveScenes)
        {
            if (!CanApplyLoad())
            {
                return false;
            }

            var loadedScenes = ListPool<int>.Get();
            try
            {
                var loadActiveScene = activeScene != NoSceneId;
                var activeSceneChanged = false;

                if (loadActiveScene)
                {
                    activeSceneChanged = _loadedScenes.TryLoadActiveScene(activeScene, out _);
                    if (activeSceneChanged)
                    {
                        // 0番目をアクティブシーンとして通知するため先頭へ入れる
                        loadedScenes.Add(activeScene);
                    }
                }

                _loadedScenes.LoadAdditiveScenes(additiveScenes, loadedScenes);

                // 旧アクティブシーンはアディティブシーンへ降格するだけで、OnUnloadのActionは実行しない。切り替えはNotifyActiveSceneChangedで通知する。

                NotifyLoadedScenes(loadedScenes);
                NotifyAnySceneLoaded(loadedScenes, activeSceneChanged);

                if (activeSceneChanged)
                {
                    NotifyActiveSceneChanged();
                }

                var requestedCount = additiveScenes.Length + (loadActiveScene ? 1 : 0);
                return loadedScenes.Count == requestedCount;
            }
            finally
            {
                ListPool<int>.Release(loadedScenes);
            }
        }

        /// <summary>
        /// アンロードをStateへ反映し、アンロード時のActionを実行する。
        /// </summary>
        /// <param name="additiveScenes">アンロードするシーンID</param>
        /// <returns>指定した全てのシーンがStateへ反映されたか</returns>
        private bool ApplyUnLoad(ReadOnlySpan<int> additiveScenes)
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
                    if (_loadedScenes.TryUnLoadAdditiveScene(additiveScene))
                    {
                        unloadedScenes.Add(additiveScene);
                    }
                }

                for (int i = 0; i < unloadedScenes.Count; i++)
                {
                    _unLoadedActions.Invoke(unloadedScenes[i]);
                }

                if (unloadedScenes.Count > 0)
                {
                    // Stateに登録されたActionを実行し終えてから、各ChildBoardのシーンスコープを解除する
                    _blackBoard.OnSceneChanged(unloadedScenes);
                }

                return unloadedScenes.Count == additiveScenes.Length;
            }
            finally
            {
                ListPool<int>.Release(unloadedScenes);
            }
        }

        #endregion

        #region アクションの実行

        /// <summary>
        /// Stateへ反映済みのシーンについて、ロード時のアクションをまとめて実行する。
        /// </summary>
        /// <param name="loadedScenes">ロードされたシーンID</param>
        private void NotifyLoadedScenes(List<int> loadedScenes)
        {
            for (int i = 0; i < loadedScenes.Count; i++)
            {
                _loadedActions.Invoke(loadedScenes[i]);
            }
        }

        /// <summary>
        /// Stateへ反映されたシーンをまとめて通知する。購読者がいない場合は配列を確保しない。
        /// </summary>
        /// <param name="loadedScenes">Stateへ反映されたシーンID</param>
        /// <param name="containsActiveScene">0番目のシーンがアクティブシーンとしてロードされたか</param>
        private void NotifyAnySceneLoaded(List<int> loadedScenes, bool containsActiveScene)
        {
            if (_anySceneLoadedActions.Count == 0 || loadedScenes.Count == 0)
            {
                return;
            }

            _anySceneLoadedActions.Invoke(loadedScenes.ToArray(), containsActiveScene);
        }

        private void NotifyActiveSceneChanged()
        {
            _activeSceneChangedActions.Invoke();
        }

        private void NotifyPhaseChanged()
        {
            _phaseChangedActions.Invoke(Phase);
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
}
