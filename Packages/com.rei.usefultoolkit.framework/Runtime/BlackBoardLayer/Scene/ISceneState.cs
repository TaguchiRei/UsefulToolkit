using System;
using System.Collections.Generic;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary>
    /// シーンのロード状況を読み取り、その変化に対するActionを登録するためのインターフェース。
    /// Stateの書き換えはこの面には含めないため、これを取得したクラスはシーンを操作できない。
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
    }
}
