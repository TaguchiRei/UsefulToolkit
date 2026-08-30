namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// EventBoardへ登録するイベントインターフェースの基盤。
    /// ChildEventBoardBaseはこれを継承したインターフェースの型をキーにチャンネルを保持する。
    /// 実装(ActionChannelなど)はInvokeを持つが、購読側へはこのインターフェース派生型としてのみ公開する。
    /// </summary>
    public interface IEvent
    {
    }
}
