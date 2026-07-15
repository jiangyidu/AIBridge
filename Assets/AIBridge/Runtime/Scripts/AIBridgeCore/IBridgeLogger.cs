namespace AIBridge.Core
{
    /// <summary>
    /// 引擎无关的日志抽象接口。
    /// 
    /// 替换各模块中对 UnityEngine.Debug.Log 的直接依赖，
    /// 使 AgentCommandRegistry、AgentJson 等通用模块可以在非 Unity 环境中运行。
    /// 
    /// 各引擎实现：
    ///   Unity   → Debug.Log / Debug.LogWarning / Debug.LogError
    ///   AutoCAD → Editor.WriteMessage / System.Diagnostics.Debug
    ///   Tekla   → System.Diagnostics.Debug / 自定义日志
    /// </summary>
    public interface IBridgeLogger
    {
        void Log(string message);
        void LogWarning(string message);
        void LogError(string message);
    }

    /// <summary>
    /// 默认控制台日志实现（用于非引擎环境下的测试或独立运行）。
    /// </summary>
    public class ConsoleBridgeLogger : IBridgeLogger
    {
        public static readonly ConsoleBridgeLogger Instance = new ConsoleBridgeLogger();

        public void Log(string message) => System.Console.WriteLine("[AIBridge] " + message);
        public void LogWarning(string message) => System.Console.WriteLine("[AIBridge WARN] " + message);
        public void LogError(string message) => System.Console.Error.WriteLine("[AIBridge ERROR] " + message);
    }

    /// <summary>
    /// 全局日志代理。各引擎在初始化时设置 BridgeLog.Logger。
    /// 通用模块中使用 BridgeLog.Log() 而非 UnityEngine.Debug.Log()。
    /// </summary>
    public static class BridgeLog
    {
        private static IBridgeLogger _logger = ConsoleBridgeLogger.Instance;

        /// <summary>
        /// 设置全局日志实现。引擎插件在启动时调用一次。
        /// 例如 Unity 端: BridgeLog.SetLogger(new UnityBridgeLogger());
        /// </summary>
        public static void SetLogger(IBridgeLogger logger)
        {
            _logger = logger ?? ConsoleBridgeLogger.Instance;
        }

        public static void Log(string message) => _logger.Log(message);
        public static void LogWarning(string message) => _logger.LogWarning(message);
        public static void LogError(string message) => _logger.LogError(message);
    }
}
