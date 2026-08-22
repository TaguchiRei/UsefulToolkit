using System.Collections.Generic;
using System;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary>
    /// シーンStateの読み取り面。今どのシーングループにいるか、どこへ遷移できるか、
    /// 遷移中かどうかを取得でき、それらが変化したときのActionを登録できる。
    /// </summary>
    public interface ISceneStateGetter : IStateGetter
    {
        /// <summary>
        /// 現在読み込んでいるシーングループ。
        /// 未遷移のときと、直前の遷移が失敗して確定できないときはSceneGroupId.None。
        /// </summary>
        SceneGroupId CurrentGroup { get; }

        /// <summary> 現在のグループから遷移できるシーングループの一覧 </summary>
        IReadOnlyList<SceneGroupId> NextGroups { get; }

        /// <summary> ロード・アンロードの対象外として、ゲーム中ずっと読み込まれ続けるシーン名の一覧 </summary>
        IReadOnlyList<string> PersistentScenes { get; }

        /// <summary> 遷移の進行状況 </summary>
        SceneTransitionPhase Phase { get; }

        /// <summary> 遷移中かどうか </summary>
        bool IsTransitioning { get; }

        /// <summary>
        /// CurrentGroupが確定・変化したときに実行されるハンドラを登録する。
        /// 遷移完了時(同じグループへの再遷移でも発火する)と、遷移失敗でNoneになったときに呼ばれる。
        /// </summary>
        /// <returns>Disposeを実行すると登録解除される</returns>
        /// <exception cref="ArgumentNullException">handlerがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">同じハンドラがすでに登録されているときに出力</exception>
        IDisposable RegisterOnCurrentGroupChanged(Action<StateContext<SceneGroupId>> handler);

        /// <summary>
        /// 指定したシーングループへの遷移が完了したときに実行されるActionを登録する。
        /// 登録時点ですでにそのグループにいても発火せず、遷移が失敗した場合も発火しない。
        /// </summary>
        /// <returns>Disposeを実行すると登録解除される</returns>
        /// <exception cref="ArgumentNullException">actionがnullのときに出力</exception>
        IDisposable RegisterOnGroupLoaded(SceneGroupId group, Action action);

        /// <summary>
        /// Phaseが変化したときに実行されるハンドラを登録する。
        /// ローディング画面の開閉や遷移中の入力ブロックに使う。
        /// </summary>
        /// <returns>Disposeを実行すると登録解除される</returns>
        /// <exception cref="ArgumentNullException">handlerがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">同じハンドラがすでに登録されているときに出力</exception>
        IDisposable RegisterOnPhaseChanged(Action<StateContext<SceneTransitionPhase>> handler);
    }
}
