namespace UsefulToolkit.BlackBoard
{
    /// <summary>
    /// モジュール単位でEventChannelを公開する子ボードの基底クラス。BlackBoardへ登録するための
    /// 型として機能するだけで、チャンネルの保存方法・キー設計(型キー/複合キーなど)は
    /// サブクラス(例: InputBoard)の責務とする——ChildStateBoardBaseとは保存領域を共有しない。
    /// </summary>
    public abstract class ChildEventBoardBase
    {
    }
}
