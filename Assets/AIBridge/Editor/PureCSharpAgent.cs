using System;
using System.Collections.Generic;
using AIBridge.Core;
using LitJson;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>
    /// 纯 C#、可跨 Domain Reload 恢复的 Agent 调度器。
    /// 不把长期主循环寄托在 Task/线程/EditorWindow 实例上，每次 Editor update 只推进一个状态。
    /// </summary>
    [InitializeOnLoad]
    public static class PureCSharpAgent
    {
        private const int MaxLlmAttempts = 3;
        private const double FailedSessionRetrySeconds = 5.0;
        private static AgentSessionState _state;
        private static AgentLlmOperation _llmOperation;
        private static string _inMemoryApiKey = "";
        private static bool _resumeHandled;
        private static bool _sessionLoadFailed;
        private static double _nextSessionLoadAttempt;

        public static event Action StateChanged;

        static PureCSharpAgent()
        {
            LoadSessionIntoMemory();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EditorApplication.delayCall += ResumeAfterReload;
        }

        public static bool IsRunning
        {
            get { return _state != null && _state.active; }
        }

        public static string SessionId
        {
            get { return _state == null ? "" : _state.sessionId; }
        }

        public static string Status
        {
            get { return _state == null ? "" : _state.status; }
        }

        public static int CurrentStep
        {
            get { return _state == null ? 0 : _state.step; }
        }

        public static AgentSessionState CurrentState
        {
            get { return _state; }
        }

        public static bool HasStateRecoveryFailure
        {
            get { return _sessionLoadFailed; }
        }

        public static string StateRecoveryError
        {
            get { return _sessionLoadFailed && _state != null ? _state.lastError : ""; }
        }

        public static bool StartSession(
            AgentSessionConfig config,
            IList<AgentMessageState> conversation,
            out string error)
        {
            error = "";
            if (_sessionLoadFailed)
            {
                error = "现有 Agent 状态恢复失败。为避免覆盖未完成任务，已阻止启动新会话：" + StateRecoveryError;
                return false;
            }
            if (config == null)
            {
                error = "Agent 配置不能为空";
                return false;
            }
            if (string.IsNullOrEmpty(config.apiUrl))
            {
                error = "LLM 接口地址不能为空";
                return false;
            }
            if (string.IsNullOrEmpty(config.model))
            {
                error = "模型名称不能为空";
                return false;
            }

            config.apiKey = AgentEncryptionUtility.NormalizeApiKey(config.apiKey);
            if (config.requireApiKey && string.IsNullOrEmpty(config.apiKey))
            {
                error = "当前远程服务需要 API Key。请展开“配置”，重新输入有效密钥后再发送。";
                return false;
            }

            DisposeLlmOperation();
            _inMemoryApiKey = config.apiKey ?? "";

            AgentSessionState state = new AgentSessionState();
            state.sessionId = Guid.NewGuid().ToString("N");
            state.active = true;
            state.phase = AgentRunPhase.RequestingLlm;
            state.maxSteps = Math.Max(1, Math.Min(80, config.maxSteps));
            state.provider = config.provider ?? "";
            state.apiUrl = config.apiUrl ?? "";
            state.model = config.model ?? "";
            state.requireApiKey = config.requireApiKey;
            state.language = config.language ?? "zh";
            state.userSystemPrompt = config.userSystemPrompt ?? "";
            state.unityVersion = Application.unityVersion;
            state.projectPath = AgentStateStore.ProjectRoot;
            state.status = "Agent 会话准备就绪";

            AgentMessageState system = new AgentMessageState();
            system.role = "system";
            system.content = AgentPromptBuilder.Build(config, state.unityVersion, state.projectPath);
            state.messages.Add(system);

            if (conversation != null)
            {
                for (int i = 0; i < conversation.Count; i++)
                {
                    AgentMessageState source = conversation[i];
                    if (source == null || (source.role != "user" && source.role != "assistant")) continue;
                    AgentMessageState copy = new AgentMessageState();
                    copy.role = source.role;
                    copy.content = source.content ?? "";
                    copy.reasoningContent = source.reasoningContent ?? "";
                    state.messages.Add(copy);
                }
            }

            if (state.messages.Count < 2 || state.messages[state.messages.Count - 1].role != "user")
            {
                error = "会话中没有待处理的用户消息";
                return false;
            }

            _state = state;
            _sessionLoadFailed = false;
            SaveState();
            AgentStateStore.AppendHistory("system", "[C# Agent] 会话已启动：" + state.sessionId, "");
            return true;
        }

        public static void Cancel()
        {
            if (_state == null || !_state.active) return;
            _state.cancelRequested = true;
            _state.status = "正在取消 Agent 会话";
            if (_llmOperation != null) _llmOperation.Abort();
            SaveState();
        }

        private static void ResumeAfterReload()
        {
            if (_resumeHandled) return;
            _resumeHandled = true;
            if (!LoadSessionIntoMemory() || _state == null || !_state.active)
            {
                NotifyChanged();
                return;
            }

            ResumeLoadedActiveSession();
            NotifyChanged();
        }

        private static void ResumeLoadedActiveSession()
        {
            if (_state == null || !_state.active) return;

            if (_state.phase == AgentRunPhase.WaitingLlm)
            {
                if (_state.llmAttempt >= MaxLlmAttempts)
                {
                    Fail("LLM 请求在脚本重载时中断，已达到最大安全重试次数");
                    return;
                }
                _state.phase = AgentRunPhase.RequestingLlm;
                _state.notBeforeUtcTicks = DateTime.UtcNow.AddSeconds(1).Ticks;
                _state.status = "脚本重载中断了网络请求，准备有限重试";
                SaveState();
                AgentStateStore.AppendHistory("system", "[恢复] Domain Reload 中断了 LLM 请求，将使用同一会话状态重试。", "");
            }
            else if (_state.phase == AgentRunPhase.StartingCompile ||
                     _state.phase == AgentRunPhase.WaitingCompile)
            {
                AgentCompileTransactionState compile = AgentStateStore.LoadCompile();
                if (compile != null && compile.operationId == _state.compileOperationId)
                {
                    _state.phase = AgentRunPhase.WaitingCompile;
                    _state.status = "已在新脚本域中恢复编译事务";
                    SaveState();
                }
                else
                {
                    // 编译工具在真正写文件前被外部重载打断；它带有 operationId，可安全重试。
                    _state.phase = AgentRunPhase.ExecutingTools;
                    _state.status = "编译尚未启动，准备使用原操作 ID 继续";
                    SaveState();
                }
            }
            else if (_state.phase == AgentRunPhase.ExecutingPreparedTool)
            {
                CompleteUncertainPreparedTool();
            }
        }

        private static void BeforeAssemblyReload()
        {
            // 恢复失败占位状态只存在内存，绝不能覆盖原始损坏文件及其备份。
            if (!_sessionLoadFailed && _state != null) AgentStateStore.SaveSession(_state);
            DisposeLlmOperation();
        }

        private static void Tick()
        {
            CompileTransactionManager.Tick();
            if (_sessionLoadFailed)
            {
                RetryFailedSessionLoad();
                return;
            }
            if (_state == null)
            {
                if (EditorApplication.timeSinceStartup < _nextSessionLoadAttempt) return;
                if (!LoadSessionIntoMemory()) return;
            }
            if (_state == null || !_state.active) return;

            if (_state.cancelRequested)
            {
                CancelNow();
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _state.status = "进入 Play Mode 前暂停 Agent";
                return;
            }

            if (_state.phase == AgentRunPhase.RequestingLlm)
                BeginLlmRequest();
            else if (_state.phase == AgentRunPhase.WaitingLlm)
                PollLlmRequest();
            else if (_state.phase == AgentRunPhase.ExecutingTools)
                ExecuteNextTool();
            else if (_state.phase == AgentRunPhase.WaitingCompile ||
                     _state.phase == AgentRunPhase.StartingCompile)
                PollCompileTransaction();
            else if (_state.phase == AgentRunPhase.ExecutingPreparedTool)
                CompleteUncertainPreparedTool();
        }

        private static bool LoadSessionIntoMemory()
        {
            AgentSessionState loaded = AgentStateStore.LoadSession();
            if (loaded != null)
            {
                _state = loaded;
                _sessionLoadFailed = false;
                _nextSessionLoadAttempt = 0;
                return true;
            }

            string error = AgentStateStore.LastSessionLoadError;
            if (AgentStateStore.HasSessionFile && !string.IsNullOrEmpty(error))
            {
                SetSessionLoadFailure(error);
            }
            else
            {
                _state = null;
                _sessionLoadFailed = false;
                _nextSessionLoadAttempt = EditorApplication.timeSinceStartup + 1.0;
            }
            return false;
        }

        private static void RetryFailedSessionLoad()
        {
            if (EditorApplication.timeSinceStartup < _nextSessionLoadAttempt) return;
            AgentSessionState recovered = AgentStateStore.LoadSession();
            if (recovered == null)
            {
                SetSessionLoadFailure(AgentStateStore.LastSessionLoadError);
                return;
            }

            _state = recovered;
            _sessionLoadFailed = false;
            _nextSessionLoadAttempt = 0;
            if (_state.active) ResumeLoadedActiveSession();
            NotifyChanged();
        }

        private static void SetSessionLoadFailure(string error)
        {
            _sessionLoadFailed = true;
            _nextSessionLoadAttempt = EditorApplication.timeSinceStartup + FailedSessionRetrySeconds;
            AgentSessionState failed = new AgentSessionState();
            failed.active = false;
            failed.phase = AgentRunPhase.Failed;
            failed.status = "Agent 状态恢复失败；保留原文件并低频重试";
            failed.lastError = string.IsNullOrEmpty(error) ? "未知状态读取错误" : error;
            _state = failed;
        }

        private static void BeginLlmRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (_state.notBeforeUtcTicks > DateTime.UtcNow.Ticks) return;
            bool retryingSameDecision = !string.IsNullOrEmpty(_state.requestId);
            if (!retryingSameDecision && _state.step >= _state.maxSteps)
            {
                Fail("已达到最大步骤数 " + _state.maxSteps + "，为避免无限循环已停止");
                return;
            }
            if (_state.llmAttempt >= MaxLlmAttempts)
            {
                Fail("LLM 请求已连续失败 " + MaxLlmAttempts + " 次");
                return;
            }

            if (!retryingSameDecision) _state.step++;
            _state.llmAttempt++;
            if (!retryingSameDecision) _state.requestId = Guid.NewGuid().ToString("N");
            _state.phase = AgentRunPhase.WaitingLlm;
            _state.status = "步骤 " + _state.step + "/" + _state.maxSteps + "：请求 LLM 决策";
            SaveState();
            AgentStateStore.AppendHistory("system", "[步骤 " + _state.step + "/" + _state.maxSteps + "] 请求大语言模型决策", "");

            try
            {
                string apiKey = ResolveApiKey();
                if (_state.requireApiKey && string.IsNullOrEmpty(apiKey))
                {
                    Fail("当前远程服务的 API Key 为空或本地密钥已无法解密。请展开“配置”，重新输入有效密钥后再发送。不会自动重试。");
                    return;
                }
                _llmOperation = AgentLlmClient.Start(_state, apiKey);
            }
            catch (Exception ex)
            {
                ScheduleLlmRetry("启动 LLM 请求失败: " + ex.Message);
            }
        }

        private static void PollLlmRequest()
        {
            if (_llmOperation == null)
            {
                // 只有 Domain Reload 或异常恢复会进入此分支。
                ScheduleLlmRetry("LLM 请求对象已丢失，可能发生了脚本重载");
                return;
            }
            if (!_llmOperation.IsDone) return;

            AgentLlmResponse response = _llmOperation.GetResponse();
            DisposeLlmOperation();
            if (!response.success)
            {
                if (response.retryable)
                    ScheduleLlmRetry(response.error);
                else
                    Fail(BuildNonRetryableLlmError(response));
                return;
            }

            AgentMessageState assistant = new AgentMessageState();
            assistant.role = "assistant";
            assistant.content = response.content ?? "";
            assistant.reasoningContent = response.reasoningContent ?? "";
            assistant.toolCalls = response.toolCalls ?? new List<AgentToolCallState>();
            _state.messages.Add(assistant);
            _state.llmAttempt = 0;
            _state.requestId = "";

            if (assistant.toolCalls.Count == 0)
            {
                string finalText = string.IsNullOrEmpty(assistant.content)
                    ? "模型没有返回文本或工具调用。" : assistant.content;
                AgentStateStore.AppendHistory("assistant", finalText, assistant.reasoningContent);
                _state.active = false;
                _state.phase = AgentRunPhase.Completed;
                _state.status = "Agent 会话完成";
                SaveState();
                return;
            }

            if (!string.IsNullOrEmpty(assistant.content))
                AgentStateStore.AppendHistory("system", "[模型计划] " + assistant.content, assistant.reasoningContent);
            _state.pendingToolCalls = assistant.toolCalls;
            _state.toolCallIndex = 0;
            _state.pendingExecution = null;
            _state.phase = AgentRunPhase.ExecutingTools;
            _state.status = "模型请求执行 " + assistant.toolCalls.Count + " 个工具";
            SaveState();
        }

        private static void ExecuteNextTool()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (_state.pendingToolCalls == null || _state.toolCallIndex >= _state.pendingToolCalls.Count)
            {
                _state.pendingToolCalls = new List<AgentToolCallState>();
                _state.toolCallIndex = 0;
                _state.pendingExecution = null;
                _state.phase = AgentRunPhase.RequestingLlm;
                _state.notBeforeUtcTicks = 0;
                _state.status = "工具批次完成，准备下一步决策";
                SaveState();
                return;
            }

            AgentToolCallState call = _state.pendingToolCalls[_state.toolCallIndex];
            bool compileTool = IsCompileTool(call.name);
            if (_state.pendingExecution == null)
            {
                _state.pendingExecution = new AgentToolExecutionState();
                _state.pendingExecution.operationId = Guid.NewGuid().ToString("N");
                _state.pendingExecution.toolCallId = call.id;
                _state.pendingExecution.toolName = call.name;
                _state.pendingExecution.argumentsJson = call.argumentsJson ?? "{}";
                _state.pendingExecution.status = "Prepared";
            }

            if (compileTool)
            {
                _state.compileOperationId = _state.pendingExecution.operationId;
                _state.phase = AgentRunPhase.StartingCompile;
            }
            else
            {
                _state.phase = AgentRunPhase.ExecutingPreparedTool;
            }
            _state.status = "执行工具: " + call.name;
            SaveState();

            string argsJson = AddOperationId(call.argumentsJson, _state.pendingExecution.operationId);
            AgentStateStore.AppendHistory("system", "[步骤 " + _state.step + "] 调用工具 `" + call.name + "`\n```json\n" + argsJson + "\n```", "");

            string result;
            try { result = AgentToolRouter.ExecuteTool(call.name, argsJson); }
            catch (Exception ex) { result = AgentJson.Error("工具执行异常: " + ex.Message, ex); }

            if (compileTool && IsCompilingResult(result))
            {
                _state.phase = AgentRunPhase.WaitingCompile;
                _state.status = "等待 Unity 编译并完成 Domain Reload";
                SaveState();
                return;
            }

            CompleteTool(result);
        }

        private static void PollCompileTransaction()
        {
            if (string.IsNullOrEmpty(_state.compileOperationId))
            {
                CompleteTool(AgentJson.Error("编译阶段缺少 operationId，已停止自动重放"));
                return;
            }

            bool final;
            string result = CompileTransactionManager.GetStatusJson(_state.compileOperationId, out final);
            if (!final)
            {
                AgentCompileTransactionState compile = AgentStateStore.LoadCompile();
                if (compile != null && !string.IsNullOrEmpty(compile.detail))
                    _state.status = compile.detail;
                return;
            }

            string completedOperation = _state.compileOperationId;
            CompleteTool(result);
            CompileTransactionManager.Consume(completedOperation);
        }

        private static void CompleteTool(string resultJson)
        {
            AgentToolExecutionState execution = _state.pendingExecution;
            AgentToolCallState call = _state.pendingToolCalls != null && _state.toolCallIndex < _state.pendingToolCalls.Count
                ? _state.pendingToolCalls[_state.toolCallIndex] : null;

            AgentMessageState tool = new AgentMessageState();
            tool.role = "tool";
            tool.toolCallId = execution == null ? (call == null ? "" : call.id) : execution.toolCallId;
            tool.name = execution == null ? (call == null ? "" : call.name) : execution.toolName;
            tool.content = string.IsNullOrEmpty(resultJson) ? AgentJson.Error("工具没有返回结果") : resultJson;
            _state.messages.Add(tool);

            if (execution != null)
            {
                execution.status = "Committed";
                execution.resultJson = tool.content;
            }
            AgentStateStore.AppendHistory("system", "[步骤 " + _state.step + "] `" + tool.name + "` 返回：\n```json\n" + tool.content + "\n```", "");

            _state.toolCallIndex++;
            _state.pendingExecution = null;
            _state.compileOperationId = "";
            _state.phase = AgentRunPhase.ExecutingTools;
            _state.status = "工具结果已提交，继续当前批次";
            SaveState();
        }

        private static void CompleteUncertainPreparedTool()
        {
            if (_state.pendingExecution == null)
            {
                _state.phase = AgentRunPhase.ExecutingTools;
                SaveState();
                return;
            }

            JsonData result = AgentJson.NewObject();
            result["Success"] = false;
            result["Uncertain"] = true;
            result["Message"] = "普通工具在执行提交之间发生 Domain Reload。为避免重复副作用，宿主没有自动重放；请先探查当前场景/资源状态再决定下一步。";
            _state.pendingExecution.status = "Uncertain";
            CompleteTool(JsonMapper.ToJson(result));
        }

        private static void ScheduleLlmRetry(string reason)
        {
            DisposeLlmOperation();
            if (_state.llmAttempt >= MaxLlmAttempts)
            {
                Fail(reason);
                return;
            }
            _state.phase = AgentRunPhase.RequestingLlm;
            _state.notBeforeUtcTicks = DateTime.UtcNow.AddSeconds(Math.Max(1, _state.llmAttempt * 2)).Ticks;
            _state.status = "LLM 请求失败，准备第 " + (_state.llmAttempt + 1) + " 次尝试";
            _state.lastError = reason ?? "";
            SaveState();
            AgentStateStore.AppendHistory("system", "[网络重试] " + reason, "");
        }

        private static string BuildNonRetryableLlmError(AgentLlmResponse response)
        {
            string provider = string.IsNullOrEmpty(_state.provider) ? "远程 API" : _state.provider;
            if (response.failureKind == "Authentication")
            {
                return "API 认证失败（HTTP " + response.statusCode + "）。当前服务商：" + provider +
                       "，接口：" + _state.apiUrl + "。请展开“配置”，删除旧值后重新粘贴该服务商签发的有效 API Key。" +
                       "认证错误不会自动重试。";
            }
            if (response.failureKind == "Billing")
                return "API 账户余额或额度不足（HTTP 402）。请检查 " + provider + " 账户后重试；本错误不会自动重试。";
            if (response.failureKind == "InvalidRequest")
                return "LLM 请求配置无效。服务商：" + provider + "，模型：" + _state.model +
                       "，接口：" + _state.apiUrl + "。" + response.error + "；本错误不会自动重试。";
            return response.error + "；该错误不会自动重试。";
        }

        private static void CancelNow()
        {
            DisposeLlmOperation();
            _state.active = false;
            _state.cancelRequested = false;
            _state.phase = AgentRunPhase.Cancelled;
            _state.status = "Agent 会话已取消";
            SaveState();
            AgentStateStore.AppendHistory("system", "[系统] 用户终止了 C# Agent 会话。编译事务若已开始，将继续完成安全回滚。", "");
        }

        private static void Fail(string message)
        {
            DisposeLlmOperation();
            if (_state == null) return;
            _state.active = false;
            _state.phase = AgentRunPhase.Failed;
            _state.status = "Agent 会话失败";
            _state.lastError = message ?? "未知错误";
            SaveState();
            AgentStateStore.AppendHistory("assistant", "[C# Agent 错误] " + _state.lastError, "");
        }

        private static string ResolveApiKey()
        {
            if (!string.IsNullOrEmpty(_inMemoryApiKey))
                return AgentEncryptionUtility.NormalizeApiKey(_inMemoryApiKey);
            return AgentEncryptionUtility.NormalizeApiKey(
                AgentEncryptionUtility.Decrypt(EditorPrefs.GetString("AIAss_Api_Key", "")));
        }

        private static string AddOperationId(string argumentsJson, string operationId)
        {
            try
            {
                JsonData args = AgentJson.ParseObject(argumentsJson);
                if (!args.IsObject) args = AgentJson.NewObject();
                args["_operationId"] = operationId ?? "";
                return JsonMapper.ToJson(args);
            }
            catch
            {
                JsonData args = AgentJson.NewObject();
                args["_operationId"] = operationId ?? "";
                args["_raw_arguments"] = argumentsJson ?? "";
                return JsonMapper.ToJson(args);
            }
        }

        private static bool IsCompileTool(string toolName)
        {
            return toolName == BridgeProtocol.TOOL_COMPILE_TEMP_METHOD ||
                   toolName == BridgeProtocol.TOOL_COMPILE_SCRIPT;
        }

        private static bool IsCompilingResult(string json)
        {
            try
            {
                JsonData data = AgentJson.ParseObject(json);
                return AgentJson.GetString(data, "Status") == "compiling";
            }
            catch { return false; }
        }

        private static void SaveState()
        {
            if (_sessionLoadFailed) return;
            AgentStateStore.SaveSession(_state);
            NotifyChanged();
        }

        private static void NotifyChanged()
        {
            Action handler = StateChanged;
            if (handler == null) return;
            try { handler(); }
            catch { }
        }

        private static void DisposeLlmOperation()
        {
            if (_llmOperation == null) return;
            try { _llmOperation.Dispose(); }
            catch { }
            _llmOperation = null;
        }
    }
}
