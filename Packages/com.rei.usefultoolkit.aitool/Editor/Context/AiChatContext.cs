using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UsefulToolkit.Framework;

namespace UsefulToolkit.Ai
{
    /// <summary>
    /// チャットメッセージの最小単位。
    /// </summary>
    [Serializable]
    public class ChatMessage
    {
        public string Role;
        public string Content;
    }

    /// <summary>
    /// 特定のエージェントに関連付けられた会話履歴。
    /// </summary>
    [Serializable]
    public class AgentHistory
    {
        public string AgentGuid;
        public List<ChatMessage> Messages = new List<ChatMessage>();
    }

    /// <summary>
    /// 会話全体のコンテキスト（参加エージェントと履歴）を管理するデータベース。
    /// </summary>
    [Serializable]
    public sealed class AiChatContext
    {
        [SerializeField] private string id;
        [SerializeField] private List<string> activeAgentGuids = new List<string>();
        [SerializeField] private List<AgentHistory> histories = new List<AgentHistory>();

        public string Id => id;
        public List<string> ActiveAgentGuids => activeAgentGuids;
        public List<AgentHistory> Histories => histories;

        private static string GetSavePath(string contextId) => $"UserSettings/UsefulToolkit/AiChat/Contexts/{contextId}.json";

        public AiChatContext(string id)
        {
            this.id = id;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(id)) return;

            var json = JsonUtility.ToJson(this, true);
            string savePath = GetSavePath(id);
            FileGenerator.WriteFile(savePath, json);
        }

        public static AiChatContext Load(string contextId)
        {
            string savePath = GetSavePath(contextId);
            if (!File.Exists(savePath))
            {
                return new AiChatContext(contextId);
            }

            try
            {
                var json = File.ReadAllText(savePath);
                var context = JsonUtility.FromJson<AiChatContext>(json);
                if (context == null) context = new AiChatContext(contextId);
                else if (string.IsNullOrEmpty(context.id)) context.id = contextId;
                return context;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AiChatContext] 読み込みに失敗しました。新規作成します: {e.Message}");
                return new AiChatContext(contextId);
            }
        }

        public AgentHistory GetOrCreateHistory(string agentGuid)
        {
            var history = histories.Find(h => h.AgentGuid == agentGuid);
            if (history == null)
            {
                history = new AgentHistory { AgentGuid = agentGuid };
                histories.Add(history);
            }
            return history;
        }

        public void AddActiveAgent(string agentGuid)
        {
            if (!activeAgentGuids.Contains(agentGuid))
            {
                activeAgentGuids.Add(agentGuid);
            }
        }

        public void RemoveActiveAgent(string agentGuid)
        {
            activeAgentGuids.Remove(agentGuid);
        }
    }
}
