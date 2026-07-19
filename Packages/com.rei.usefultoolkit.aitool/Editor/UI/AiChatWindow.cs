using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UsefulToolkit.Framework;

namespace UsefulToolkit.Ai
{
    public sealed class AiChatWindow : EditorWindow
    {
        [MenuItem("UsefulToolkit/AI/AiChat", false, 11)]
        public static void Open()
        {
            var wnd = GetWindow<AiChatWindow>();
            wnd.titleContent = new GUIContent("AiChat");
            wnd.minSize = new Vector2(400, 600);
        }

        [SerializeField] private List<AgentData> _activeAgents = new List<AgentData>();
        [SerializeField] private int _selectedAgentIndex = 0;

        // Renaming state
        private bool _isRenaming;

        // UI Elements
        private ScrollView _chatHistory;
        private TextField _inputField;
        private Button _sendButton;
        private Button _cancelButton;
        private VisualElement _tabContainer;
        private Label _statusLabel;
        private VisualElement _toolbar;

        // Visual Colors
        private readonly Color _rootBg = new Color(0.12f, 0.12f, 0.12f);
        private readonly Color _headerBg = new Color(0.15f, 0.15f, 0.15f);
        private readonly Color _inputAreaBg = new Color(0.18f, 0.18f, 0.18f);
        private readonly Color _userBubbleBg = new Color(0.08f, 0.48f, 0.95f);
        private readonly Color _aiBubbleBg = new Color(0.24f, 0.24f, 0.24f);
        private readonly Color _commandBoxBg = new Color(0.15f, 0.15f, 0.15f);

        private AiChatSession CurrentSession => (_selectedAgentIndex >= 0 && _selectedAgentIndex < _activeAgents.Count)
            ? AiChatSessionManager.GetSession(_activeAgents[_selectedAgentIndex])
            : null;

        private void OnEnable()
        {
            AiChatSessionManager.OnContextChanged += OnContextChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            AiChatSessionManager.OnContextChanged -= OnContextChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            UnsubscribeCurrentSession();
        }

        private void OnUndoRedo() => CreateGUI();

        private void OnContextChanged()
        {
            RefreshAgents();
            BuildTabs();
            RefreshChatUI();
            if (rootVisualElement != null) CreateGUI();
        }

        private void SubscribeToSession(AiChatSession session)
        {
            if (session == null) return;
            session.OnStateChanged += OnSessionStateChanged;
            session.OnMessageAdded += OnSessionMessageAdded;
        }

        private void UnsubscribeCurrentSession()
        {
            var session = CurrentSession;
            if (session == null) return;
            session.OnStateChanged -= OnSessionStateChanged;
            session.OnMessageAdded -= OnSessionMessageAdded;
        }

        private void OnSessionStateChanged()
        {
            // CreateGUI を呼ぶと全UIが再構築されインタラクションが切れる可能性があるため、
            // ステータス更新のみに限定する
            UpdateStatusUI();

            // コマンドカード内のボタンも更新する必要があるため、必要であれば個別に再レンダリングする
            // ここでは簡易的に、ボタン群を探索して状態を更新する
            var buttons = rootVisualElement.Query<Button>().ToList();
            var session = CurrentSession;
            bool enabled = session != null && !session.IsProcessing;

            foreach (var btn in buttons)
            {
                if (btn.text == "Reject" || btn.text == "Approve")
                {
                    btn.SetEnabled(enabled);
                }
            }
        }

        private void OnSessionMessageAdded() => RefreshChatUI();

        private void UpdateStatusUI()
        {
            var session = CurrentSession;
            if (session == null) return;

            _statusLabel.text = session.StatusText;
            bool processing = session.IsProcessing;

            if (_sendButton != null) _sendButton.style.display = processing ? DisplayStyle.None : DisplayStyle.Flex;
            if (_cancelButton != null) _cancelButton.style.display = processing ? DisplayStyle.Flex : DisplayStyle.None;

            SetInputEnabled(!processing);
        }

