using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
namespace UsefulToolkit.Ai
{
    /// <summary>
    /// 特定のコンテキストとエージェントにおける会話状態と通信を管理するクラス。
    /// </summary>
    public sealed class AiChatSession
    {
        private readonly string _contextId;
        private readonly string _agentGuid;
        private readonly IAiClient _client; // IAiClient
        
        private bool _isProcessing;
        private CancellationTokenSource _cts;
        private string _statusText = "Ready";

        public event Action OnStateChanged;
        public event Action OnMessageAdded;

        public bool IsProcessing => _isProcessing;
        public string StatusText => _statusText;
        public IAiClient Client => _client; // IAiClient

        public AiChatSession(string contextId, string agentGuid, IAiClient client)
        {
            _contextId = contextId;
            _agentGuid = agentGuid;
            _client = client;
        }

        public void Cancel()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _statusText = "Cancelled by user.";
                OnStateChanged?.Invoke();
            }
        }

        public async Task SendMessageAsync(string text)
        {
            if (_isProcessing) return;
            if (_client == null)
            {
                Debug.LogError("[AiChatSession] _client is null!");
                return;
            }

            _isProcessing = true;
            _statusText = "AI is thinking...";
            _cts = new CancellationTokenSource();
            OnStateChanged?.Invoke();

            try
            {
                string rawText = await _client.SendMessageAsync(text, _cts.Token);
                AddMessageToHistory("model", rawText); // AIの返答を保存
                
                _statusText = "Ready";
                OnMessageAdded?.Invoke();
                
                try 
                {
                    await CheckAndAutoExecuteCommandsAsync();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AiChatSession] Error in CheckAndAutoExecuteCommandsAsync: {e.Message}\n{e.StackTrace}");
                }
            }
            catch (OperationCanceledException)
            {
                _statusText = "Cancelled.";
            }
            catch (Exception e)
            {
                _statusText = "Error occurred.";
                Debug.LogError($"[AiChatSession] {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                _isProcessing = false;
                _cts?.Dispose();
                _cts = null;
                OnStateChanged?.Invoke();
            }
        }

        /// <param name="messageAlreadySaved">trueの場合、feedbackMessageはすでにhistory.Messagesに保存済みのためAddMessageToHistoryをスキップする（AddMessage経由で呼ばれる場合）</param>
        public async Task RequestAiResponseAfterCommandAsync(string feedbackMessage, bool isInternalCall = false, bool messageAlreadySaved = false)
        {
            if (!isInternalCall && _isProcessing) return;

            _isProcessing = true;
            _statusText = "AI is thinking...";
            _cts = new CancellationTokenSource();
            OnStateChanged?.Invoke();

            try
            {
                // messageAlreadySaved=trueの場合はhistory.Messagesへの保存済みなのでスキップ
                if (!messageAlreadySaved)
                {
                    AddMessageToHistory("user", feedbackMessage); // フィードバック保存
                }

                string rawText = await _client.SendMessageAsync(feedbackMessage, _cts.Token);
                AddMessageToHistory("model", rawText); // AIの返答を保存

                _statusText = "Ready";
                OnMessageAdded?.Invoke();

                await CheckAndAutoExecuteCommandsAsync();
            }
            catch (OperationCanceledException)
            {
                _statusText = "Cancelled.";
            }
            catch (Exception e)
            {
                _statusText = "Error occurred.";
                Debug.LogError($"[AiChatSession] {e.Message}");
            }
            finally
            {
                _isProcessing = false;
                _cts?.Dispose();
                _cts = null;
                OnStateChanged?.Invoke();
            }
        }

        private void AddMessageToHistory(string role, string content)
        {
            var msg = new ChatMessage { Role = role, Content = content };
            var agent = GetAgent();
            if (agent != null)
            {
                AiChatSessionManager.AddMessage(agent, msg);
            }
            OnMessageAdded?.Invoke();
        }

        private AgentData GetAgent()
        {
            string path = AssetDatabase.GUIDToAssetPath(_agentGuid);
            return AssetDatabase.LoadAssetAtPath<AgentData>(path);
        }

        private async Task CheckAndAutoExecuteCommandsAsync()
        {
            var settings = AiChatSettings.Load().ActiveClientSettings;
            if (settings == null || !settings.EnableAutoExecuteCommands) return;

            var agent = GetAgent();
            if (agent == null)
            {
                Debug.LogWarning("[AiChatSession] AgentData not found. Auto-execution aborted.");
                return;
            }

            var parsed = GetParsedHistory();
            var lastMsg = parsed.LastOrDefault(m => m.Role == "model");
            if (lastMsg == null || lastMsg.Commands == null || lastMsg.Commands.Count == 0) return;

            List<string> results = new List<string>();
            bool anyExecuted = false;

            foreach (var cmd in lastMsg.Commands)
            {
                if (cmd != null && cmd.isPending)
                {
                    ExecuteCommandSilent(cmd, agent);
                    string argKey = (cmd.arguments != null && cmd.arguments.Length > 0) ? string.Join(", ", cmd.arguments) : "none";
                    results.Add($"[Command Result: {cmd.name ?? "unknown"}({argKey})]\n{cmd.executionResult}");
                    anyExecuted = true;
                }
            }

            if (anyExecuted)
            {
                string feedback = string.Join("\n\n", results) + "\n\nAll pending commands have been executed. Please proceed.";
                await RequestAiResponseAfterCommandAsync(feedback, true);
            }
        }

        private void ExecuteCommandSilent(CommandInfo info, AgentData agent)
        {
            if (agent == null)
            {
                Debug.LogError("[AiChatSession] ExecuteCommandSilent: agent is null!");
                return;
            }

            info.isPending = false;
            info.isApproved = true;

            object cmdInstance = null;
            if (agent.GetCommands != null) cmdInstance = agent.GetCommands.FirstOrDefault(c => c != null && c.GetType().Name == info.name);
            if (cmdInstance == null && agent.SetCommands != null) cmdInstance = agent.SetCommands.FirstOrDefault(c => c != null && c.GetType().Name == info.name);
            if (cmdInstance == null && agent.ContactCommands != null) cmdInstance = agent.ContactCommands.FirstOrDefault(c => c != null && c.GetType().Name == info.name);

            if (cmdInstance == null)
            {
                info.executionResult = "Error: Command not found.";
            }
            else
            {
                try
                {
                    if (cmdInstance is IGetCommand gc)
                    {
                        // AI から受け取った配列形式の引数(JSON文字列)をそのまま渡す
                        string arg = JsonConvert.SerializeObject(info.arguments ?? new string[0]);
                        info.executionResult = gc.Execute(arg);
                    }
                    else if (cmdInstance is ISetCommand sc)
                    {
                        // SetCommand は常に JSON 配列として渡す（パス直接渡しを禁止し、JSONパース前提にするため）
                        string arg = (info.arguments != null && info.arguments.Length > 0) 
                                     ? JsonConvert.SerializeObject(info.arguments) 
                                     : "";
                        info.executionResult = sc.Execute(arg);
                    }
                    else if (cmdInstance is IContactCommand cc)
                    {
                        // IContactCommand は引数として [targetAgentName, message] を期待する想定
                        string arg1 = (info.arguments != null && info.arguments.Length > 0) ? info.arguments[0] : "";
                        string arg2 = (info.arguments != null && info.arguments.Length > 1) ? info.arguments[1] : "";
                        info.executionResult = cc.Execute(arg1, arg2);
                    }
                }
                catch (Exception e)
                {
                    info.executionResult = $"Error: {e.Message}";
                }
            }

            string argKey = (info.arguments != null && info.arguments.Length > 0) ? string.Join(", ", info.arguments) : "none";
            string feedback = $"[Command Result: {info.name ?? "unknown"}({argKey})]\n{info.executionResult}";
            
            AiChatSessionManager.AddMessage(agent, new ChatMessage { Role = "user", Content = feedback });
        }

        public List<ParsedMessage> GetParsedHistory()
        {
            var rawMessages = _client.ExportMessages();
            var parsed = new List<ParsedMessage>();

            for (int i = 0; i < rawMessages.Count; i++)
            {
                var rm = rawMessages[i];
                if (rm.Role == "system") continue;
                if (rm.Role == "user" && (rm.Content.StartsWith("[Command Result:") || rm.Content.StartsWith("[Command Rejected:"))) continue;

                var msg = new ParsedMessage { Role = rm.Role, Text = rm.Content };
                
                if (rm.Role == "model")
                {
                    try
                    {
                        var data = JsonConvert.DeserializeObject<AiResponse>(rm.Content);
                        if (data != null)
                        {
                            msg.Text = data.message;
                            if (data.commands != null)
                            {
                                foreach (var c in data.commands)
                                {
                                    CheckCommandStatusInHistory(c, rawMessages, i + 1);
                                    msg.Commands.Add(c);
                                }
                            }
                        }
                    }
                    catch { }
                }
                parsed.Add(msg);
            }
            return parsed;
        }

        private void CheckCommandStatusInHistory(CommandInfo cmd, List<ChatMessage> allMessages, int startIndex)
        {
            string argKey = (cmd.arguments != null && cmd.arguments.Length > 0) ? string.Join(", ", cmd.arguments) : "none";
            string searchKey = $"[Command Result: {cmd.name}({argKey})]";
            string rejectKey = $"[Command Rejected: {cmd.name}({argKey})]";

            for (int j = startIndex; j < allMessages.Count; j++)
            {
                var m = allMessages[j];
                if (m.Role == "user")
                {
                    if (m.Content.StartsWith(searchKey))
                    {
                        cmd.isPending = false;
                        cmd.isApproved = true;
                        cmd.executionResult = m.Content.Substring(searchKey.Length).Trim();
                        return;
                    }
                    if (m.Content.StartsWith(rejectKey))
                    {
                        cmd.isPending = false;
                        cmd.isApproved = false;
                        cmd.executionResult = "Rejected by user.";
                        return;
                    }
                }
            }
        }
        
        public void ClearHistory()
        {
            var agent = GetAgent();
            if (agent != null) AiChatSessionManager.ResetSession(agent);
            _statusText = "History cleared.";
            OnMessageAdded?.Invoke();
            OnStateChanged?.Invoke();
        }
    }
}
