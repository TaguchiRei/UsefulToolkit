using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary>
    /// シーンのロード状況の読み取り、変化時のActionの登録、
    /// ロード/アンロードの要求を行うためのインターフェース。
    /// </summary>
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
        /// 指定したシーンが常駐シーンか。常駐シーンはアクティブ化・アンロード・降格のいずれもされない。
        /// </summary>
        /// <param name="sceneId">確認するシーンID</param>
        public bool IsPersistentScene(int sceneId);

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
        /// <param name="loadedAction">シーンロード時に実行されるAction。引数に新規にロードされたシーン情報が入り、第二引数がtrueの際は配列の０番目がアクティブシーンとしてロードされている
        /// </param>
        /// <returns>Disposeすると登録を解除できる</returns>
        public IDisposable RegisterEventAnySceneLoaded(ActionEntry<int[], bool> loadedAction);

        /// <summary>
        /// アクティブシーンが切り替わった際に実行されるActionを登録する。
        /// 新しいシーンのロードによる切り替えと、ロード済みシーンの昇格による切り替えの両方で実行される。
        /// </summary>
        /// <param name="changedAction">アクティブシーン切り替え時に実行されるAction</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        public IDisposable RegisterEventOnActiveSceneChanged(ActionEntry changedAction);

        /// <summary>
        /// ロード/アンロードの進行状況が変わった際に実行されるActionを登録する。
        /// ロード開始/終了とアンロード開始/終了は全てこの一本で受け取り、引数のPhaseで区別する。
        /// </summary>
        /// <param name="changedAction">進行状況の変化時に実行されるAction。引数には変化後のPhaseが入る</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        public IDisposable RegisterEventOnPhaseChanged(ActionEntry<SceneLoadPhase> changedAction);

        /// <summary>
        /// シーンのロードを要求する。
        /// 既にロード済みのシーンは読み直さないため、要求する側は重複を気にしなくてよい。
        /// </summary>
        /// <param name="mainSceneId">アクティブシーンにするシーンID。SceneState.NoSceneIdならアクティブシーンは変えない</param>
        /// <param name="subSceneIds">共にロードするシーンID</param>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        /// <returns>要求した全てのシーンがロードされStateへ反映されたか</returns>
        public UniTask<bool> RequestLoadAsync(int mainSceneId, IReadOnlyList<int> subSceneIds,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// シーンのアンロードを要求する。
        /// ロードされていないシーン、アクティブシーン、常駐シーンは対象から外れる。
        /// </summary>
        /// <param name="sceneIds">アンロードするシーンID</param>
        /// <param name="cancellationToken">アンロードの中断に使う</param>
        /// <returns>対象の全てのシーンがアンロードされStateへ反映されたか</returns>
        public UniTask<bool> RequestUnLoadAsync(IReadOnlyList<int> sceneIds,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// シーンを上書きロードする。
        /// 現在ロード中の管理シーン(アクティブ + アディティブ)のうち、要求に含まれず常駐でもないものを
        /// 全てアンロードしてから、要求シーンをロードする。
        /// アクティブシーンも要求に含まれなければアンロードされ、アディティブシーンへの降格は行わない。
        /// 「元のシーン状況を丸ごとこのグループで上書きする」用途向け。
        /// </summary>
        /// <param name="mainSceneId">アクティブシーンにするシーンID。SceneState.NoSceneIdならアクティブシーンはNoneになる</param>
        /// <param name="subSceneIds">共にロードするシーンID</param>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        /// <returns>要求した全てのシーンがロードされ、余剰シーンのアンロードまでStateへ反映されたか</returns>
        public UniTask<bool> RequestOverwriteLoadAsync(int mainSceneId, IReadOnlyList<int> subSceneIds,
            CancellationToken cancellationToken = default);
    }
}
