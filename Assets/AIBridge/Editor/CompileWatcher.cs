using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>
    /// 编译监视器：监听 Unity 编译事件，捕获编译错误并缓存，
    /// 供 Agent 循环在编译后查询精确的错误信息（文件 + 行号 + 消息）。
    /// 解决原有系统编译失败仅显示"超时"无法诊断的问题。
    /// </summary>
    [InitializeOnLoad]
    public static class CompileWatcher
    {
        // ─── 公开状态 ────────────────────────────────────────
        /// <summary>最近一次编译捕获到的错误列表。</summary>
        public static List<CompilerMessage> LastErrors { get; private set; } = new List<CompilerMessage>();
        /// <summary>最近一次编译是否成功（无 Error 级别消息）。</summary>
        public static bool LastCompileSucceeded { get; private set; } = true;
        /// <summary>最近一次编译完成的 EditorApplication.timeSinceStartup 时间戳。</summary>
        public static double LastCompileFinishTime { get; private set; }
        // ─────────────────────────────────────────────────────

        static CompileWatcher()
        {
            CompilationPipeline.assemblyCompilationFinished += OnCompilationFinished;
        }

        private static void OnCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            LastErrors.Clear();
            LastCompileSucceeded = true;

            foreach (var msg in messages)
            {
                if (msg.type == CompilerMessageType.Error)
                {
                    LastErrors.Add(msg);
                    LastCompileSucceeded = false;
                }
            }

            LastCompileFinishTime = EditorApplication.timeSinceStartup;

            if (!LastCompileSucceeded)
            {
                Debug.LogWarning($"[CompileWatcher] 编译失败，共 {LastErrors.Count} 个错误。" +
                                 $"请调用 CompileWatcher.GetErrorsJson() 获取详情。");
            }
        }

        /// <summary>
        /// 将最近一次编译错误序列化为 JSON 字符串，供 Agent 读取并交给 LLM 分析。
        /// 格式：{"Success":bool, "Errors":[{"File":"...","Line":N,"Column":N,"Message":"..."},...]}
        /// </summary>
        public static string GetErrorsJson()
        {
            if (LastCompileSucceeded)
                return "{\"Success\":true,\"Errors\":[]}";

            var sb = new System.Text.StringBuilder();
            sb.Append("{\"Success\":false,\"Errors\":[");
            for (int i = 0; i < LastErrors.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var err = LastErrors[i];
                sb.Append("{");
                sb.Append($"\"File\":\"{AgentUtility.EscJ(err.file)}\",");
                sb.Append($"\"Line\":{err.line},");
                sb.Append($"\"Column\":{err.column},");
                sb.Append($"\"Message\":\"{AgentUtility.EscJ(err.message)}\"");
                sb.Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// 重置错误状态（通常在开始新一次编译前调用）。
        /// </summary>
        public static void Reset()
        {
            LastErrors.Clear();
            LastCompileSucceeded = true;
        }
    }
}