        public void CreateGUI()
        {
            try
            {
                rootVisualElement.Clear();
                ApplyRootStyle(rootVisualElement);

                RefreshAgents();
                BuildLayout(rootVisualElement);
                RefreshChatUI();
                UpdateStatusUI();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AiChatWindow] CreateGUI Error: {e.Message}\n{e.StackTrace}");
            }

            // Drag & Drop
            UsefulDragAndDropUtility.RegisterDragAndDropCallbacks(rootVisualElement, null, objects =>
            {
                bool added = false;
                foreach (var obj in objects)
                {
                    if (obj is AgentData agent)
                    {
                        AiChatSessionManager.GetSession(agent);
                        added = true;
                    }
                }

                if (added)
                {
                    AiChatSessionManager.SaveAll();
                    OnContextChanged();
                }
            });
        }

        private void RefreshAgents()
        {
            _activeAgents.Clear();
            var context = AiChatSessionManager.Context;
            if (context == null) return;

            foreach (var guid in context.ActiveAgentGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var agent = AssetDatabase.LoadAssetAtPath<AgentData>(path);
                if (agent != null) _activeAgents.Add(agent);
            }

            if (_selectedAgentIndex >= _activeAgents.Count)
                _selectedAgentIndex = Mathf.Max(0, _activeAgents.Count - 1);
        }

        private void BuildLayout(VisualElement root)
        {
            // Toolbar
            _toolbar = new VisualElement();
            _toolbar.style.flexDirection = FlexDirection.Row;
            _toolbar.style.paddingLeft = 10;
            _toolbar.style.paddingRight = 10;
            _toolbar.style.paddingTop = 5;
            _toolbar.style.paddingBottom = 5;
            _toolbar.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            _toolbar.style.borderBottomWidth = 1;
            _toolbar.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);

            var metadata = AiChatSessionManager.Metadata;
            var currentItem = metadata.Contexts.Find(c => c.Id == metadata.CurrentContextId);

