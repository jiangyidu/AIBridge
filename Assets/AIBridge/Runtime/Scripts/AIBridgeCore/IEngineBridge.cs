namespace AIBridge.Core
{
    /// <summary>
    /// 引擎桥接核心接口。
    /// 
    /// 每个目标宿主（Unity, AutoCAD, Tekla, UE4, ...）都需要实现此接口。
    /// Editor-only C# Agent 通过此抽象调用宿主能力，不直接依赖具体引擎实现。
    /// 
    /// 2026-06-16: 从 AgentBridge.cs / SceneObserver.cs / CompileWatcher.cs 提取的
    /// 引擎无关契约，为跨平台移植提供统一的编程接口。
    /// </summary>
    public interface IEngineBridge
    {
        // ─── 场景/文档查询 ─────────────────────────────────────────

        /// <summary>获取当前场景/文档的层级结构 JSON。</summary>
        string QueryScene();

        /// <summary>获取指定对象的详细信息 JSON（通过名称/Handle/ID 标识）。</summary>
        string QueryObject(string identifier);

        /// <summary>搜索项目中的资产/资源。</summary>
        string FindAssets(string filter);

        /// <summary>获取编译错误的 JSON 字符串。</summary>
        string GetCompileErrors();

        // ─── 日志 ──────────────────────────────────────────────────

        /// <summary>获取最近 N 条日志的 JSON 字符串。</summary>
        string GetRecentLogs(int count);
    }
}
