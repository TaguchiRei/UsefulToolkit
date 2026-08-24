namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary> シーン遷移の進行状況 </summary>
    public enum SceneTransitionPhase
    {
        /// <summary> 遷移していない。CurrentGroupは実際のシーン構成と一致している </summary>
        Idle,

        /// <summary> 遷移中。CurrentGroupは遷移元を指したままで、実際のシーン構成とは一致しない </summary>
        Loading,

        /// <summary> 直前の遷移が例外で中断した。CurrentGroupはSceneGroupId.None </summary>
        Failed,
    }
}
