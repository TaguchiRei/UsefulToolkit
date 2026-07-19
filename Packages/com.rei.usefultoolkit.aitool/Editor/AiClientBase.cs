using System;
using System.Collections.Generic;

namespace UsefulToolkit.Ai
{
    /// <summary>
    /// AIクライアントの抽象基底クラス。
    /// さまざまなAIプロバイダーのクライアント共通のインターフェースを定義します。
    /// </summary>
    [Serializable]
    public abstract class AiClientBase : IAiClient
    {
        /// <summary>
        /// クライアントの表示名
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// APIリクエストのタイムアウト時間（秒）。
        /// </summary>
        public abstract int TimeoutSeconds { get; set; }

        /// <summary>
        /// AIの役割や振る舞いを定義するシステムプロンプト。
        /// </summary>
        public abstract string SystemInstructionText { get; set; }

        /// <summary>
        /// 現在の会話履歴をChatMessageのリストとしてエクスポートします。
        /// </summary>
        public abstract List<ChatMessage> ExportMessages();

        /// <summary>
        /// ChatMessageのリストから会話履歴をインポート（復元）します。
        /// </summary>
        public abstract void ImportMessages(List<ChatMessage> messages);

        /// <summary>
        /// クライアント内の会話履歴をクリアします。
        /// </summary>
        public abstract void ClearConversation();

        /// <summary>
        /// メッセージを送信し、応答を取得します。
        /// </summary>
        public abstract System.Threading.Tasks.Task<string> SendMessageAsync(string message, System.Threading.CancellationToken ct = default);
    }
}
