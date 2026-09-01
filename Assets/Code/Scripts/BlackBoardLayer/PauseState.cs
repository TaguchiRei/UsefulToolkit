using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.ProgramTools;

namespace Sandbox.BlackBoard
{
    /// <summary>
    /// ポーズ状態の読み取り面。BlackBoard 経由でどのシーンからでも取得できる。
    /// </summary>
    public interface IPauseState : IStateGetter
    {
        bool IsPaused { get; }
    }

    /// <summary>
    /// ポーズ状態そのもの。BlackBoard へは IPauseState として登録する為、
    /// 値を書き換えられるのは具象型を保持している生成元 (PauseManager) だけになる。
    /// </summary>
    [RegisterBoard(typeof(PauseBoard))]
    public sealed class PauseState : GameStateBase, IPauseState
    {
        public bool IsPaused { get; set; }

        public override string GetLog() => $"IsPaused : {IsPaused}";
    }
}
