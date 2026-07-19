using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Ai
{
    /// <summary>
    /// エージェントごとのクライアント・セッションおよび会話履歴の永続化を管理するマネージャー。
    /// </summary>
    [InitializeOnLoad]
    public static class AiChatSessionManager
    {
        private static AiChatContextMetadata _metadata;
        private static AiChatContext _context;

        private static readonly Dictionary<(string contextId, string agentGuid), AiChatSession> _sessions =
            new Dictionary<(string, string), AiChatSession>();

        private static List<Type> _availableClientTypes;

        public static event Action OnContextChanged;

        public static List<Type> GetAvailableClientTypes()
        {
            if (_availableClientTypes == null)
            {
                _availableClientTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .Where(t => t.IsSubclassOf(typeof(AiClientBase)) && !t.IsAbstract)
                    .ToList();
            }

            return _availableClientTypes;
        }

        /// <summary>
        /// 全てのコンテキストのメタデータ。
        /// </summary>
        public static AiChatContextMetadata Metadata
        {
            get
            {
                if (_metadata == null) _metadata = AiChatContextMetadata.Load();
                return _metadata;
            }
        }

        /// <summary>
        /// 現在の会話コンテキスト（履歴データ）。
        /// </summary>
        public static AiChatContext Context
        {
            get
            {
                if (_context == null) EnsureCurrentContext();
                return _context;
            }
        }

        static AiChatSessionManager()
        {
            EnsureCurrentContext();
            EditorApplication.quitting += SaveAll;
        }

        private static void EnsureCurrentContext()
        {
            _metadata = AiChatContextMetadata.Load();
            if (string.IsNullOrEmpty(_metadata.CurrentContextId))
            {
                if (_metadata.Contexts.Count > 0)
                {
                    _metadata.CurrentContextId = _metadata.Contexts[0].Id;
                }
                else
                {
                    CreateNewContext("Default Chat");
                }
            }

            _context = AiChatContext.Load(_metadata.CurrentContextId);
        }

        /// <summary>
        /// 現在の状態を保存します。
        /// </summary>
        public static void SaveAll()
        {
            if (_context == null) return;

            foreach (var pair in _sessions)
            {
                if (pair.Key.contextId == _metadata.CurrentContextId)
                {
                    var history = _context.GetOrCreateHistory(pair.Key.agentGuid);
                    history.Messages = pair.Value.Client.ExportMessages();
                }
            }

            _context.Save();
            _metadata?.Save();
        }

        /// <summary>
        /// コンテキストを切り替えます。
        /// </summary>
        public static void SwitchContext(string contextId)
        {
            if (_metadata.CurrentContextId == contextId) return;

            SaveAll();

            _metadata.CurrentContextId = contextId;
            _metadata.Save();

            _context = AiChatContext.Load(contextId);
            OnContextChanged?.Invoke();
        }

        public static void CreateNewContext(string name)
        {
            string id = Guid.NewGuid().ToString();
            var item = new AiChatContextItem(id, name);

            _metadata ??= AiChatContextMetadata.Load();
            _metadata.Contexts.Insert(0, item);
            _metadata.CurrentContextId = id;
            _metadata.Save();

            _context = new AiChatContext(id);
            _context.Save();

            OnContextChanged?.Invoke();
        }

        public static void RenameContext(string id, string newName)
        {
            var item = Metadata.Contexts.Find(c => c.Id == id);
            if (item == null) return;

            item.Name = newName;
            _metadata.Save();
            OnContextChanged?.Invoke();
        }

        public static void DeleteContext(string id)
        {
            var item = _metadata.Contexts.Find(c => c.Id == id);
            if (item == null) return;

            _metadata.Contexts.Remove(item);

            string path = $"UserSettings/UsefulToolkit/AiChat/Contexts/{id}.json";
            if (File.Exists(path)) File.Delete(path);

            if (_metadata.CurrentContextId == id)
            {
                _metadata.CurrentContextId = "";
                EnsureCurrentContext();
            }
            else
            {
                _metadata.Save();
            }

            var keysToRemove = _sessions.Keys.Where(k => k.contextId == id).ToList();
            foreach (var key in keysToRemove) _sessions.Remove(key);

            OnContextChanged?.Invoke();
        }

        public static AiChatSession GetSession(AgentData agent)
        {
            if (agent == null) return null;

            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(agent));
            if (string.IsNullOrEmpty(guid)) return null;

            string contextId = Metadata.CurrentContextId;
            var key = (contextId, guid);

            if (!_sessions.TryGetValue(key, out var session))
            {
                var chatSettings = AiChatSettings.Load();
                var settings = chatSettings.ActiveClientSettings;
                if (settings == null)
                {
                    throw new InvalidOperationException(
                        "Active client settings are not configured. Please open AI Settings.");
                }

                var apiKey = settings.ApiKey;
                var model = settings.ModelName;
                var timeout = settings.TimeoutSeconds;
                var suffix = settings.SystemPromptSuffix;

                var allAgents = new List<AgentData>();
                foreach (var g in Context.ActiveAgentGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    var a = AssetDatabase.LoadAssetAtPath<AgentData>(path);
                    if (a != null) allAgents.Add(a);
                }

                if (!allAgents.Contains(agent)) allAgents.Add(agent);

                string fullSystemPrompt = BuildFullSystemPrompt(agent, allAgents, suffix);

                // ファクトリ経由でのクライアント生成（型名を利用）
                IAiClient client = AiClientFactory.CreateClient(settings.ClientTypeFullName, apiKey, model,
                    fullSystemPrompt, timeout);

                // セッション生成時に永続化された最新の履歴を強制ロード
                var history = Context.GetOrCreateHistory(guid);
                if (history.Messages != null && history.Messages.Count > 0)
                {
                    client.ImportMessages(history.Messages);
                }

                session = new AiChatSession(contextId, guid, client);
                _sessions[key] = session;
                Context.AddActiveAgent(guid);
            }
            else
            {
                // セッションがメモリ上にあった場合でも、念のため永続化履歴と再同期
                var history = Context.GetOrCreateHistory(guid);
                session.Client.ImportMessages(history.Messages);
            }

            return session;
        }

        public static IAiClient GetAiClient(AgentData agent) => GetSession(agent)?.Client;

        private static string BuildFullSystemPrompt(AgentData agent, List<AgentData> allAgents, string promptSuffix)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Your name is {agent.Name}.");
            sb.AppendLine($"Your role is {agent.Role}.");
            sb.AppendLine();

            if (allAgents != null && allAgents.Count > 1)
            {
                sb.AppendLine("## Context: Team Members");
                sb.AppendLine(
                    "You are part of a team. Here are the other members in this context and their capabilities (command names only):");
                foreach (var other in allAgents)
                {
                    if (other == agent) continue;
                    sb.AppendLine($"- {other.Name}: {other.Role}");
                    var cmdNames = new List<string>();
                    if (other.GetCommands != null)
                        cmdNames.AddRange(other.GetCommands.Where(c => c != null).Select(c => c.GetType().Name));
                    if (other.SetCommands != null)
                        cmdNames.AddRange(other.SetCommands.Where(c => c != null).Select(c => c.GetType().Name));
                    if (other.ContactCommands != null)
                        cmdNames.AddRange(other.ContactCommands.Where(c => c != null).Select(c => c.GetType().Name));
                    if (cmdNames.Count > 0) sb.AppendLine($"  Capabilities: {string.Join(", ", cmdNames)}");
                }

                sb.AppendLine();
            }

            sb.AppendLine("## Your Instructions");
            sb.AppendLine(agent.SystemPrompt);
            sb.AppendLine(
                "NOTE: When delegating tasks to team members, ONLY describe the work to be done. Do NOT instruct them on which specific commands to use; let them decide the best way to accomplish the task based on their own capabilities.");
            sb.AppendLine();
            sb.AppendLine("## Available Commands");
            sb.AppendLine(
                "You can use the following commands by including them in the 'commands' field of your JSON response.");
            sb.AppendLine(
                "IMPORTANT: You MUST ONLY use the commands listed below. If you need to perform an action for which you do not have the command, you MUST delegate the task to an appropriate team member using your available CONTACT commands.");

            if (agent.GetCommands != null)
                foreach (var cmd in agent.GetCommands)
                    if (cmd != null)
                        sb.AppendLine($"- {cmd.GetType().Name}: (GET) {cmd.Description ?? ""}");
            if (agent.SetCommands != null)
                foreach (var cmd in agent.SetCommands)
                    if (cmd != null)
                        sb.AppendLine($"- {cmd.GetType().Name}: (SET) {cmd.Description ?? ""}");
            if (agent.ContactCommands != null)
                foreach (var cmd in agent.ContactCommands)
                    if (cmd != null)
                        sb.AppendLine($"- {cmd.GetType().Name}: (CONTACT) {cmd.Description ?? ""}");

            if (!string.IsNullOrEmpty(promptSuffix))
            {
                sb.AppendLine();
                sb.AppendLine("### Personal Instructions");
                sb.AppendLine(promptSuffix);
            }

            string prompt = sb.ToString();
            return prompt;
        }

        public static async void AddMessage(AgentData agent, ChatMessage message)
        {
            if (agent == null || message == null) return;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(agent));
            if (string.IsNullOrEmpty(guid)) return;

            // 永続層（history.Messages）に先に保存する。
            // GetSession が ImportMessages(history.Messages) で conversation を再構築するため、
            // history.Messages が必ず最新状態でなければならない。
            var history = Context.GetOrCreateHistory(guid);
            history.Messages.Add(message);
            _context.Save();

            // セッションをロード（ImportMessages により conversation に新メッセージが反映される）
            var session = GetSession(agent);
            if (session == null)
            {
                Debug.LogError($"[AiChatSessionManager] Failed to load session for {agent.Name}");
                return;
            }

            if (session.IsProcessing)
            {
                Debug.LogWarning(
                    $"[AiChatSessionManager] {agent.Name} is already processing. Message saved to history only.");
                return;
            }

            Debug.Log($"[AiChatSessionManager] Triggering API for {agent.Name}: {message.Content}");

            // messageAlreadySaved: true → history.Messages に保存済みなので
            // RequestAiResponseAfterCommandAsync 内部の AddMessageToHistory("user") をスキップし二重保存を防ぐ。
            await session.RequestAiResponseAfterCommandAsync(message.Content, isInternalCall: false,
                messageAlreadySaved: true);
        }

        public static void ResetSession(AgentData agent)
        {
            if (agent == null) return;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(agent));
            if (string.IsNullOrEmpty(guid)) return;

            string contextId = Metadata.CurrentContextId;
            if (_sessions.TryGetValue((contextId, guid), out var session))
            {
                session.Client.ClearConversation();
            }

            var history = _context.GetOrCreateHistory(guid);
            history.Messages.Clear();
            _context.Save();
        }
    }
}