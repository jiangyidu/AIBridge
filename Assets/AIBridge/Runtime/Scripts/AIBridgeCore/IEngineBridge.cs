using System;

namespace AIBridge.Core
{
    /// <summary>
    /// 引擎桥接核心接口。
    /// 
    /// 每个目标宿主（Unity, AutoCAD, Tekla, UE4, ...）都需要实现此接口。
    /// Python Agent 通过 HTTP 协议调用这些能力，不直接依赖任何引擎 API。
    /// 
    /// 2026-06-16: 从 AgentBridge.cs / SceneObserver.cs / CompileWatcher.cs 提取的
    /// 引擎无关契约，为跨平台移植提供统一的编程接口。
    /// </summary>
    public interface IEngineBridge
    {
        // ─── 引擎信息 ──────────────────────────────────────────────

        /// <summary>引擎名称（如 "Unity", "AutoCAD", "Tekla"）。</summary>
        string EngineName { get; }

        /// <summary>引擎版本号。</summary>
        string EngineVersion { get; }

        /// <summary>当前项目/文件路径。</summary>
        string ProjectPath { get; }

        /// <summary>项目数据目录路径（Unity: Application.dataPath, AutoCAD: 文档路径）。</summary>
        string DataPath { get; }

        // ─── HTTP 桥接生命周期 ──────────────────────────────────────

        /// <summary>启动 HTTP 桥接服务。</summary>
        bool StartServer();

        /// <summary>停止 HTTP 桥接服务。</summary>
        void StopServer();

        /// <summary>HTTP 桥接服务是否正在运行。</summary>
        bool IsServerRunning { get; }

        /// <summary>当前活跃的 HTTP 端口号。</summary>
        int ActivePort { get; }

        // ─── 主线程调度 ────────────────────────────────────────────

        /// <summary>
        /// 将一个操作排入主线程队列。
        /// 大多数引擎 API 必须在主线程调用，HTTP 请求到达后通过此方法转发。
        /// </summary>
        void EnqueueMainThread(Action action);

        // ─── 场景/文档查询 ─────────────────────────────────────────

        /// <summary>获取当前场景/文档的层级结构 JSON。</summary>
        string QueryScene();

        /// <summary>获取指定对象的详细信息 JSON（通过名称/Handle/ID 标识）。</summary>
        string QueryObject(string identifier);

        /// <summary>搜索项目中的资产/资源。</summary>
        string FindAssets(string filter);

        // ─── 命令执行 ──────────────────────────────────────────────

        /// <summary>
        /// 通过反射执行已注册的静态命令方法。
        /// 所有 [AgentCommand] 标注的方法都可通过此接口调用。
        /// </summary>
        string ExecuteCommand(string className, string methodName, string[] args);

        // ─── 编译系统 ──────────────────────────────────────────────

        /// <summary>当前是否正在编译。</summary>
        bool IsCompiling { get; }

        /// <summary>最近一次编译是否成功。</summary>
        bool LastCompileSucceeded { get; }

        /// <summary>获取编译错误的 JSON 字符串。</summary>
        string GetCompileErrors();

        /// <summary>
        /// 触发资产/脚本刷新（Unity: AssetDatabase.Refresh, AutoCAD: N/A）。
        /// </summary>
        void RefreshAssets();

        // ─── 日志 ──────────────────────────────────────────────────

        /// <summary>获取最近 N 条日志的 JSON 字符串。</summary>
        string GetRecentLogs(int count);
    }
}
