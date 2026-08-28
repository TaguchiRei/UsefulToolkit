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

        private readonly LoadedSceneSet _loadedScenes = new();
        private readonly SceneLoadRequester _loadRequester;

        private readonly KeyedActionEntryList<int> _loadedActions = new();
        private readonly KeyedActionEntryList<int> _unLoadedActions = new();
        private readonly ActionEntryList<int[], bool> _anySceneLoadedActions = new();
        private readonly ActionEntryList _activeSceneChangedActions = new();
        private readonly ActionEntryList<SceneLoadPhase> _phaseChangedActions = new();

        public SceneState()
        {
            _loadRequester = new SceneLoadRequester(this);
        }

        #region ISceneState実装

        public bool IsLoaded(int sceneId)
        {
            return _loadedScenes.IsLoaded(sceneId);
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

            // アンロード中にアディティブシーンの集合が変化するため、対象を先に複製しておく
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
                var previousActiveScene = NoSceneId;

                if (loadActiveScene)
                {
                    activeSceneChanged = _loadedScenes.TryLoadActiveScene(activeScene, out previousActiveScene);
                    if (activeSceneChanged)
                    {
                        // 0番目をアクティブシーンとして通知するため先頭へ入れる
                        loadedScenes.Add(activeScene);
                    }
                }

                _loadedScenes.LoadAdditiveScenes(additiveScenes, loadedScenes);

                if (activeSceneChanged && previousActiveScene != NoSceneId)
                {
                    _unLoadedActions.Invoke(previousActiveScene);
                }

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
