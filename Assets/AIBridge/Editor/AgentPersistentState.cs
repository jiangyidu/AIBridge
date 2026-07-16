using System;
using System.Collections.Generic;

namespace AIBridge.Agent
{
    /// <summary>
    /// 纯 C# Agent 的持久化阶段。字符串常量用于保证旧版本状态文件可迁移，
    /// 避免枚举顺序变化导致恢复到错误阶段。
    /// </summary>
    public static class AgentRunPhase
    {
        public const string Idle = "Idle";
        public const string RequestingLlm = "RequestingLlm";
        public const string WaitingLlm = "WaitingLlm";
        public const string ExecutingTools = "ExecutingTools";
        public const string ExecutingPreparedTool = "ExecutingPreparedTool";
        public const string StartingCompile = "StartingCompile";
        public const string WaitingCompile = "WaitingCompile";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
    }

    [Serializable]
    public sealed class AgentToolCallState
    {
        public string id = "";
        public string name = "";
        public string argumentsJson = "{}";
    }

    [Serializable]
    public sealed class AgentMessageState
    {
        public string role = "";
        public string content = "";
        public string reasoningContent = "";
        public string toolCallId = "";
        public string name = "";
        public List<AgentToolCallState> toolCalls = new List<AgentToolCallState>();
    }

    [Serializable]
    public sealed class AgentToolExecutionState
    {
        public string operationId = "";
        public string toolCallId = "";
        public string toolName = "";
        public string argumentsJson = "{}";
        public string status = ""; // Prepared / Committed / Uncertain
        public string resultJson = "";
    }

    /// <summary>
    /// 可以跨 Domain Reload 恢复的 Agent 会话。敏感 API Key 不得写入此对象。
    /// </summary>
    [Serializable]
    public sealed class AgentSessionState
    {
        public int schemaVersion = 2;
        public string sessionId = "";
        public bool active;
        public bool cancelRequested;
        public string phase = AgentRunPhase.Idle;
        public int step;
        public int maxSteps = 30;
        public string provider = "";
        public string apiUrl = "";
        public string model = "";
        public bool requireApiKey;
        public string language = "zh";
        public string userSystemPrompt = "";
        public string unityVersion = "";
        public string projectPath = "";
        public List<AgentMessageState> messages = new List<AgentMessageState>();
        public List<AgentToolCallState> pendingToolCalls = new List<AgentToolCallState>();
        public int toolCallIndex;
        public AgentToolExecutionState pendingExecution;
        public string compileOperationId = "";
        public string requestId = "";
        public int llmAttempt;
        public long notBeforeUtcTicks;
        public string status = "";
        public string lastError = "";
        public long revision;
        public long updatedUtcTicks;
    }

    /// <summary>启动新会话所需的非敏感配置；API Key 只存在内存中。</summary>
    public sealed class AgentSessionConfig
    {
        public string provider = "";
        public string apiUrl = "";
        public string apiKey = "";
        public bool requireApiKey;
        public string model = "";
        public string language = "zh";
        public string userSystemPrompt = "";
        public int maxSteps = 30;
    }

    [Serializable]
    public sealed class AgentCompilerErrorState
    {
        public string file = "";
        public int line;
        public int column;
        public string message = "";
    }

    public static class CompileTransactionPhase
    {
        public const string Prepared = "Prepared";
        public const string AwaitingCompilation = "AwaitingCompilation";
        public const string CompilationFailed = "CompilationFailed";
        public const string CompiledAwaitingReload = "CompiledAwaitingReload";
        public const string RollingBack = "RollingBack";
        public const string AwaitingRollbackCompilation = "AwaitingRollbackCompilation";
        public const string RollbackCompiledAwaitingReload = "RollbackCompiledAwaitingReload";
        public const string Succeeded = "Succeeded";
        public const string FailedRestored = "FailedRestored";
        public const string FailedRollback = "FailedRollback";
        public const string FailedConflict = "FailedConflict";
        public const string TimedOut = "TimedOut";
    }

    [Serializable]
    public sealed class AgentCompileTransactionState
    {
        public int schemaVersion = 1;
        public string operationId = "";
        public string phase = CompileTransactionPhase.Prepared;
        public string sourcePath = "";
        public string assetPath = "";
        public string backupPath = "";
        public bool sourceExisted;
        public string generatedHash = "";
        public string originalHash = "";
        public string targetKind = ""; // method / type
        public string targetName = "";
        public string targetTypeName = "";
        public string originDomainId = "";
        public string compileFinishedDomainId = "";
        public bool compileCycleStarted;
        public bool compileCycleFinished;
        public bool rollbackCycle;
        public List<AgentCompilerErrorState> currentErrors = new List<AgentCompilerErrorState>();
        public List<AgentCompilerErrorState> originalErrors = new List<AgentCompilerErrorState>();
        public string detail = "";
        public long startedUtcTicks;
        public long phaseStartedUtcTicks;
        public long updatedUtcTicks;
    }
}
