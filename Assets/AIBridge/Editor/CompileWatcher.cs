using System.Collections.Generic;
using LitJson;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>
    /// 按“完整编译周期”累计错误。旧实现会在每个 assemblyCompilationFinished 中清空列表，
    /// 导致早先程序集的错误被后续程序集覆盖。
    /// </summary>
    [InitializeOnLoad]
    public static class CompileWatcher
    {
        private static readonly List<CompilerMessage> CycleErrors = new List<CompilerMessage>();
        private static bool _cycleActive;
        private static bool _finishPending;

        public static List<CompilerMessage> LastErrors { get; private set; }
        public static bool LastCompileSucceeded { get; private set; }
        public static double LastCompileFinishTime { get; private set; }

        static CompileWatcher()
        {
            LastErrors = new List<CompilerMessage>();
            LastCompileSucceeded = true;
            // Unity 2018.4 只有 assembly 级事件。完整周期通过“首个开始 + isCompiling 结束”收口。
#pragma warning disable 0618 // 2023 标记过时，但 Unity 2018.4 必须使用该事件。
            CompilationPipeline.assemblyCompilationStarted -= OnAssemblyCompilationStarted;
            CompilationPipeline.assemblyCompilationStarted += OnAssemblyCompilationStarted;
#pragma warning restore 0618
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            EditorApplication.update -= PollCompilationFinished;
            EditorApplication.update += PollCompilationFinished;
        }

        private static void OnAssemblyCompilationStarted(string assemblyPath)
        {
            if (_cycleActive) return;
            _cycleActive = true;
            _finishPending = false;
            CycleErrors.Clear();
            LastErrors.Clear();
            LastCompileSucceeded = true;
            CompileTransactionManager.OnCompilationStarted();
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages != null)
            {
                for (int i = 0; i < messages.Length; i++)
                {
                    if (messages[i].type == CompilerMessageType.Error)
                        CycleErrors.Add(messages[i]);
                }
            }
            _finishPending = true;
            CompileTransactionManager.OnCompilationProgress(CycleErrors);
        }

        private static void PollCompilationFinished()
        {
            if (!_cycleActive || !_finishPending || EditorApplication.isCompiling) return;
            FinalizeCompilationCycle();
        }

        private static void FinalizeCompilationCycle()
        {
            LastErrors = new List<CompilerMessage>(CycleErrors);
            LastCompileSucceeded = LastErrors.Count == 0;
            LastCompileFinishTime = EditorApplication.timeSinceStartup;
            CompileTransactionManager.OnCompilationFinished(LastErrors);
            _cycleActive = false;
            _finishPending = false;
            if (!LastCompileSucceeded)
                Debug.LogWarning("[CompileWatcher] 完整编译周期失败，共 " + LastErrors.Count + " 个错误。");
        }

        public static string GetErrorsJson()
        {
            JsonData root = AgentJson.NewObject();
            JsonData errors = AgentJson.NewArray();
            for (int i = 0; i < LastErrors.Count; i++)
            {
                JsonData item = AgentJson.NewObject();
                item["File"] = LastErrors[i].file ?? "";
                item["Line"] = LastErrors[i].line;
                item["Column"] = LastErrors[i].column;
                item["Message"] = LastErrors[i].message ?? "";
                errors.Add(item);
            }

            if (errors.Count == 0)
            {
                AgentCompileTransactionState transaction = AgentStateStore.LoadCompile();
                if (transaction != null)
                {
                    IList<AgentCompilerErrorState> persisted = transaction.originalErrors != null && transaction.originalErrors.Count > 0
                        ? transaction.originalErrors : transaction.currentErrors;
                    if (persisted != null)
                    {
                        for (int i = 0; i < persisted.Count; i++)
                        {
                            JsonData item = AgentJson.NewObject();
                            item["File"] = persisted[i].file ?? "";
                            item["Line"] = persisted[i].line;
                            item["Column"] = persisted[i].column;
                            item["Message"] = persisted[i].message ?? "";
                            errors.Add(item);
                        }
                    }
                }
            }

            root["Success"] = errors.Count == 0;
            root["Errors"] = errors;
            return JsonMapper.ToJson(root);
        }

        public static void Reset()
        {
            CycleErrors.Clear();
            LastErrors.Clear();
            LastCompileSucceeded = true;
            _cycleActive = false;
            _finishPending = false;
        }
    }
}
