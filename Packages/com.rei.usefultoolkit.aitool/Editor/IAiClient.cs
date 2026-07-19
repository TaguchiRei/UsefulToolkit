using System.Collections.Generic;

namespace UsefulToolkit.Ai
{
    /// <summary>
    /// AIクライアントの共通インタフェース。
    /// </summary>
    public interface IAiClient
    {
        string Name { get; }
        int TimeoutSeconds { get; set; }
        string SystemInstructionText { get; set; }
        List<ChatMessage> ExportMessages();
        void ImportMessages(List<ChatMessage> messages);
        void ClearConversation();
        System.Threading.Tasks.Task<string> SendMessageAsync(string message, System.Threading.CancellationToken ct = default);
    }
}
