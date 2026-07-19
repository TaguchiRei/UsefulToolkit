namespace UsefulToolkit.Ai
{
    /// <summary>
    /// 他のエージェントに対するコマンドインターフェース。
    /// </summary>
    public interface IContactCommand
    {
        string Description { get; }
        /// <summary>
        /// 特定のエージェントに指示を転送します。
        /// </summary>
        /// <param name="targetAgentGuid">宛先となるAIのGUID</param>
        /// <param name="message">指示内容</param>
        /// <returns>処理結果</returns>
        string Execute(string targetAgentGuid, string message);
    }
}
