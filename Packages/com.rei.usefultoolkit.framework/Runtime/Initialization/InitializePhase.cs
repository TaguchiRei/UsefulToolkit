namespace UsefulToolkit.Initialization
{
    /// <summary>
    /// GameCompositorが今どの初期化フェーズにいるかを表す。
    /// TryRegisterContentをAwake(収集フェーズ)以外から呼んだ誤用を検出するために使う。
    /// Abortedは誤用を検出して初期化を打ち切った状態で、Noneとはログの文言を分ける。
    /// </summary>
    internal enum InitializePhase
    {
        None,
        Collection,
        Inject,
        Initialize,
        Aborted
    }
}