            if (_isRenaming && currentItem != null)
            {
                var renameField = new TextField { value = currentItem.Name };
                renameField.style.flexGrow = 1;
                renameField.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return)
                    {
                        AiChatSessionManager.RenameContext(currentItem.Id, renameField.value);
                        _isRenaming = false;
                        CreateGUI();
                    }
                    else if (evt.keyCode == KeyCode.Escape)
                    {
                        _isRenaming = false;
                        CreateGUI();
                    }
                });
                _toolbar.Add(renameField);
                var saveBtn = new Button(() =>
                {
                    AiChatSessionManager.RenameContext(currentItem.Id, renameField.value);
                    _isRenaming = false;
                    CreateGUI();
                }) { text = "Save" };
                ApplyHeaderButtonStyle(saveBtn);
                _toolbar.Add(saveBtn);
                var cancelBtn = new Button(() =>
                {
                    _isRenaming = false;
                    CreateGUI();
                }) { text = "Cancel" };
                ApplyHeaderButtonStyle(cancelBtn);
                _toolbar.Add(cancelBtn);
                renameField.Focus();
            }
            else
            {
                var contextPopup = new UnityEngine.UIElements.PopupField<AiChatContextItem>("Context",
                    metadata.Contexts, currentItem ?? (metadata.Contexts.Count > 0 ? metadata.Contexts[0] : null));
                contextPopup.style.flexGrow = 1;
                contextPopup.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue != null) AiChatSessionManager.SwitchContext(evt.newValue.Id);
                });
                _toolbar.Add(contextPopup);

                var renameBtn = new Button(() =>
                {
                    _isRenaming = true;
                    CreateGUI();
                }) { text = "✎" };
                ApplyHeaderButtonStyle(renameBtn);
                _toolbar.Add(renameBtn);

                var newChatBtn = new Button(() =>
                {
                    AiChatSessionManager.CreateNewContext($"Chat {metadata.Contexts.Count + 1}");
                }) { text = "+" };
                ApplyHeaderButtonStyle(newChatBtn);
                _toolbar.Add(newChatBtn);

                var deleteChatBtn = new Button(() =>
                {
                    if (metadata.Contexts.Count > 1 && EditorUtility.DisplayDialog("Delete Chat",
                            $"Are you sure you want to delete '{currentItem?.Name}'?", "Delete", "Cancel"))
                        AiChatSessionManager.DeleteContext(metadata.CurrentContextId);
                }) { text = "-" };
                ApplyHeaderButtonStyle(deleteChatBtn);
                _toolbar.Add(deleteChatBtn);
            }

            root.Add(_toolbar);

            // Header
            var header = new VisualElement();
            header.style.flexShrink = 0;
            ApplyHeaderStyle(header);
            header.Add(new Label("AI CHAT")
                { style = { fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold, color = Color.white } });

            var headerButtons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var agentsBtn = new Button(() =>
            {
                var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Code/Editor/AiChat/Agents/Data");
                if (folder != null)
                {
                    Selection.activeObject = folder;
                    EditorGUIUtility.PingObject(folder);
                    EditorUtility.FocusProjectWindow();
                }
            }) { text = "Agents" };
            ApplyHeaderButtonStyle(agentsBtn);
            headerButtons.Add(agentsBtn);

            var clearBtn = new Button(OnClearClicked) { text = "Clear" };
            ApplyHeaderButtonStyle(clearBtn);
            headerButtons.Add(clearBtn);

            var settingsBtn = new Button(UsefulToolkitSettings.ShowWindow) { text = "Settings" };
            ApplyHeaderButtonStyle(settingsBtn);
            headerButtons.Add(settingsBtn);

            header.Add(headerButtons);
            root.Add(header);

            // Tabs
            _tabContainer = new VisualElement();
            _tabContainer.style.flexShrink = 0;
            ApplyTabContainerStyle(_tabContainer);
            root.Add(_tabContainer);
            BuildTabs();

            // Status Bar
            _statusLabel = new Label("Ready");
            _statusLabel.style.flexShrink = 0;
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.paddingLeft = 10;
            _statusLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            root.Add(_statusLabel);

            // Chat Area
            _chatHistory = new ScrollView(ScrollViewMode.Vertical);
            _chatHistory.style.flexGrow = 1;
            _chatHistory.style.flexShrink = 1;
            _chatHistory.style.paddingLeft = 15;
            _chatHistory.style.paddingRight = 15;
            _chatHistory.style.paddingTop = 15;
            _chatHistory.style.paddingBottom = 15;
            root.Add(_chatHistory);

            // Input Area
            var inputArea = new VisualElement();
            inputArea.style.flexShrink = 0;
            ApplyInputAreaStyle(inputArea);

            var inputContainer = new VisualElement
                { style = { flexDirection = FlexDirection.Row, flexGrow = 1, flexShrink = 1 } };
            _inputField = new TextField { multiline = true };
            _inputField.style.flexGrow = 1;
            _inputField.style.maxHeight = 150;
            _inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown);
            ApplyTextFieldStyle(_inputField);
            inputContainer.Add(_inputField);
            inputArea.Add(inputContainer);

            _sendButton = new Button(OnSendClicked) { text = "Send" };
            ApplySendButtonStyle(_sendButton);
            inputArea.Add(_sendButton);

            _cancelButton = new Button(OnCancelClicked) { text = "Cancel" };
            ApplyCancelButtonStyle(_cancelButton);
            _cancelButton.style.display = DisplayStyle.None;
            inputArea.Add(_cancelButton);

            root.Add(inputArea);
        }

        private void BuildTabs()
        {
            if (_tabContainer == null) return;
            _tabContainer.Clear();
            for (int i = 0; i < _activeAgents.Count; i++)
            {
                int index = i;
                var agent = _activeAgents[i];
                bool isSelected = _selectedAgentIndex == i;

                var tab = new Button(() =>
                {
                    UnsubscribeCurrentSession();
                    _selectedAgentIndex = index;
                    SubscribeToSession(CurrentSession);
                    BuildTabs();
                    RefreshChatUI();
                    UpdateStatusUI();
                }) { text = agent.Name };

                tab.style.backgroundColor = isSelected ? _rootBg : new Color(0.2f, 0.2f, 0.2f);
                tab.style.color = isSelected ? Color.cyan : Color.gray;
                if (isSelected) tab.style.unityFontStyleAndWeight = FontStyle.Bold;
                ApplyTabStyle(tab);
                _tabContainer.Add(tab);
            }

            // 初回購読
            SubscribeToSession(CurrentSession);
        }

        private void RefreshChatUI()
        {
            if (_chatHistory == null) return;
            _chatHistory.Clear();
            var session = CurrentSession;
            if (session == null) return;

            var agent = _activeAgents[_selectedAgentIndex];
            var parsedHistory = session.GetParsedHistory();

            foreach (var msg in parsedHistory) AddMessageToUI(msg, agent);
            ScrollToBottom();
        }

        private void AddMessageToUI(ParsedMessage msg, AgentData agent)
        {
            bool isUser = msg.Role == "user";
            var container = new VisualElement { style = { flexDirection = FlexDirection.Column, marginBottom = 15 } };
            var bubble = new VisualElement
            {
                style =
                {
                    paddingTop = 10, paddingBottom = 10, paddingLeft = 14, paddingRight = 14, borderTopLeftRadius = 12,
                    borderTopRightRadius = 12, borderBottomLeftRadius = 12, borderBottomRightRadius = 12,
                    maxWidth = Length.Percent(85)
                }
            };

            if (isUser)
            {
                bubble.style.alignSelf = Align.FlexEnd;
                bubble.style.backgroundColor = _userBubbleBg;
                bubble.style.borderBottomRightRadius = 2;
            }
            else
            {
                bubble.style.alignSelf = Align.FlexStart;
                bubble.style.backgroundColor = _aiBubbleBg;
                bubble.style.borderBottomLeftRadius = 2;
            }

            var senderRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, alignItems = Align.Center,
                    marginBottom = 4
                }
            };
            senderRow.Add(new Label(isUser ? "You" : agent.Name)
            {
                style = { fontSize = 10, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.7f, 0.7f, 0.7f) }
            });
            var copyBtn = new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = msg.Text;
                _statusLabel.text = "Copied!";
            }) { text = "Copy" };
            ApplyCopyButtonStyle(copyBtn);
            senderRow.Add(copyBtn);
            bubble.Add(senderRow);

            var messageField = new TextField { value = msg.Text, isReadOnly = true, multiline = true };
            ApplyMessageFieldStyle(messageField);
            bubble.Add(messageField);

            if (!isUser && msg.Commands.Count > 0)
                foreach (var cmd in msg.Commands)
                    bubble.Add(CreateCommandCard(cmd, agent));

            container.Add(bubble);
            _chatHistory.Add(container);
        }

        private VisualElement CreateCommandCard(CommandInfo cmd, AgentData agent)
        {
            var card = new VisualElement();
            ApplyCommandCardStyle(card);

            var titleRow = new VisualElement
                { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            titleRow.Add(new Image
            {
                image = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image,
                style = { width = 16, height = 16, marginRight = 5 }
            });
            titleRow.Add(new Label(cmd.name)
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11, color = new Color(0.3f, 0.7f, 1.0f) }
            });

            if (!cmd.isPending)
            {
                titleRow.Add(new VisualElement { style = { flexGrow = 1 } });
                titleRow.Add(new Image
                {
                    image = EditorGUIUtility
                        .IconContent(cmd.isApproved ? "Collab.BuildSucceeded" : "Collab.BuildFailed").image,
                    style = { width = 16, height = 16 }
                });
            }

            card.Add(titleRow);

            if (cmd.arguments != null)
                foreach (var arg in cmd.arguments)
                    card.Add(new Label($" • {arg}")
                    {
                        style = { fontSize = 10, color = new Color(0.7f, 0.7f, 0.7f), whiteSpace = WhiteSpace.Normal }
                    });

            if (cmd.isPending)
            {
                var settings = AiChatSettings.Load().ActiveClientSettings;
                if (!settings.EnableAutoExecuteCommands)
                {
                    var btnRow = new VisualElement
                    {
                        style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 8 }
                    };
                    var rejectBtn = new Button(() => RejectCommand(cmd)) { text = "Reject" };
                    ApplyCommandButtonStyle(rejectBtn, new Color(0.4f, 0.2f, 0.2f));
                    btnRow.Add(rejectBtn);
                    var approveBtn = new Button(() => ExecuteCommand(cmd)) { text = "Approve" };
                    ApplyCommandButtonStyle(approveBtn, new Color(0.2f, 0.4f, 0.2f));
                    btnRow.Add(approveBtn);

                    // ボタンの活性化状態を明示的に設定
                    bool isProcessing = CurrentSession.IsProcessing;
                    rejectBtn.SetEnabled(!isProcessing);
                    approveBtn.SetEnabled(!isProcessing);

                    card.Add(btnRow);
                }
            }
            else if (!string.IsNullOrEmpty(cmd.executionResult))
            {
                card.Add(new Label("Result:")
                    { style = { fontSize = 9, marginTop = 5, unityFontStyleAndWeight = FontStyle.Bold } });
                var resultBox = new Label(cmd.executionResult);
                ApplyResultBoxStyle(resultBox);
                card.Add(resultBox);
            }

            return card;
        }

        private void ExecuteCommand(CommandInfo info)
        {
            var session = CurrentSession;
            if (session == null || session.IsProcessing) return;

            info.isPending = false;
            info.isApproved = true;

            var agent = _activeAgents[_selectedAgentIndex];
            object cmdInstance = null;
            if (agent.GetCommands != null)
                cmdInstance = agent.GetCommands.FirstOrDefault(c => c.GetType().Name == info.name);
            if (cmdInstance == null && agent.SetCommands != null)
                cmdInstance = agent.SetCommands.FirstOrDefault(c => c.GetType().Name == info.name);

            if (cmdInstance != null)
            {
                try
                {
                    string arg = (info.arguments != null && info.arguments.Length > 0) ? info.arguments[0] : "";
                    if (cmdInstance is IGetCommand gc) info.executionResult = gc.Execute(arg);
                    else if (cmdInstance is ISetCommand sc) info.executionResult = sc.Execute(arg);
                }
                catch (Exception e)
                {
                    info.executionResult = $"Error: {e.Message}";
                }
            }
            else info.executionResult = "Error: Command not found.";

            string argKey = (info.arguments != null && info.arguments.Length > 0)
                ? string.Join(", ", info.arguments)
                : "none";
            string feedback = $"[Command Result: {info.name}({argKey})]\n{info.executionResult}";
            _ = session.RequestAiResponseAfterCommandAsync(feedback);
        }

        private void RejectCommand(CommandInfo info)
        {
            var session = CurrentSession;
            if (session == null || session.IsProcessing) return;

            info.isPending = false;
            info.isApproved = false;
            info.executionResult = "User rejected the execution.";

            string argKey = (info.arguments != null && info.arguments.Length > 0)
                ? string.Join(", ", info.arguments)
                : "none";
            string feedback = $"[Command Rejected: {info.name}({argKey})]\nUser did not approve the execution.";
            _ = session.RequestAiResponseAfterCommandAsync(feedback);
        }

        private void OnInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return && !evt.shiftKey)
            {
                OnSendClicked();
                evt.StopPropagation();
            }
        }

        private async void OnSendClicked()
        {
            var session = CurrentSession;
            if (session == null || session.IsProcessing) return;

            string text = _inputField.value.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _inputField.value = "";

            // 履歴への追加はSession側で行うように変更したため、送信指示のみ行う
            // ただし、UIに即座に反映させるために AddMessage してから送信する
            var agent = _activeAgents[_selectedAgentIndex];
            AiChatSessionManager.AddMessage(agent, new ChatMessage { Role = "user", Content = text });

            RefreshChatUI();
            await session.SendMessageAsync(text);
        }

        private void OnCancelClicked() => CurrentSession?.Cancel();

        private void SetInputEnabled(bool enabled)
        {
            if (_inputField != null) _inputField.SetEnabled(enabled);
            if (_sendButton != null) _sendButton.SetEnabled(enabled);
        }

        private void OnClearClicked() => CurrentSession?.ClearHistory();

        private void ScrollToBottom() => _chatHistory.RegisterCallback<GeometryChangedEvent>(OnHistoryGeometryChanged);

        private void OnHistoryGeometryChanged(GeometryChangedEvent evt)
        {
            _chatHistory.UnregisterCallback<GeometryChangedEvent>(OnHistoryGeometryChanged);
            _chatHistory.scrollOffset = new Vector2(0, _chatHistory.contentContainer.layout.height);
        }

        #region Styling

        private void ApplyRootStyle(VisualElement e)
        {
            e.style.backgroundColor = _rootBg;
            e.style.flexDirection = FlexDirection.Column;
        }

        private void ApplyHeaderStyle(VisualElement e)
        {
            e.style.flexDirection = FlexDirection.Row;
            e.style.justifyContent = Justify.SpaceBetween;
            e.style.alignItems = Align.Center;
            e.style.paddingTop = 10;
            e.style.paddingBottom = 10;
            e.style.paddingLeft = 15;
            e.style.paddingRight = 15;
            e.style.backgroundColor = _headerBg;
            e.style.borderBottomWidth = 1;
            e.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);
        }

        private void ApplyHeaderButtonStyle(Button e)
        {
            e.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            e.style.color = new Color(0.8f, 0.8f, 0.8f);
            e.style.borderTopWidth = 0;
            e.style.borderBottomWidth = 0;
            e.style.borderLeftWidth = 0;
            e.style.borderRightWidth = 0;
            e.style.borderTopLeftRadius = 4;
            e.style.borderTopRightRadius = 4;
            e.style.borderBottomLeftRadius = 4;
            e.style.borderBottomRightRadius = 4;
            e.style.paddingLeft = 8;
            e.style.paddingRight = 8;
            e.style.marginLeft = 5;
        }

        private void ApplyTabContainerStyle(VisualElement e)
        {
            e.style.flexDirection = FlexDirection.Row;
            e.style.paddingLeft = 15;
            e.style.paddingTop = 5;
            e.style.backgroundColor = _headerBg;
        }

        private void ApplyTabStyle(Button tab)
        {
            tab.style.borderTopWidth = 0;
            tab.style.borderBottomWidth = 0;
            tab.style.borderLeftWidth = 0;
            tab.style.borderRightWidth = 0;
            tab.style.borderTopLeftRadius = 0;
            tab.style.borderTopRightRadius = 0;
            tab.style.borderBottomLeftRadius = 0;
            tab.style.borderBottomRightRadius = 0;
            tab.style.marginRight = 5;
            tab.style.paddingLeft = 10;
            tab.style.paddingRight = 10;
            tab.style.height = 25;
        }

        private void ApplyInputAreaStyle(VisualElement e)
        {
            e.style.flexDirection = FlexDirection.Row;
            e.style.paddingTop = 15;
            e.style.paddingBottom = 15;
            e.style.paddingLeft = 15;
            e.style.paddingRight = 15;
            e.style.backgroundColor = _inputAreaBg;
            e.style.borderTopWidth = 1;
            e.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f);
        }

        private void ApplyTextFieldStyle(TextField e)
        {
            var textInput = e.Q("unity-text-input");
            if (textInput != null)
            {
                textInput.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
                textInput.style.borderTopLeftRadius = 8;
                textInput.style.borderTopRightRadius = 8;
                textInput.style.borderBottomLeftRadius = 8;
                textInput.style.borderBottomRightRadius = 8;
                textInput.style.paddingLeft = 10;
                textInput.style.paddingRight = 10;
                textInput.style.paddingTop = 8;
                textInput.style.paddingBottom = 8;
                textInput.style.color = Color.white;
                textInput.style.borderTopWidth = 0;
                textInput.style.borderBottomWidth = 0;
                textInput.style.borderLeftWidth = 0;
                textInput.style.borderRightWidth = 0;
            }
        }

        private void ApplySendButtonStyle(Button e)
        {
            e.style.width = 80;
            e.style.height = 36;
            e.style.marginLeft = 12;
            e.style.backgroundColor = new Color(0.15f, 0.4f, 0.8f);
            e.style.color = Color.white;
            e.style.borderTopLeftRadius = 8;
            e.style.borderTopRightRadius = 8;
            e.style.borderBottomLeftRadius = 8;
            e.style.borderBottomRightRadius = 8;
            e.style.unityFontStyleAndWeight = FontStyle.Bold;
            e.style.borderTopWidth = 0;
            e.style.borderBottomWidth = 0;
            e.style.borderLeftWidth = 0;
            e.style.borderRightWidth = 0;
        }

        private void ApplyCancelButtonStyle(Button e)
        {
            e.style.width = 80;
            e.style.height = 36;
            e.style.marginLeft = 12;
            e.style.backgroundColor = new Color(0.7f, 0.2f, 0.2f);
            e.style.color = Color.white;
            e.style.borderTopLeftRadius = 8;
            e.style.borderTopRightRadius = 8;
            e.style.borderBottomLeftRadius = 8;
            e.style.borderBottomRightRadius = 8;
            e.style.unityFontStyleAndWeight = FontStyle.Bold;
            e.style.borderTopWidth = 0;
            e.style.borderBottomWidth = 0;
            e.style.borderLeftWidth = 0;
            e.style.borderRightWidth = 0;
        }

        private void ApplyCommandButtonStyle(Button e, Color color)
        {
            e.style.backgroundColor = color;
            e.style.color = Color.white;
            e.style.fontSize = 10;
            e.style.paddingLeft = 8;
            e.style.paddingRight = 8;
            e.style.height = 22;
            e.style.marginLeft = 5;
            e.style.borderTopWidth = 0;
            e.style.borderBottomWidth = 0;
            e.style.borderLeftWidth = 0;
            e.style.borderRightWidth = 0;
            e.style.borderTopLeftRadius = 4;
            e.style.borderTopRightRadius = 4;
            e.style.borderBottomLeftRadius = 4;
            e.style.borderBottomRightRadius = 4;
        }

        private void ApplyCopyButtonStyle(Button e)
        {
            e.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            e.style.color = new Color(0.6f, 0.6f, 0.6f);
            e.style.fontSize = 9;
            e.style.paddingLeft = 4;
            e.style.paddingRight = 4;
            e.style.height = 16;
            e.style.borderTopWidth = 0;
            e.style.borderBottomWidth = 0;
            e.style.borderLeftWidth = 0;
            e.style.borderRightWidth = 0;
            e.style.borderTopLeftRadius = 2;
            e.style.borderTopRightRadius = 2;
            e.style.borderBottomLeftRadius = 2;
            e.style.borderBottomRightRadius = 2;
        }

        private void ApplyMessageFieldStyle(TextField e)
        {
            e.style.fontSize = 13;
            e.style.whiteSpace = WhiteSpace.Normal;
            var textInput = e.Q("unity-text-input");
            if (textInput != null)
            {
                textInput.style.backgroundColor = new Color(0, 0, 0, 0);
                textInput.style.borderTopWidth = 0;
                textInput.style.borderBottomWidth = 0;
                textInput.style.borderLeftWidth = 0;
                textInput.style.borderRightWidth = 0;
                textInput.style.paddingLeft = 0;
                textInput.style.paddingRight = 0;
                textInput.style.paddingTop = 0;
                textInput.style.paddingBottom = 0;
                textInput.style.color = Color.white;
            }
        }

        private void ApplyCommandCardStyle(VisualElement card)
        {
            card.style.marginTop = 8;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.backgroundColor = _commandBoxBg;
            card.style.borderTopLeftRadius = 6;
            card.style.borderTopRightRadius = 6;
            card.style.borderBottomLeftRadius = 6;
            card.style.borderBottomRightRadius = 6;
        }

        private void ApplyResultBoxStyle(Label box)
        {
            box.style.fontSize = 10;
            box.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            box.style.paddingTop = 4;
            box.style.paddingBottom = 4;
            box.style.paddingLeft = 6;
            box.style.paddingRight = 6;
            box.style.marginTop = 2;
            box.style.whiteSpace = WhiteSpace.Normal;
        }

        #endregion
    }
}