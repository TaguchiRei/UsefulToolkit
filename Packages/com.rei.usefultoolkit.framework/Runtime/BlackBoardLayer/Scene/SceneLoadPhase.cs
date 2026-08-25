namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary> シーンのロード/アンロードの進行状況 </summary>
    public enum SceneLoadPhase
    {
        /// <summary> 進行中の処理はない。ロードもアンロードも開始できる </summary>
        None,

        /// <summary> ロード中。終わるまでアンロードはできない </summary>
        Loading,

        /// <summary> アンロード中。終わるまでロードはできない </summary>
        UnLoading,
    }
}