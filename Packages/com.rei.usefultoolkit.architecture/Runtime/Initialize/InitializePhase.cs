namespace UsefulToolkit.Architecture
{
    /// <summary>
    /// GameCompositerが今どの初期化フェーズにいるかを表す。
    /// TryRegisterContentをAwake(収集フェーズ)以外から呼んだ誤用を検出するために使う。
    /// </summary>
    internal enum InitializePhase
    {
        None,
        Collection,
        Inject,
        Initialize
    }
}
