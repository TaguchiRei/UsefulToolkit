namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// EventBoardへ登録するイベントインターフェースの基盤。
    /// ChildEventBoardBaseはこれを継承したインターフェースの型をキーにチャンネルを保持する。
    /// 実装(ActionChannelなど)はInvokeを持つが、購読側へはこのインターフェース派生型として
    /// しか公開しないことで、外部からInvokeされる事故を型で防ぐ。
    /// </summary>
    public interface IEvent
    {
    }
}
