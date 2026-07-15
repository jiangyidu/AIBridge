using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AIBridge.Agent
{
    /// <summary>
    /// AIBridge 环境配置向导。
    /// 用于检测开发者的系统环境中是否安装了 Python、Pip 以及必要的依赖包（如 requests）。
    /// 如果缺失依赖，可以提供一键安装或引导下载。
    /// </summary>
    public class AIBridgeSetupWizard : EditorWindow
    {
        private enum SetupStep
        {
            Welcome,
            CheckingPython,
            PythonNotFound,
            CheckingDependencies,
            InstallingDependencies,
            DependenciesError,
            AllSet
        }

        private SetupStep _currentStep = SetupStep.Welcome;
        private string _statusMessage = "";
        private string _pythonVersion = "";
        private string _errorMessage = "";
        private string _resolvedPythonCmd = "python";
        private Vector2 _scrollPos;

        // 异步进程相关
        private Process _activeProcess;
        private string _processOutput;
        private string _processError;
        private Action<int, string, string> _onProcessExited;
        private double _processStartTime;
        private double _processTimeoutMs;
        private bool _isProcessTimedOut;

        [MenuItem("AIBridge/Environment Setup Wizard")]
        public static void ShowWindow()
        {
            var window = GetWindow<AIBridgeSetupWizard>(true, AgentLocalization.Get("wizard_title", "AIBridge Environment Wizard"), true);
            window.titleContent = new GUIContent(AgentLocalization.Get("wizard_title", "AIBridge Environment Wizard"));
            window.minSize = new Vector2(450, 350);
            window.maxSize = new Vector2(450, 350);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            // 每次打开向导时自动重置状态
            _currentStep = SetupStep.Welcome;
            _statusMessage = AgentLocalization.Get("wizard_welcome_status", "欢迎使用 AIBridge。在开始之前，我们需要检查您的 Python 环境。");
            _resolvedPythonCmd = EditorPrefs.GetString("AIBridge_PythonCmd", "python");
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (_activeProcess != null && !_activeProcess.HasExited)
            {
                try { _activeProcess.Kill(); } catch { }
            }
        }

        private void OnEditorUpdate()
        {
            if (_activeProcess != null)
            {
                bool hasExited = false;
                try
                {
                    hasExited = _activeProcess.HasExited;
                }
                catch
                {
                    hasExited = true;
                }

                if (!hasExited)
                {
                    double elapsed = (EditorApplication.timeSinceStartup - _processStartTime) * 1000.0;
                    if (elapsed > _processTimeoutMs)
                    {
                        _isProcessTimedOut = true;
                        try { _activeProcess.Kill(); } catch { }
                        hasExited = true;
                    }
                }

                if (hasExited)
                {
                    int exitCode = -1;
                    if (!_isProcessTimedOut)
                    {
                        try { exitCode = _activeProcess.ExitCode; } catch { }
                    }
                    var callback = _onProcessExited;

                    try { _activeProcess.Dispose(); } catch { }
                    _activeProcess = null;

                    if (callback != null)
                    {
                        string err = _isProcessTimedOut ? AgentLocalization.Get("wizard_timeout", "操作超时 (可能命令被阻止或弹出窗口)") : _processError;
                        callback.Invoke(exitCode, _processOutput, err);
                    }
                    Repaint();
                }
            }
        }

        private void OnGUI()
        {
            titleContent = new GUIContent(AgentLocalization.Get("wizard_title", "AIBridge Environment Wizard"));
            GUILayout.Space(10);
            GUILayout.Label(AgentLocalization.Get("wizard_header", "🤖 AIBridge 环境配置向导"), EditorStyles.largeLabel);
            GUILayout.Space(10);

            // 进度指示器
            DrawProgressBar();

            GUILayout.Space(20);

            // 状态显示区
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, "box", GUILayout.Height(150));
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

            // 底部操作按钮区
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            switch (_currentStep)
            {
                case SetupStep.Welcome:
                    if (GUILayout.Button(AgentLocalization.Get("wizard_btn_start_check", "开始检测环境"), GUILayout.Width(150), GUILayout.Height(30)))
                    {
                        CheckPython();
                    }
                    break;
                case SetupStep.CheckingPython:
                case SetupStep.CheckingDependencies:
                case SetupStep.InstallingDependencies:
                    GUI.enabled = false;
                    GUILayout.Button(AgentLocalization.Get("wizard_btn_wait", "请稍候..."), GUILayout.Width(150), GUILayout.Height(30));
                    GUI.enabled = true;
                    break;
                case SetupStep.PythonNotFound:
                    if (GUILayout.Button(AgentLocalization.Get("wizard_btn_download_python", "下载 Python"), GUILayout.Width(120), GUILayout.Height(30)))
                    {
                        Application.OpenURL("https://www.python.org/downloads/");
                    }
                    if (GUILayout.Button(AgentLocalization.Get("wizard_btn_recheck", "重新检测"), GUILayout.Width(120), GUILayout.Height(30)))
                    {
                        CheckPython();
                    }
                    break;
                case SetupStep.DependenciesError:
                    if (GUILayout.Button(AgentLocalization.Get("wizard_btn_install_deps", "一键安装依赖"), GUILayout.Width(150), GUILayout.Height(30)))
                    {
                        InstallDependencies();
                    }
                    if (GUILayout.Button(AgentLocalization.Get("wizard_btn_skip", "跳过"), GUILayout.Width(80), GUILayout.Height(30)))
                    {
                        _currentStep = SetupStep.AllSet;
                        _statusMessage = AgentLocalization.Get("wizard_skip_status", "<b><color=green>配置完成！</color></b>\n虽然跳过了依赖安装，您仍可以尝试运行 Agent。");
                        _errorMessage = "";
                    }
                    break;
                case SetupStep.AllSet:
                    if (GUILayout.Button(AgentLocalization.Get("wizard_btn_open_assistant", "打开 AI 助手"), GUILayout.Width(150), GUILayout.Height(30)))
                    {
                        AIAssistantWindow.ShowWindow();
                        Close();
                    }
                    break;
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawProgressBar()
        {
            float progress = 0f;
            switch (_currentStep)
            {
                case SetupStep.Welcome: progress = 0f; break;
                case SetupStep.CheckingPython: progress = 0.2f; break;
                case SetupStep.PythonNotFound: progress = 0.2f; break;
                case SetupStep.CheckingDependencies: progress = 0.5f; break;
                case SetupStep.InstallingDependencies: progress = 0.7f; break;
                case SetupStep.DependenciesError: progress = 0.7f; break;
                case SetupStep.AllSet: progress = 1f; break;
            }

            Rect rect = EditorGUILayout.GetControlRect(false, 18);
            EditorGUI.ProgressBar(rect, progress, AgentLocalization.Get("wizard_progress_title", "配置进度"));
        }

        // ------------------ 核心检测逻辑 ------------------

        private void CheckPython()
        {
            _currentStep = SetupStep.CheckingPython;
            _errorMessage = "";
            TryNextPythonCommand(0);
        }

        private void TryNextPythonCommand(int index)
        {
            string[] commands = { "python", "python3", "py" };
            if (index >= commands.Length)
            {
                _currentStep = SetupStep.PythonNotFound;
                _statusMessage = AgentLocalization.Get("wizard_python_not_found", "<b><color=red>未检测到 Python 运行环境</color></b>\n\nAIBridge 的后台 Agent 需要 Python 支持。");
                _errorMessage = AgentLocalization.Get("wizard_python_not_found_err", "建议：\n1. 点击下方按钮前往官网下载 Python (推荐 3.8+)\n2. <b>安装时务必勾选 \"Add Python to PATH\"</b>\n3. 安装完成后可能需要重启 Unity 或电脑");
                Repaint();
                return;
            }

            string cmd = commands[index];
            _statusMessage = AgentLocalization.GetFormat("wizard_detecting_python", cmd);
            Repaint();

            RunCommandAsync(cmd, "--version", (exitCode, output, error) =>
            {
                string combined = (output + "\n" + error).Trim();
                bool hasPythonVersion = combined.Contains("Python") || System.Text.RegularExpressions.Regex.IsMatch(combined, @"\d+\.\d+\.\d+");

                if (exitCode == 0 && hasPythonVersion)
                {
                    _resolvedPythonCmd = cmd;
                    EditorPrefs.SetString("AIBridge_PythonCmd", cmd);
                    _pythonVersion = combined;
                    _statusMessage = AgentLocalization.GetFormat("wizard_python_detected", _pythonVersion, cmd);
                    CheckDependencies();
                }
                else
                {
                    TryNextPythonCommand(index + 1);
                }
            }, 5000);
        }

        private void CheckDependencies()
        {
            _currentStep = SetupStep.CheckingDependencies;
            _errorMessage = "";

            // 检查 requests 模块是否安装
            RunCommandAsync(_resolvedPythonCmd, "-c \"import requests; print('requests OK')\"", (exitCode, output, error) =>
            {
                if (exitCode == 0 && output.Contains("requests OK"))
                {
                    _currentStep = SetupStep.AllSet;
                    _statusMessage = AgentLocalization.GetFormat("wizard_all_set_status", _resolvedPythonCmd, _pythonVersion);
                }
                else
                {
                    _currentStep = SetupStep.DependenciesError;
                    _statusMessage = AgentLocalization.GetFormat("wizard_missing_deps", _pythonVersion, _resolvedPythonCmd);
                    _errorMessage = AgentLocalization.Get("wizard_missing_deps_err", "请点击下方「一键安装依赖」使用 pip 进行安装。");
                }
            }, 8000);
        }

        private void InstallDependencies()
        {
            _currentStep = SetupStep.InstallingDependencies;
            _statusMessage = AgentLocalization.GetFormat("wizard_installing_deps", _resolvedPythonCmd);
            _errorMessage = "";

            RunCommandAsync(_resolvedPythonCmd, "-m pip install requests", (exitCode, output, error) =>
            {
                if (exitCode == 0)
                {
                    _currentStep = SetupStep.AllSet;
                    _statusMessage = AgentLocalization.GetFormat("wizard_install_success", _resolvedPythonCmd, _pythonVersion);
                }
                else
                {
                    _currentStep = SetupStep.DependenciesError;
                    _statusMessage = AgentLocalization.Get("wizard_install_failed", "<b><color=red>依赖安装失败</color></b>\n可能是因为网络问题或 pip 未正确配置。");
                    _errorMessage = AgentLocalization.GetFormat("wizard_install_failed_err", error, output, _resolvedPythonCmd);
                }
            }, 60000);
        }

        // ------------------ 异步进程执行封装 ------------------

        private void RunCommandAsync(string command, string arguments, Action<int, string, string> onExited, double timeoutMs = 8000)
        {
            _processOutput = "";
            _processError = "";
            _onProcessExited = onExited;
            _processStartTime = EditorApplication.timeSinceStartup;
            _processTimeoutMs = timeoutMs;
            _isProcessTimedOut = false;

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                _activeProcess = new Process();
                _activeProcess.StartInfo = startInfo;
                _activeProcess.EnableRaisingEvents = true;

                _activeProcess.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null) _processOutput += e.Data + "\n";
                };
                _activeProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null) _processError += e.Data + "\n";
                };

                _activeProcess.Start();
                _activeProcess.BeginOutputReadLine();
                _activeProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                _activeProcess = null;
                // 通常是因为找不到 command (如 python 不在环境变量中)
                _onProcessExited?.Invoke(-1, "", ex.Message);
            }
        }
    }
}
