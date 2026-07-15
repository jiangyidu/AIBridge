using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LitJson;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>
    /// Unity Editor AI 助手窗口。
    /// 纯 C# 版本：UI 只负责配置与展示；可恢复 Agent 状态机独立于窗口运行，
    /// 并将续跑状态持久化到 Library/AIBridge。
    /// </summary>
    public partial class AIAssistantWindow : EditorWindow
    {
        private enum LLMMode { RemoteAPI = 0, Ollama = 1 }
        private enum APIProvider
        {
            DeepSeek = 0, MiniMax = 1, TongyiQianwen = 2,
            Kimi = 3, ZhipuGLM = 4, Gemini = 5, Claude = 6, Custom = 7
        }

        [Serializable]
        private class ChatMessage
        {
            public string role;
            public string content;
            public string reasoning_content;
            public string time;
        }

        private static readonly string[] ProviderNames =
        {
            "DeepSeek", "MiniMax", "通义千问", "Kimi", "智谱 GLM", "Gemini", "Claude", "自定义"
        };

        private static readonly string[] ProviderURLs =
        {
            "https://api.deepseek.com",
            "https://api.minimax.chat/v1",
            "https://dashscope.aliyuncs.com/compatible-mode/v1",
            "https://api.moonshot.cn/v1",
            "https://open.bigmodel.cn/api/paas/v4",
            "https://generativelanguage.googleapis.com/v1beta/openai",
            "https://api.anthropic.com",
            ""
        };

        private static readonly string[] ProviderModels =
        {
            "deepseek-v4-pro",
            "MiniMax-Text-01",
            "qwen-plus",
            "moonshot-v1-8k",
            "glm-4",
            "gemini-2.0-flash",
            "claude-3-5-sonnet-20241022",
            ""
        };

        private string[] GetLocalizedProviderNames()
        {
            return new string[]
            {
                "DeepSeek",
                "MiniMax",
                AgentLocalization.Get("provider_tongyi", "通义千问"),
                "Kimi",
                AgentLocalization.Get("provider_zhipu", "智谱 GLM"),
                "Gemini",
                "Claude",
                AgentLocalization.Get("provider_custom", "自定义")
            };
        }

        private LLMMode _mode = LLMMode.RemoteAPI;
        private APIProvider _provider = APIProvider.DeepSeek;
        private string _apiKey = "";
        private bool _apiKeyNeedsReentry;
        private string _baseUrl = "";
        private string _modelName = "";
        private string _ollamaUrl = "http://localhost:11434";
        private string _ollamaModel = "llama3";
        private string _userSystemPrompt = "";
        private int _maxSteps = 30;
        private bool _allowGeneratedCodeExecution = false;

        private List<ChatMessage> _messages = new List<ChatMessage>();
        private string _inputText = "";
        private Vector2 _chatScroll;
        private Vector2 _cmdScroll;
        private bool _showConfig = true;
        private bool _showCmdPanel = true;
        private bool _showReasoning = true;
        private bool _isWaiting = false;
        private string _agentStepStatus = null;
        private int _agentStepCount = 0;
        private string _activeSessionId = null;
        private DateTime _lastHistoryLoad = DateTime.MinValue;

        private string[] _cmdArgs = new string[0];
        private string[] _cmdArgValues = new string[0];
        private string _selectedCmdKey = null;
        private string _cmdSearchText = "";
        private string _cmdCategoryFilter = "All";

        private static string ProjectPath
        {
            get { return Path.GetDirectoryName(Application.dataPath); }
        }

        private static string HistoryPath
        {
            get { return AgentStateStore.HistoryPath; }
        }

        [MenuItem("AIBridge/AI Assistant")]
        public static void ShowWindow()
        {
            var win = GetWindow<AIAssistantWindow>("AI Assistant");
            win.titleContent = new GUIContent(AgentLocalization.Get("window_title", "AI Assistant"));
            win.minSize = new Vector2(980, 620);
        }

        private void OnEnable()
        {
            AgentToolDefinitions.Init(Application.dataPath);
            LoadPrefs();
            LoadHistory();
            SyncAgentState();
            PureCSharpAgent.StateChanged -= OnAgentStateChanged;
            PureCSharpAgent.StateChanged += OnAgentStateChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            SavePrefs();
            PureCSharpAgent.StateChanged -= OnAgentStateChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            SyncAgentState();
            if (File.Exists(HistoryPath))
            {
                DateTime write = File.GetLastWriteTimeUtc(HistoryPath);
                if (write > _lastHistoryLoad)
                {
                    LoadHistory();
                    _lastHistoryLoad = write;
                    Repaint();
                }
            }
        }

        private void OnGUI()
        {
            ModernUI.Ensure();
            titleContent = new GUIContent(AgentLocalization.Get("window_title", "AI Assistant"));
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), ModernUI.Bg);

            DrawToolbar();

            EditorGUILayout.BeginHorizontal(ModernUI.PagePadding, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawChatPanel();
            if (_showCmdPanel)
            {
                GUILayout.Space(12);
                DrawCommandPanel();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical(ModernUI.TopBar, GUILayout.Height(86));

            EditorGUILayout.BeginHorizontal(GUILayout.Height(34));
            EditorGUILayout.BeginVertical(GUILayout.Width(300));
            GUILayout.Label(AgentLocalization.Get("window_title", "AI Assistant"), ModernUI.WindowTitle);
            GUILayout.Label("Pure C# Agent · Domain Reload Resilient", ModernUI.WindowSubtitle);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            string statusStr = _isWaiting
                ? "● " + AgentLocalization.Get("status_running", "Running")
                : "● " + AgentLocalization.Get("status_ready", "Ready");
            GUILayout.Label(statusStr, _isWaiting ? ModernUI.StatusRunning : ModernUI.StatusOnline, GUILayout.Width(116), GUILayout.Height(24));
            GUILayout.Space(8);

            if (GUILayout.Button("▶ " + AgentLocalization.Get("btn_detect_service", "启动 / 检测"), ModernUI.PrimaryButton, GUILayout.Width(128), GUILayout.Height(28)))
            {
                DetectCSharpAgentRuntimeAndReport();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(34));
            GUILayout.Label(AgentLocalization.Get("mode_label", "模式"), ModernUI.ToolbarLabel, GUILayout.Width(48));
            if (DrawTab(AgentLocalization.Get("mode_remote", "远程 API"), _mode == LLMMode.RemoteAPI, 92))
            {
                _mode = LLMMode.RemoteAPI;
                SavePrefs();
            }
            if (DrawTab(AgentLocalization.Get("mode_ollama", "Ollama 本地"), _mode == LLMMode.Ollama, 104))
            {
                _mode = LLMMode.Ollama;
                SavePrefs();
            }
            GUILayout.Label(AgentLocalization.Get("agent_badge_csharp", "Agent: C#"), ModernUI.AgentBadge, GUILayout.Width(116), GUILayout.Height(28));

            GUILayout.FlexibleSpace();

            _showConfig = GUILayout.Toggle(_showConfig, AgentLocalization.Get("toggle_config", "配置"), ModernUI.ToolbarToggle, GUILayout.Width(64), GUILayout.Height(26));
            _showCmdPanel = GUILayout.Toggle(_showCmdPanel, AgentLocalization.Get("toggle_commands", "命令"), ModernUI.ToolbarToggle, GUILayout.Width(64), GUILayout.Height(26));

            if (GUILayout.Button(AgentLocalization.Get("btn_clear_history", "清空历史"), ModernUI.SecondaryButton, GUILayout.Width(82), GUILayout.Height(26)))
            {
                if (EditorUtility.DisplayDialog(
                    AgentLocalization.Get("dialog_confirm"),
                    AgentLocalization.Get("dialog_clear_history_msg"),
                    AgentLocalization.Get("dialog_ok"),
                    AgentLocalization.Get("dialog_cancel")))
                {
                    _messages.Clear();
                    SaveHistory();
                }
            }

            if (_isWaiting)
            {
                if (GUILayout.Button(AgentLocalization.Get("btn_stop", "停止"), ModernUI.DangerButton, GUILayout.Width(64), GUILayout.Height(26)))
                {
                    StopPendingSession();
                }
            }

            string langBtnLabel = AgentLocalization.CurrentLanguage == "zh" ? "EN" : "中";
            if (GUILayout.Button(langBtnLabel, ModernUI.SecondaryButton, GUILayout.Width(42), GUILayout.Height(26)))
            {
                AgentLocalization.CurrentLanguage = AgentLocalization.CurrentLanguage == "zh" ? "en" : "zh";
                AgentLocalization.LoadTranslations();
                foreach (var win in Resources.FindObjectsOfTypeAll<AIAssistantWindow>()) win.Repaint();
                foreach (var win in Resources.FindObjectsOfTypeAll<AIBridgeSetupWizard>()) win.Repaint();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private bool DrawTab(string text, bool active, float width)
        {
            bool clicked = GUILayout.Button(text, active ? ModernUI.TabActive : ModernUI.TabNormal, GUILayout.Width(width), GUILayout.Height(28));
            return clicked;
        }

        private void DetectCSharpAgentRuntimeAndReport()
        {
            AddMessage("system", AgentLocalization.Get("msg_detecting_csharp", "正在检测进程内 C# Agent、工具定义与持久化目录..."));
            SaveHistory();

            bool runtimeOk = false;
            string detail = "";
            try
            {
                AgentToolDefinitions.Init(Application.dataPath);
                string toolsJson = AgentToolDefinitions.GetToolsJson();
                if (string.IsNullOrEmpty(toolsJson)) throw new InvalidOperationException("工具定义为空");
                if (!Directory.Exists(AgentStateStore.StateDirectory))
                    Directory.CreateDirectory(AgentStateStore.StateDirectory);
                runtimeOk = AgentBridge.EnsureServerRunning();
                detail = "C# Runtime: In-process\nState: " + AgentStateStore.StateDirectory +
                         "\nDomain: " + AgentDomainIdentity.Current;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
            }

            if (!runtimeOk)
            {
                AddMessage("assistant", "C# Agent 运行时检测失败：" + detail);
                SaveHistory();
                Repaint();
                return;
            }

            AddMessage("system", "检测完成：纯 C# Agent 与 Unity 命令执行端可用。\n" + detail);
            EditorUtility.DisplayDialog(
                AgentLocalization.Get("dialog_detect_title", "启动 / 检测"),
                "检测成功：无需 Python、pip、本地代理进程或端口。\n\n" + detail,
                AgentLocalization.Get("dialog_ok", "确定"));

            SaveHistory();
            Repaint();
        }

        private void DrawChatPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (_showConfig)
            {
                DrawConfigPanel();
                GUILayout.Space(10);
            }

            DrawConversationCard();
            EditorGUILayout.EndVertical();
        }

        private void DrawConfigPanel()
        {
            EditorGUILayout.BeginVertical(ModernUI.Card, GUILayout.ExpandWidth(true));

            EditorGUILayout.BeginHorizontal();
            string configTitle = AgentLocalization.Get("config_title_csharp", "C# 智能体配置");
            string configSubtitle = AgentLocalization.Get("config_subtitle_csharp", "（Unity 进程内直连 LLM，状态可跨脚本重载恢复）");
            GUILayout.Label(configTitle, ModernUI.CardTitle, GUILayout.Width(140));
            GUILayout.Label(configSubtitle, ModernUI.CardSubtitle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            if (_mode == LLMMode.RemoteAPI)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

                int newProviderIndex = DrawPopupRow(AgentLocalization.Get("config_provider", "服务商"), (int)_provider, GetLocalizedProviderNames());
                APIProvider newProvider = (APIProvider)newProviderIndex;
                if (newProvider != _provider)
                {
                    _provider = newProvider;
                    if (_provider != APIProvider.Custom)
                    {
                        _baseUrl = ProviderURLs[(int)_provider];
                        _modelName = ProviderModels[(int)_provider];
                    }
                    SavePrefs();
                }

                _baseUrl = DrawTextRow(AgentLocalization.Get("config_base_url", "接口地址"), _baseUrl);
                _modelName = DrawTextRow(AgentLocalization.Get("config_model", "模型名称"), _modelName);
                EditorGUILayout.EndVertical();

                GUILayout.Space(16);

                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                string nextApiKey = DrawPasswordRow(AgentLocalization.Get("config_api_key", "接口密钥"), _apiKey);
                if (nextApiKey != _apiKey)
                {
                    _apiKey = nextApiKey;
                    _apiKeyNeedsReentry = false;
                }
                if (_apiKeyNeedsReentry)
                {
                    EditorGUILayout.HelpBox(
                        "旧 API Key 无法安全解密，已阻止发送。请删除旧值并重新粘贴有效密钥。",
                        MessageType.Error);
                }
                DrawMaxStepRow();
                DrawServiceRow();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                _ollamaUrl = DrawTextRow(AgentLocalization.Get("config_ollama_url", "Ollama 地址"), _ollamaUrl);
                _ollamaModel = DrawTextRow(AgentLocalization.Get("config_ollama_model", "Ollama 模型"), _ollamaModel);
                EditorGUILayout.EndVertical();
                GUILayout.Space(16);
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                DrawMaxStepRow();
                DrawServiceRow();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("▾ " + AgentLocalization.Get("config_system_prompt_label", "额外系统提示词"), ModernUI.SectionTitle);
            _userSystemPrompt = EditorGUILayout.TextArea(_userSystemPrompt, ModernUI.TextArea, GUILayout.MinHeight(54), GUILayout.ExpandWidth(true));

            EditorGUILayout.EndVertical();
        }

        private int DrawPopupRow(string label, int value, string[] options)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(28));
            GUILayout.Label(label, ModernUI.FieldLabel, GUILayout.Width(92));
            int newValue = EditorGUILayout.Popup(value, options, ModernUI.Popup, GUILayout.ExpandWidth(true), GUILayout.Height(24));
            EditorGUILayout.EndHorizontal();
            return newValue;
        }

        private string DrawTextRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(28));
            GUILayout.Label(label, ModernUI.FieldLabel, GUILayout.Width(92));
            string newValue = EditorGUILayout.TextField(value, ModernUI.TextField, GUILayout.ExpandWidth(true), GUILayout.Height(24));
            EditorGUILayout.EndHorizontal();
            return newValue;
        }

        private string DrawPasswordRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(28));
            GUILayout.Label(label, ModernUI.FieldLabel, GUILayout.Width(92));
            string newValue = EditorGUILayout.PasswordField(value, ModernUI.TextField, GUILayout.ExpandWidth(true), GUILayout.Height(24));
            EditorGUILayout.EndHorizontal();
            return newValue;
        }

        private void DrawMaxStepRow()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(28));
            GUILayout.Label(AgentLocalization.Get("config_max_steps", "最大执行步数"), ModernUI.FieldLabel, GUILayout.Width(92));
            _maxSteps = EditorGUILayout.IntSlider(_maxSteps, 1, 80, GUILayout.ExpandWidth(true));
            GUILayout.Label(_maxSteps.ToString(), ModernUI.NumberBadge, GUILayout.Width(40), GUILayout.Height(22));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawServiceRow()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(28));
            GUILayout.Label(AgentLocalization.Get("config_csharp_runtime", "C# 运行时"), ModernUI.FieldLabel, GUILayout.Width(92));
            EditorGUILayout.SelectableLabel("In-process · Reload Resilient", ModernUI.MiniValue, GUILayout.Height(22), GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(28));
            GUILayout.Label(AgentLocalization.Get("config_execute_generated", "执行生成代码"), ModernUI.FieldLabel, GUILayout.Width(92));
            bool next = EditorGUILayout.ToggleLeft(
                AgentLocalization.Get("config_execute_generated_hint", "允许自动执行 AI 生成的临时代码（默认关闭）"),
                _allowGeneratedCodeExecution,
                GUILayout.ExpandWidth(true));
            if (next != _allowGeneratedCodeExecution)
            {
                _allowGeneratedCodeExecution = next;
                SavePrefs();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawConversationCard()
        {
            EditorGUILayout.BeginVertical(ModernUI.Card, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginHorizontal(GUILayout.Height(28));
            GUILayout.Label(AgentLocalization.Get("conversation_title", "会话"), ModernUI.CardTitle, GUILayout.Width(56));
            string sessionText = string.IsNullOrEmpty(_activeSessionId) ? "Session: -" : "Session: " + _activeSessionId;
            GUILayout.Label(sessionText, ModernUI.CardSubtitle, GUILayout.MinWidth(260));
            GUILayout.FlexibleSpace();
            bool oldShowReasoning = _showReasoning;
            _showReasoning = GUILayout.Toggle(_showReasoning, AgentLocalization.Get("toggle_show_reasoning", "显示思考过程"), ModernUI.Toggle, GUILayout.Width(150));
            if (oldShowReasoning != _showReasoning)
            {
                SavePrefs();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            EditorGUILayout.BeginVertical(ModernUI.ChatViewport, GUILayout.ExpandHeight(true));
            _chatScroll = EditorGUILayout.BeginScrollView(_chatScroll, GUILayout.ExpandHeight(true));
            if (_messages.Count == 0)
            {
                GUILayout.Label(AgentLocalization.Get("empty_chat_hint_csharp", "输入任务后，C# Agent 会在这里显示可恢复的执行过程与结果。"), ModernUI.EmptyHint, GUILayout.ExpandHeight(true));
            }
            else
            {
                for (int i = 0; i < _messages.Count; i++) DrawChatBubble(_messages[i]);
            }

            if (_isWaiting)
            {
                string status = !string.IsNullOrEmpty(_agentStepStatus)
                    ? AgentLocalization.GetFormat("agent_executing", _agentStepStatus)
                    : AgentLocalization.Get("agent_processing_csharp", "C# Agent 正在处理请求...");
                GUILayout.Space(6);

                // Graphical progress bar feedback
                float progressVal = _maxSteps > 0 ? Mathf.Clamp01((float)_agentStepCount / _maxSteps) : 0.5f;
                Rect r = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(r, progressVal, $"{status} ({_agentStepCount}/{_maxSteps})");
                GUILayout.Space(4);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // GUILayout.Space(8);
            // DrawPromptTemplates();
            GUILayout.Space(4);
            DrawInputArea();
            EditorGUILayout.EndVertical();
        }

        private struct MessageBlock
        {
            public bool isCode;
            public string content;
            public string language;
        }

        private static List<MessageBlock> ParseMessageBlocks(string text)
        {
            List<MessageBlock> blocks = new List<MessageBlock>();
            if (string.IsNullOrEmpty(text)) return blocks;

            string[] parts = text.Split(new string[] { "```" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0)
                {
                    if (!string.IsNullOrEmpty(parts[i]))
                    {
                        blocks.Add(new MessageBlock { isCode = false, content = parts[i] });
                    }
                }
                else
                {
                    string block = parts[i];
                    string lang = "";
                    string code = block;

                    int firstNewLine = block.IndexOf('\n');
                    if (firstNewLine >= 0)
                    {
                        string possibleLang = block.Substring(0, firstNewLine).Trim();
                        if (possibleLang.Length > 0 && possibleLang.Length < 15)
                        {
                            lang = possibleLang;
                            code = block.Substring(firstNewLine + 1);
                        }
                    }

                    blocks.Add(new MessageBlock { isCode = true, content = code, language = lang });
                }
            }
            return blocks;
        }

        private void DrawChatBubble(ChatMessage msg)
        {
            bool isUser = msg.role == "user";
            bool isSystem = msg.role == "system";
            string rawContent = msg.content ?? "";

            List<MessageBlock> blocks = new List<MessageBlock>();
            if (!isUser && !isSystem && _showReasoning && !string.IsNullOrEmpty(msg.reasoning_content))
            {
                blocks.Add(new MessageBlock { isCode = false, content = "<b>[Reasoning Process]</b>" });
                blocks.Add(new MessageBlock { isCode = true, content = msg.reasoning_content, language = "reasoning" });
                blocks.Add(new MessageBlock { isCode = false, content = "<b>[Final Answer]</b>" });
            }
            blocks.AddRange(ParseMessageBlocks(rawContent));

            float availableWidth = position.width - (_showCmdPanel ? 390f : 70f);
            float width = Mathf.Clamp(availableWidth * 0.88f, 320f, 820f);
            GUIStyle bodyStyle = ModernUI.MessageText;
            GUIStyle bubbleStyle = isUser ? ModernUI.MsgUser : (isSystem ? ModernUI.MsgSystem : ModernUI.MsgAI);

            EditorGUILayout.BeginHorizontal();
            if (isUser) GUILayout.FlexibleSpace();

            EditorGUILayout.BeginVertical(bubbleStyle, GUILayout.Width(width));
            string roleName = isUser ? AgentLocalization.Get("role_user", "你") : (isSystem ? AgentLocalization.Get("role_system", "系统") : AgentLocalization.Get("role_ai", "AI"));
            string time = string.IsNullOrEmpty(msg.time) ? "" : "  " + msg.time;
            GUILayout.Label(roleName + time, ModernUI.MessageMeta);
            GUILayout.Space(4);

            foreach (var block in blocks)
            {
                if (string.IsNullOrEmpty(block.content)) continue;

                if (block.isCode)
                {
                    EditorGUILayout.BeginHorizontal();
                    string headerLabel = string.IsNullOrEmpty(block.language) ? "CODE" : block.language.ToUpper();
                    GUILayout.Label($" ❖ {headerLabel}", ModernUI.CodeBlockHeader);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(AgentLocalization.Get("btn_copy", "复制"), ModernUI.CopyButton, GUILayout.Width(42), GUILayout.Height(18)))
                    {
                        EditorGUIUtility.systemCopyBuffer = block.content;
                    }
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(2);

                    float codeHeight = ModernUI.CodeBlockText.CalcHeight(new GUIContent(block.content), width - 36f);
                    codeHeight += 16f;

                    EditorGUILayout.BeginVertical(ModernUI.CodeBlockBg);
                    EditorGUILayout.SelectableLabel(block.content, ModernUI.CodeBlockText, GUILayout.Height(codeHeight), GUILayout.ExpandWidth(true));
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(6);
                }
                else
                {
                    float textHeight = bodyStyle.CalcHeight(new GUIContent(block.content), width - 28f);
                    textHeight += 12f;
                    EditorGUILayout.SelectableLabel(block.content, bodyStyle, GUILayout.Height(textHeight), GUILayout.ExpandWidth(true));
                    GUILayout.Space(4);
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(AgentLocalization.Get("btn_copy_all", "复制全文"), ModernUI.CopyButton, GUILayout.Width(64), GUILayout.Height(20)))
            {
                string fullCopy = rawContent;
                if (!isUser && !isSystem && !string.IsNullOrEmpty(msg.reasoning_content))
                {
                    fullCopy = msg.reasoning_content + "\n\n" + fullCopy;
                }
                EditorGUIUtility.systemCopyBuffer = fullCopy;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            if (!isUser) GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(7);
        }

        //快捷方法，可以暂时留着
        private void DrawPromptTemplates()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(AgentLocalization.Get("templates_title", "快捷指令:"), ModernUI.MiniValue, GUILayout.Width(58), GUILayout.Height(20));
            DrawPromptTemplateButton("📦 " + AgentLocalization.Get("template_create_cube", "创建立方体"), "在原点创建一个红色立方体");
            DrawPromptTemplateButton("🔍 " + AgentLocalization.Get("template_check_hierarchy", "检查层级"), "检查当前场景的层级结构，告诉我有什么物体");
            DrawPromptTemplateButton("🧹 " + AgentLocalization.Get("template_clean_scene", "清理场景"), "清理场景中临时生成的所有多余物体");
            DrawPromptTemplateButton("📜 " + AgentLocalization.Get("template_create_script", "生成脚本"), "生成一个控制物体旋转的 MonoBehaviour 脚本并挂载到选中的物体上");
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPromptTemplateButton(string label, string promptText)
        {
            if (GUILayout.Button(label, ModernUI.SecondaryButton, GUILayout.Height(20)))
            {
                _inputText = promptText;
                GUI.FocusControl("AIAssistantInput");
            }
        }

        private void DrawInputArea()
        {
            EditorGUILayout.BeginHorizontal(ModernUI.Composer, GUILayout.Height(82));
            GUI.SetNextControlName("AIAssistantInput");
            _inputText = EditorGUILayout.TextArea(_inputText, ModernUI.InputArea, GUILayout.MinHeight(68), GUILayout.ExpandWidth(true));
            GUILayout.Space(8);

            EditorGUILayout.BeginVertical(GUILayout.Width(110));
            GUI.enabled = !_isWaiting && !string.IsNullOrEmpty((_inputText ?? "").Trim());
            if (GUILayout.Button(_isWaiting ? AgentLocalization.Get("status_waiting", "等待中") : "✈ " + AgentLocalization.Get("btn_send", "发送"), ModernUI.PrimaryButton, GUILayout.Height(32)))
            {
                string text = _inputText.Trim();
                _inputText = "";
                GUI.FocusControl(null);
                SendUserMessage(text);
            }
            GUI.enabled = true;

            GUILayout.Space(6);
            GUI.enabled = _isWaiting;
            if (GUILayout.Button("■ " + AgentLocalization.Get("btn_stop", "停止"), ModernUI.DangerButton, GUILayout.Height(30)))
            {
                StopPendingSession();
            }
            GUI.enabled = true;
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCommandPanel()
        {
            EditorGUILayout.BeginVertical(ModernUI.Card, GUILayout.Width(332), GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            GUILayout.Label(AgentLocalization.Get("cmd_panel_title", "Unity 执行命令"), ModernUI.CardTitle);
            GUILayout.Label(AgentLocalization.Get("cmd_panel_subtitle", "搜索、分类、手动执行 Unity 命令"), ModernUI.CardSubtitle);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent(
                AgentLocalization.Get("btn_refresh", "刷新"),
                AgentLocalization.Get("tooltip_refresh_cmds", "重新扫描 Unity 执行命令列表")),
                ModernUI.SecondaryButton, GUILayout.Width(58), GUILayout.Height(26)))
            {
                AgentCommandRegistry.Scan();
                AddMessage("system", AgentLocalization.Get("msg_commands_refreshed", "已刷新 Unity 执行命令列表。"));
                SaveHistory();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
            _cmdSearchText = EditorGUILayout.TextField(_cmdSearchText, ModernUI.SearchField, GUILayout.Height(28));

            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            DrawCategoryChip("All", 54);
            DrawCategoryChip("Scene", 58);
            DrawCategoryChip("Json", 52);
            DrawCategoryChip("Utility", 66);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);
            EditorGUILayout.BeginVertical(ModernUI.CommandViewport, GUILayout.ExpandHeight(true));
            _cmdScroll = EditorGUILayout.BeginScrollView(_cmdScroll, GUILayout.ExpandHeight(true));
            var cmds = AgentCommandRegistry.Commands;
            foreach (var kv in cmds)
            {
                AgentCommandInfo info = kv.Value;
                if (!CommandMatchesSearch(info)) continue;
                DrawCommandRow(kv.Key, info);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
        }

        private void DrawCategoryChip(string category, float width)
        {
            bool active = _cmdCategoryFilter == category;
            string displayCategory = category == "All" ? AgentLocalization.Get("category_all", "全部") : category;
            if (GUILayout.Button(displayCategory, active ? ModernUI.FilterActive : ModernUI.FilterNormal, GUILayout.Width(width), GUILayout.Height(24)))
            {
                _cmdCategoryFilter = category;
            }
        }

        private void DrawCommandRow(string key, AgentCommandInfo info)
        {
            bool selected = _selectedCmdKey == key;
            string category = NormalizeCategory(info.Category);

            EditorGUILayout.BeginVertical(selected ? ModernUI.CommandRowSelected : ModernUI.CommandRow, GUILayout.MinHeight(34));
            EditorGUILayout.BeginHorizontal(GUILayout.Height(28));

            if (GUILayout.Button(info.MethodName, ModernUI.CommandNameButton, GUILayout.ExpandWidth(true), GUILayout.Height(24)))
            {
                _selectedCmdKey = selected ? null : key;
                _cmdArgs = info.ParameterNames ?? new string[0];
                _cmdArgValues = new string[_cmdArgs.Length];
            }

            GUILayout.Label(category, ModernUI.CommandTag, GUILayout.Width(58), GUILayout.Height(20));

            if (GUILayout.Button("▶", ModernUI.SmallRunButton, GUILayout.Width(32), GUILayout.Height(22)))
            {
                string[] args = info.ParameterNames ?? new string[0];
                if (args.Length > 0 && !selected)
                {
                    _selectedCmdKey = key;
                    _cmdArgs = args;
                    _cmdArgValues = new string[_cmdArgs.Length];
                }
                else
                {
                    ExecuteCommand(info, selected ? _cmdArgValues : new string[0]);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (selected)
            {
                GUILayout.Label(info.ClassName, ModernUI.CommandClass);
                if (!string.IsNullOrEmpty(info.Description))
                {
                    GUILayout.Label(info.Description, ModernUI.CommandDescription);
                }

                for (int i = 0; i < _cmdArgs.Length; i++)
                {
                    string label = _cmdArgs[i] + " (" + info.ParameterTypes[i] + ")";
                    _cmdArgValues[i] = DrawTextRow(label, _cmdArgValues[i] ?? "");
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(AgentLocalization.Get("btn_manual_exec", "手动执行"), ModernUI.PrimaryButton, GUILayout.Width(92), GUILayout.Height(26)))
                {
                    ExecuteCommand(info, _cmdArgValues);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private string NormalizeCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return "Utility";
            if (category.IndexOf("scene", StringComparison.OrdinalIgnoreCase) >= 0) return "Scene";
            if (category.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0) return "Json";
            return category;
        }

        private static class ModernUI
        {
            public static Color Bg;
            public static Color Top;
            public static Color Panel;
            public static Color PanelLight;
            public static Color Input;
            public static Color Blue;
            public static Color Green;
            public static Color GreenText;
            public static Color Orange;
            public static Color OrangeText;
            public static Color Red;
            public static Color RedText;
            public static Color Text;
            public static Color Muted;

            public static GUIStyle PagePadding;
            public static GUIStyle TopBar;
            public static GUIStyle WindowTitle;
            public static GUIStyle WindowSubtitle;
            public static GUIStyle ToolbarLabel;
            public static GUIStyle ToolbarToggle;
            public static GUIStyle AgentBadge;
            public static GUIStyle TabNormal;
            public static GUIStyle TabActive;
            public static GUIStyle Card;
            public static GUIStyle CardTitle;
            public static GUIStyle CardSubtitle;
            public static GUIStyle SectionTitle;
            public static GUIStyle FieldLabel;
            public static GUIStyle TextField;
            public static GUIStyle Popup;
            public static GUIStyle TextArea;
            public static GUIStyle MiniValue;
            public static GUIStyle NumberBadge;
            public static GUIStyle StatusOnline;
            public static GUIStyle StatusRunning;
            public static GUIStyle PrimaryButton;
            public static GUIStyle SecondaryButton;
            public static GUIStyle DangerButton;
            public static GUIStyle IconButton;
            public static GUIStyle Toggle;
            public static GUIStyle ChatViewport;
            public static GUIStyle EmptyHint;
            public static GUIStyle MsgSystem;
            public static GUIStyle MsgUser;
            public static GUIStyle MsgAI;
            public static GUIStyle MessageMeta;
            public static GUIStyle MessageText;
            public static GUIStyle CopyButton;
            public static GUIStyle Composer;
            public static GUIStyle InputArea;
            public static GUIStyle SearchField;
            public static GUIStyle FilterActive;
            public static GUIStyle FilterNormal;
            public static GUIStyle CommandViewport;
            public static GUIStyle CommandRow;
            public static GUIStyle CommandRowSelected;
            public static GUIStyle CommandNameButton;
            public static GUIStyle CommandTag;
            public static GUIStyle CommandClass;
            public static GUIStyle CommandDescription;
            public static GUIStyle SmallRunButton;

            // Code block styles
            public static GUIStyle CodeBlockBg;
            public static GUIStyle CodeBlockText;
            public static GUIStyle CodeBlockHeader;

            private static bool _ready;
            private static bool _isProSkinCached;

            public static void Ensure()
            {
                bool isPro = EditorGUIUtility.isProSkin;
                if (_ready && _isProSkinCached == isPro) return;
                _ready = true;
                _isProSkinCached = isPro;

                if (isPro)
                {
                    Bg = new Color(0.075f, 0.082f, 0.095f);
                    Top = new Color(0.105f, 0.115f, 0.135f);
                    Panel = new Color(0.13f, 0.145f, 0.17f);
                    PanelLight = new Color(0.165f, 0.185f, 0.22f);
                    Input = new Color(0.085f, 0.098f, 0.118f);
                    Blue = new Color(0.20f, 0.43f, 0.88f);
                    Green = new Color(0.08f, 0.28f, 0.18f);
                    GreenText = new Color(0.40f, 0.95f, 0.65f);
                    Orange = new Color(0.36f, 0.25f, 0.10f);
                    OrangeText = new Color(1.0f, 0.78f, 0.35f);
                    Red = new Color(0.34f, 0.13f, 0.15f);
                    RedText = new Color(1.0f, 0.78f, 0.78f);
                    Text = new Color(0.86f, 0.88f, 0.93f);
                    Muted = new Color(0.55f, 0.60f, 0.69f);
                }
                else
                {
                    Bg = new Color(0.94f, 0.94f, 0.94f);
                    Top = new Color(0.88f, 0.88f, 0.88f);
                    Panel = new Color(0.98f, 0.98f, 0.98f);
                    PanelLight = new Color(0.85f, 0.87f, 0.90f);
                    Input = new Color(1.0f, 1.0f, 1.0f);
                    Blue = new Color(0.12f, 0.38f, 0.84f);
                    Green = new Color(0.80f, 0.94f, 0.85f);
                    GreenText = new Color(0.08f, 0.40f, 0.20f);
                    Orange = new Color(0.98f, 0.90f, 0.70f);
                    OrangeText = new Color(0.60f, 0.35f, 0.05f);
                    Red = new Color(0.95f, 0.82f, 0.84f);
                    RedText = new Color(0.60f, 0.10f, 0.15f);
                    Text = new Color(0.12f, 0.14f, 0.18f);
                    Muted = new Color(0.40f, 0.44f, 0.50f);
                }

                PagePadding = new GUIStyle { padding = new RectOffset(12, 12, 12, 12) };
                TopBar = Box(Top, new RectOffset(16, 16, 8, 8));
                WindowTitle = Label(18, isPro ? Color.white : Text, FontStyle.Bold);
                WindowSubtitle = Label(12, Muted, FontStyle.Normal);
                ToolbarLabel = Label(12, Muted, FontStyle.Normal);
                ToolbarToggle = Button(PanelLight, Text, FontStyle.Normal);
                AgentBadge = Chip(isPro ? new Color(0.13f, 0.22f, 0.36f) : new Color(0.80f, 0.88f, 1.0f), isPro ? new Color(0.76f, 0.86f, 1.0f) : new Color(0.05f, 0.25f, 0.55f), FontStyle.Bold);
                TabNormal = Button(isPro ? new Color(0.125f, 0.14f, 0.165f) : new Color(0.85f, 0.85f, 0.85f), Muted, FontStyle.Normal);
                TabActive = Button(isPro ? new Color(0.16f, 0.26f, 0.46f) : new Color(0.16f, 0.36f, 0.66f), Color.white, FontStyle.Bold);
                Card = Box(Panel, new RectOffset(14, 14, 12, 12));
                CardTitle = Label(15, isPro ? Color.white : Text, FontStyle.Bold);
                CardSubtitle = Label(12, Muted, FontStyle.Normal);
                SectionTitle = Label(12, Text, FontStyle.Bold);
                FieldLabel = Label(12, Text, FontStyle.Normal);
                TextField = TextInput(24);
                Popup = new GUIStyle(EditorStyles.popup)
                {
                    fontSize = 12,
                    normal = { background = Tex(Input), textColor = Text },
                    focused = { background = Tex(isPro ? new Color(0.10f, 0.12f, 0.15f) : new Color(0.90f, 0.92f, 0.95f)), textColor = isPro ? Color.white : Text },
                    padding = new RectOffset(8, 8, 3, 3)
                };
                TextArea = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true,
                    fontSize = 12,
                    normal = { background = Tex(Input), textColor = Text },
                    focused = { background = Tex(isPro ? new Color(0.10f, 0.12f, 0.15f) : new Color(0.90f, 0.92f, 0.95f)), textColor = isPro ? Color.white : Text },
                    padding = new RectOffset(8, 8, 7, 7)
                };
                MiniValue = Label(11, Muted, FontStyle.Normal);
                NumberBadge = Label(12, Text, FontStyle.Bold);
                NumberBadge.alignment = TextAnchor.MiddleCenter;
                NumberBadge.normal.background = Tex(Input);

                StatusOnline = Chip(Green, GreenText, FontStyle.Bold);
                StatusRunning = Chip(Orange, OrangeText, FontStyle.Bold);
                PrimaryButton = Button(Blue, Color.white, FontStyle.Bold);
                SecondaryButton = Button(PanelLight, Text, FontStyle.Normal);
                DangerButton = Button(isPro ? Red : new Color(0.95f, 0.80f, 0.80f), isPro ? RedText : new Color(0.60f, 0.10f, 0.15f), FontStyle.Bold);
                IconButton = Button(PanelLight, Text, FontStyle.Bold);
                Toggle = new GUIStyle(EditorStyles.toggle)
                {
                    fontSize = 12,
                    normal = { textColor = Text },
                    hover = { textColor = isPro ? Color.white : Text },
                    focused = { textColor = isPro ? Color.white : Text },
                    active = { textColor = isPro ? Color.white : Text }
                };

                ChatViewport = Box(isPro ? new Color(0.082f, 0.095f, 0.115f) : new Color(0.90f, 0.90f, 0.90f), new RectOffset(10, 10, 10, 10));
                EmptyHint = Label(13, Muted, FontStyle.Normal);
                EmptyHint.alignment = TextAnchor.MiddleCenter;
                MsgSystem = Box(isPro ? new Color(0.10f, 0.20f, 0.15f) : new Color(0.80f, 0.92f, 0.85f), new RectOffset(10, 10, 8, 8));
                MsgUser = Box(isPro ? new Color(0.105f, 0.16f, 0.25f) : new Color(0.85f, 0.90f, 0.96f), new RectOffset(10, 10, 8, 8));
                MsgAI = Box(isPro ? new Color(0.16f, 0.125f, 0.225f) : new Color(0.92f, 0.88f, 0.95f), new RectOffset(10, 10, 8, 8));
                MessageMeta = Label(11, Muted, FontStyle.Bold);
                MessageText = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    richText = true,
                    wordWrap = true,
                    fontSize = 12,
                    normal = { textColor = Text }
                };
                CopyButton = Button(PanelLight, Text, FontStyle.Normal);
                Composer = Box(isPro ? new Color(0.105f, 0.12f, 0.145f) : new Color(0.88f, 0.88f, 0.88f), new RectOffset(8, 8, 7, 7));
                InputArea = new GUIStyle(TextArea) { fontSize = 13 };

                SearchField = TextInput(28);
                FilterActive = Chip(Blue, Color.white, FontStyle.Bold);
                FilterNormal = Chip(PanelLight, Text, FontStyle.Normal);
                CommandViewport = Box(isPro ? new Color(0.082f, 0.095f, 0.115f) : new Color(0.90f, 0.90f, 0.90f), new RectOffset(6, 6, 6, 6));
                CommandRow = Box(isPro ? new Color(0.135f, 0.155f, 0.185f) : new Color(0.95f, 0.95f, 0.95f), new RectOffset(6, 6, 5, 5));
                CommandRowSelected = Box(isPro ? new Color(0.155f, 0.19f, 0.25f) : new Color(0.80f, 0.88f, 0.96f), new RectOffset(6, 6, 5, 7));
                CommandNameButton = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = Text }
                };
                CommandTag = Chip(isPro ? new Color(0.16f, 0.25f, 0.42f) : new Color(0.80f, 0.88f, 1.0f), isPro ? new Color(0.64f, 0.78f, 1.0f) : new Color(0.05f, 0.25f, 0.55f), FontStyle.Bold);
                CommandClass = Label(10, Muted, FontStyle.Normal);
                CommandDescription = Label(11, Muted, FontStyle.Normal);
                CommandDescription.wordWrap = true;
                SmallRunButton = Button(isPro ? new Color(0.18f, 0.24f, 0.33f) : new Color(0.80f, 0.88f, 1.0f), isPro ? new Color(0.82f, 0.90f, 1f) : new Color(0.05f, 0.25f, 0.55f), FontStyle.Bold);

                // Code block style initialization
                CodeBlockBg = Box(isPro ? new Color(0.05f, 0.06f, 0.07f) : new Color(0.92f, 0.92f, 0.92f), new RectOffset(10, 10, 8, 8));
                CodeBlockText = new GUIStyle(EditorStyles.label)
                {
                    font = Font.CreateDynamicFontFromOSFont(new string[] { "Consolas", "Courier New", "Courier", "Monospace" }, 12),
                    richText = false,
                    wordWrap = true,
                    normal = { textColor = isPro ? new Color(0.85f, 0.90f, 0.75f) : new Color(0.15f, 0.35f, 0.15f) }
                };
                CodeBlockHeader = Label(11, Muted, FontStyle.Bold);
            }

            private static GUIStyle TextInput(int height)
            {
                return new GUIStyle(EditorStyles.textField)
                {
                    fixedHeight = height,
                    fontSize = 12,
                    normal = { background = Tex(Input), textColor = Text },
                    focused = { background = Tex(_isProSkinCached ? new Color(0.105f, 0.125f, 0.16f) : new Color(0.95f, 0.97f, 1f)), textColor = Text },
                    padding = new RectOffset(8, 8, 4, 4)
                };
            }

            private static GUIStyle Box(Color color, RectOffset padding)
            {
                return new GUIStyle
                {
                    padding = padding,
                    margin = new RectOffset(0, 0, 0, 0),
                    normal = { background = Tex(color) }
                };
            }

            private static GUIStyle Label(int size, Color color, FontStyle style)
            {
                return new GUIStyle(EditorStyles.label)
                {
                    fontSize = size,
                    fontStyle = style,
                    normal = { textColor = color }
                };
            }

            private static GUIStyle Button(Color bg, Color color, FontStyle style)
            {
                Color hover = new Color(Mathf.Min(bg.r + 0.05f, 1f), Mathf.Min(bg.g + 0.05f, 1f), Mathf.Min(bg.b + 0.05f, 1f), bg.a);
                Color active = new Color(Mathf.Max(bg.r - 0.04f, 0f), Mathf.Max(bg.g - 0.04f, 0f), Mathf.Max(bg.b - 0.04f, 0f), bg.a);
                return new GUIStyle(GUI.skin.button)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = style,
                    normal = { background = Tex(bg), textColor = color },
                    hover = { background = Tex(hover), textColor = color },
                    active = { background = Tex(active), textColor = color },
                    padding = new RectOffset(8, 8, 3, 3)
                };
            }

            private static GUIStyle Chip(Color bg, Color color, FontStyle style)
            {
                GUIStyle s = Button(bg, color, style);
                s.fontSize = 11;
                return s;
            }

            private static Texture2D Tex(Color color)
            {
                Texture2D tex = new Texture2D(1, 1);
                tex.hideFlags = HideFlags.HideAndDontSave;
                tex.SetPixel(0, 0, color);
                tex.Apply();
                return tex;
            }
        }


        private bool CommandMatchesSearch(AgentCommandInfo info)
        {
            string category = NormalizeCategory(info.Category);
            if (!string.IsNullOrEmpty(_cmdCategoryFilter) && _cmdCategoryFilter != "All")
            {
                if (!string.Equals(category, _cmdCategoryFilter, StringComparison.OrdinalIgnoreCase)) return false;
            }

            if (string.IsNullOrEmpty(_cmdSearchText)) return true;
            string q = _cmdSearchText.Trim();
            if (string.IsNullOrEmpty(q)) return true;

            if (!string.IsNullOrEmpty(info.MethodName) && info.MethodName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(info.ClassName) && info.ClassName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(info.Description) && info.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(info.Category) && info.Category.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }


        private void SendUserMessage(string text)
        {
            if (PureCSharpAgent.IsRunning)
            {
                AddMessage("system", "已有 C# Agent 会话正在运行，请先等待完成或点击停止。");
                SaveHistory();
                return;
            }

            // API Key 不进入可持久化 Agent 状态；启动前必须先安全写入 EditorPrefs，
            // 这样生成脚本触发 Domain Reload 后才能恢复同一凭据。
            _apiKey = AgentEncryptionUtility.NormalizeApiKey(_apiKey);
            SavePrefs();
            if (_mode == LLMMode.RemoteAPI && !string.IsNullOrEmpty(_apiKey) && _apiKeyNeedsReentry)
            {
                AddMessage("assistant", "C# Agent 启动失败：当前 API Key 无法安全保存，已阻止请求。请重新输入密钥或检查 Unity EditorPrefs 写入权限。");
                SaveHistory();
                Repaint();
                return;
            }

            AddMessage("user", text);
            SaveHistory();

            if (!AgentBridge.EnsureServerRunning())
            {
                AddMessage("assistant", AgentLocalization.Get("err_bridge_not_running"));
                SaveHistory();
                return;
            }

            AgentSessionConfig config = BuildSessionConfig();
            string error;
            if (PureCSharpAgent.StartSession(config, BuildConversationState(), out error))
            {
                SyncAgentState();
                LoadHistory();
            }
            else
            {
                AddMessage("assistant", "C# Agent 启动失败：" + error);
                SaveHistory();
            }
            Repaint();
        }

        private AgentSessionConfig BuildSessionConfig()
        {
            AgentSessionConfig data = new AgentSessionConfig();
            data.apiUrl = GetCurrentApiUrl();
            data.apiKey = _mode == LLMMode.Ollama ? "" : _apiKey;
            data.requireApiKey = _mode == LLMMode.RemoteAPI && _provider != APIProvider.Custom;
            data.model = _mode == LLMMode.Ollama ? _ollamaModel : _modelName;
            data.provider = _mode == LLMMode.Ollama ? "openai-compatible" : ProviderNames[(int)_provider];
            data.userSystemPrompt = _userSystemPrompt ?? "";
            data.maxSteps = _maxSteps;
            data.language = AgentLocalization.CurrentLanguage;
            return data;
        }

        private List<AgentMessageState> BuildConversationState()
        {
            List<AgentMessageState> result = new List<AgentMessageState>();
            for (int i = 0; i < _messages.Count; i++)
            {
                ChatMessage source = _messages[i];
                if (source.role != "user" && source.role != "assistant") continue;
                AgentMessageState message = new AgentMessageState();
                message.role = source.role;
                message.content = source.content ?? "";
                message.reasoningContent = source.reasoning_content ?? "";
                result.Add(message);
            }
            return result;
        }

        private string GetCurrentApiUrl()
        {
            string baseUrl = (_mode == LLMMode.Ollama ? _ollamaUrl : _baseUrl) ?? "";
            baseUrl = baseUrl.Trim().TrimEnd('/');
            if (_provider == APIProvider.Claude && _mode != LLMMode.Ollama) return baseUrl;
            if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return baseUrl;
            if (_mode == LLMMode.Ollama && !baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                baseUrl += "/v1";
            return baseUrl + "/chat/completions";
        }

        private void OnAgentStateChanged()
        {
            SyncAgentState();
            Repaint();
        }

        private void SyncAgentState()
        {
            AgentSessionState state = PureCSharpAgent.CurrentState;
            _isWaiting = state != null && state.active;
            _activeSessionId = state == null ? null : state.sessionId;
            _agentStepCount = state == null ? 0 : state.step;
            _agentStepStatus = state == null ? null : state.status;
        }

        private void StopPendingSession()
        {
            PureCSharpAgent.Cancel();
            _agentStepStatus = "正在安全终止 C# Agent；已开始的编译回滚不会被中断";
            Repaint();
        }

        private void ExecuteCommand(AgentCommandInfo info, string[] argValues)
        {
            JsonData payload = AgentJson.NewObject();
            payload["ClassName"] = info.ClassName;
            payload["MethodName"] = info.MethodName;
            payload["Args"] = AgentJson.StringArray(argValues ?? new string[0]);
            string result = AgentBridge.ExecuteCommandJson(JsonMapper.ToJson(payload));
            AddMessage("system", AgentLocalization.GetFormat("msg_manual_exec_result", info.MethodName, result));
            SaveHistory();
            Repaint();
        }

        private void AddMessage(string role, string content, string reasoning = "")
        {
            _messages.Add(new ChatMessage
            {
                role = role,
                content = content ?? "",
                reasoning_content = reasoning ?? "",
                time = DateTime.Now.ToString("HH:mm:ss")
            });
        }

        private void SaveHistory()
        {
            try
            {
                JsonData root = AgentJson.NewObject();
                JsonData arr = AgentJson.NewArray();
                for (int i = 0; i < _messages.Count; i++)
                {
                    ChatMessage m = _messages[i];
                    JsonData item = AgentJson.NewObject();
                    item["role"] = m.role ?? "";
                    item["content"] = m.content ?? "";
                    item["reasoning_content"] = m.reasoning_content ?? "";
                    item["time"] = m.time ?? "";
                    arr.Add(item);
                }
                root["messages"] = arr;
                string historyDirectory = Path.GetDirectoryName(HistoryPath);
                if (!string.IsNullOrEmpty(historyDirectory) && !Directory.Exists(historyDirectory))
                    Directory.CreateDirectory(historyDirectory);
                WriteTextSharedWithRetry(HistoryPath, JsonMapper.ToJson(root));
            }
            catch (Exception e) { Debug.LogWarning("[AIAssistant] 保存历史失败: " + e.Message); }
        }

        private void LoadHistory()
        {
            try
            {
                if (!File.Exists(HistoryPath)) return;
                string json = ReadTextSharedWithRetry(HistoryPath);
                JsonData root = AgentJson.ParseObject(json);
                List<ChatMessage> list = new List<ChatMessage>();
                if (AgentJson.HasKey(root, "messages") && root["messages"].IsArray)
                {
                    JsonData arr = root["messages"];
                    for (int i = 0; i < arr.Count; i++)
                    {
                        JsonData item = arr[i];
                        list.Add(new ChatMessage
                        {
                            role = AgentJson.GetString(item, "role"),
                            content = AgentJson.GetString(item, "content"),
                            reasoning_content = AgentJson.GetString(item, "reasoning_content"),
                            time = AgentJson.GetString(item, "time")
                        });
                    }
                }
                _messages = list;
            }
            catch (Exception e) { Debug.LogWarning("[AIAssistant] 加载历史失败: " + e.Message); }
        }

        private static void WriteTextSharedWithRetry(string path, string text)
        {
            Exception last = null;
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
                    using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                        fs.Flush(true);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    System.Threading.Thread.Sleep(30 * (i + 1));
                }
            }
            throw last ?? new IOException("写入失败: " + path);
        }

        private static string ReadTextSharedWithRetry(string path)
        {
            Exception last = null;
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader reader = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                    System.Threading.Thread.Sleep(30 * (i + 1));
                }
            }
            throw last ?? new IOException("读取失败: " + path);
        }

        private void SavePrefs()
        {
            AIBridgeSettings settings = AIBridgeSettings.GetOrCreateSettings();
            settings.Apply(
                (int)_mode,
                (int)_provider,
                _baseUrl,
                _modelName,
                _ollamaUrl,
                _ollamaModel,
                _userSystemPrompt,
                _maxSteps,
                _showReasoning,
                _allowGeneratedCodeExecution);

            // API key is stored securely using versioned AES encryption in EditorPrefs.
            string normalizedKey = AgentEncryptionUtility.NormalizeApiKey(_apiKey);
            string encryptedKey = AgentEncryptionUtility.Encrypt(normalizedKey);
            if (string.IsNullOrEmpty(normalizedKey))
            {
                EditorPrefs.SetString("AIAss_Api_Key", "");
                _apiKeyNeedsReentry = false;
            }
            else if (!string.IsNullOrEmpty(encryptedKey))
            {
                EditorPrefs.SetString("AIAss_Api_Key", encryptedKey);
                _apiKeyNeedsReentry = false;
            }
            else
            {
                _apiKeyNeedsReentry = true;
            }
        }

        private void LoadPrefs()
        {
            AIBridgeSettings settings = AIBridgeSettings.GetOrCreateSettings();
            _mode = (LLMMode)settings.Mode;
            _provider = (APIProvider)settings.Provider;
            _baseUrl = settings.BaseUrl;
            _modelName = settings.ModelName;
            _ollamaUrl = settings.OllamaUrl;
            _ollamaModel = settings.OllamaModel;
            _userSystemPrompt = settings.UserSystemPrompt;
            _maxSteps = settings.MaxSteps;
            _showReasoning = settings.ShowReasoning;
            _allowGeneratedCodeExecution = settings.AllowGeneratedCodeExecution;

            string storedKey = EditorPrefs.GetString("AIAss_Api_Key", "");
            bool shouldRewrite;
            if (AgentEncryptionUtility.TryDecrypt(storedKey, out _apiKey, out shouldRewrite))
            {
                _apiKey = AgentEncryptionUtility.NormalizeApiKey(_apiKey);
                _apiKeyNeedsReentry = false;
                if (shouldRewrite && !string.IsNullOrEmpty(_apiKey))
                {
                    string migrated = AgentEncryptionUtility.Encrypt(_apiKey);
                    if (!string.IsNullOrEmpty(migrated))
                        EditorPrefs.SetString("AIAss_Api_Key", migrated);
                }
            }
            else
            {
                _apiKey = "";
                _apiKeyNeedsReentry = !string.IsNullOrEmpty(storedKey);
            }
        }
    }
}
