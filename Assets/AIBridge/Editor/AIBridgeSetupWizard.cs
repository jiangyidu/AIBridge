using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>
    /// 纯 C# 环境检查。不会启动进程、调用 pip 或写入 Assets 外的依赖。
    /// </summary>
    public class AIBridgeSetupWizard : EditorWindow
    {
        private enum SetupStep
        {
            Welcome,
            Checking,
            AllSet,
            Error
        }

        private SetupStep _currentStep = SetupStep.Welcome;
        private string _statusMessage = "";
        private string _errorMessage = "";
        private Vector2 _scrollPos;

        [MenuItem("AIBridge/Environment Setup Wizard")]
        public static void ShowWindow()
        {
            AIBridgeSetupWizard window = GetWindow<AIBridgeSetupWizard>(true, "AIBridge C# Environment", true);
            window.titleContent = new GUIContent("AIBridge C# Environment");
            window.minSize = new Vector2(500, 360);
            window.maxSize = new Vector2(500, 360);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            _currentStep = SetupStep.Welcome;
            _statusMessage = "AIBridge 采用 Editor-only 纯 C# 架构。检测过程不会安装软件或启动外部进程。";
            _errorMessage = "";
        }

        private void OnGUI()
        {
            titleContent = new GUIContent("AIBridge C# Environment");
            GUILayout.Space(10);
            GUILayout.Label("AIBridge 纯 C# 环境检查", EditorStyles.largeLabel);
            GUILayout.Space(10);

            Rect progressRect = EditorGUILayout.GetControlRect(false, 18);
            float progress = _currentStep == SetupStep.Welcome ? 0f :
                (_currentStep == SetupStep.Checking ? 0.5f : 1f);
            EditorGUI.ProgressBar(progressRect, progress, "配置进度");

            GUILayout.Space(20);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, "box", GUILayout.Height(180));
            GUIStyle statusStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
            statusStyle.richText = true;
            EditorGUILayout.LabelField(_statusMessage, statusStyle);
            if (!string.IsNullOrEmpty(_errorMessage))
            {
                GUILayout.Space(10);
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
            }
            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (_currentStep == SetupStep.Welcome || _currentStep == SetupStep.Error)
            {
                if (GUILayout.Button("检测 C# 环境", GUILayout.Width(150), GUILayout.Height(30)))
                    RunChecks();
            }
            else if (_currentStep == SetupStep.AllSet)
            {
                if (GUILayout.Button("打开 AI 助手", GUILayout.Width(150), GUILayout.Height(30)))
                {
                    AIAssistantWindow.ShowWindow();
                    Close();
                }
            }
            else
            {
                GUI.enabled = false;
                GUILayout.Button("检测中...", GUILayout.Width(150), GUILayout.Height(30));
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void RunChecks()
        {
            _currentStep = SetupStep.Checking;
            _statusMessage = "正在验证 Unity 版本、工具定义和可恢复状态目录...";
            _errorMessage = "";
            Repaint();

            try
            {
                if (!IsUnity2018_4OrNewer(Application.unityVersion))
                    throw new InvalidOperationException("最低支持 Unity 2018.4，当前版本为 " + Application.unityVersion);

                AgentToolDefinitions.Init(Application.dataPath);
                string tools = AgentToolDefinitions.GetToolsJson();
                if (string.IsNullOrEmpty(tools))
                    throw new InvalidOperationException("AgentTools.json 未加载或内容为空");

                string stateDirectory = AgentStateStore.StateDirectory;
                if (!Directory.Exists(stateDirectory)) Directory.CreateDirectory(stateDirectory);
                string probe = Path.Combine(stateDirectory, "write-probe.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);

                AIBridgeSettings.GetOrCreateSettings();
                AgentSessionState session = PureCSharpAgent.CurrentState;

                _currentStep = SetupStep.AllSet;
                _statusMessage =
                    "<b><color=green>纯 C# 环境已就绪</color></b>\n\n" +
                    "- Unity: " + Application.unityVersion + "\n" +
                    "- Agent: EditorApplication.update 持久化状态机\n" +
                    "- 状态目录: " + stateDirectory + "\n" +
                    "- 外部进程: 无\n" +
                    "- Python / pip: 不需要\n" +
                    "- 生成代码自动执行: 默认关闭" +
                    (session != null && session.active ? "\n- 已检测到可恢复的活动会话" : "");
            }
            catch (Exception ex)
            {
                _currentStep = SetupStep.Error;
                _statusMessage = "<b><color=red>C# 环境检查未通过</color></b>";
                _errorMessage = ex.Message;
            }
            Repaint();
        }

        private static bool IsUnity2018_4OrNewer(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            string[] parts = version.Split('.');
            int major;
            int minor;
            if (parts.Length < 2 || !int.TryParse(parts[0], out major) || !int.TryParse(parts[1], out minor))
                return false;
            return major > 2018 || (major == 2018 && minor >= 4);
        }
    }
}
