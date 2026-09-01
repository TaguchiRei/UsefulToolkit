using Sandbox.BlackBoard;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.BlackBoard.ProgramTools;

namespace Sandbox.Application
{
    /// <summary>
    /// ポーズ機能の操作面。State を書き換える手段はこの面にしか無く、
    /// DI コンテナ経由で各シーンへ配られる。
    /// </summary>
    public interface IPauseManager
    {
        void Pause();
        void Resume();
        void Toggle();
    }

    /// <summary>
    /// PauseState を生成・所有し、PauseBoard へ読み取り面として登録する Application クラス。
    ///
    /// 生成 (コンストラクタ) と初期化 (Initialize) を分けているのは、
    /// コンテナへの登録が Collection フェーズ (Awake) 限定である一方、
    /// BlackBoard が渡るのは Initialize フェーズだから。この分割により、
    /// State を生成する Application クラスでもコンテナに載せて他シーンへ配れる。
    /// </summary>
    public sealed class PauseManager : IPauseManager
    {
        private PauseState _pauseState;

        /// <summary>PauseState を生成し、PauseBoard へ IPauseState として登録する。</summary>
        public void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetStateBoard<PauseBoard>(out var pauseBoard))
            {
                UsefulLogger.LogError("PauseBoard を取得できない為、PauseState を登録できません。", this);
                return;
            }

            _pauseState = new PauseState();
            pauseBoard.RegisterGameState<IPauseState>(_pauseState);
        }

        public void Pause() => SetPaused(true);

        public void Resume() => SetPaused(false);

        public void Toggle() => SetPaused(_pauseState != null && !_pauseState.IsPaused);

        private void SetPaused(bool paused)
        {
            if (_pauseState == null)
            {
                UsefulLogger.LogError("Initialize が済んでいない為、ポーズ状態を変更できません。", this);
                return;
            }

            _pauseState.IsPaused = paused;
        }
    }
}
