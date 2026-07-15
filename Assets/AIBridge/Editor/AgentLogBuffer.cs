using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace AIBridge.Agent
{
    /// <summary>
    /// 日志缓冲区：静默监听 Unity Application.logMessageReceived，
    /// 保留最近 N 条日志，供 Agent 查询运行时输出（Debug.Log / LogError 等）。
    /// Agent 可通过 get_console_logs 工具读取，用于诊断 Play Mode 中的运行时问题。
    /// </summary>
    [InitializeOnLoad]
    public static class AgentLogBuffer
    {
        // ─── 内部数据结构 ────────────────────────────────────
        private class LogEntry
        {
            public string Message;
            public string StackTrace;
            public LogType Type;
            public double Timestamp;  // EditorApplication.timeSinceStartup
        }

        private static readonly Queue<LogEntry> _buffer = new Queue<LogEntry>();
        private static readonly object _lock = new object();
        private const int MAX_SIZE = 100;
        // ─────────────────────────────────────────────────────

        static AgentLogBuffer()
        {
            Application.logMessageReceived += OnLogMessage;
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            lock (_lock)
            {
                if (_buffer.Count >= MAX_SIZE)
                    _buffer.Dequeue();

                _buffer.Enqueue(new LogEntry
                {
                    Message = condition,
                    StackTrace = stackTrace,
                    Type = type,
                    Timestamp = EditorApplication.timeSinceStartup
                });
            }
        }

        /// <summary>
        /// 获取最近 count 条日志的 JSON 字符串。
        /// 格式：[{"type":"Log","message":"..."},...]
        /// </summary>
        public static string GetRecentLogsJson(int count = 10)
        {
            var sb = new System.Text.StringBuilder("[");
            lock (_lock)
            {
                var entries = new List<LogEntry>(_buffer);
                int start = System.Math.Max(0, entries.Count - count);
                bool first = true;
                for (int i = start; i < entries.Count; i++)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    var e = entries[i];
                    sb.Append("{");
                    sb.Append($"\"type\":\"{e.Type}\",");
                    sb.Append($"\"message\":\"{AgentUtility.EscJ(e.Message)}\"");
                    // Error / Warning 附带调用栈（截断 512 字符避免超长）
                    if (e.Type == LogType.Error || e.Type == LogType.Exception ||
                        e.Type == LogType.Warning)
                    {
                        string st = e.StackTrace ?? "";
                        if (st.Length > 512) st = st.Substring(0, 512) + "...";
                        sb.Append($",\"stackTrace\":\"{AgentUtility.EscJ(st)}\"");
                    }
                    sb.Append("}");
                }
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// 清空日志缓冲区（可在任务开始前调用，防止历史日志干扰分析）。
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _buffer.Clear();
            }
        }
    }
}
