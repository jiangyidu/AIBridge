using UnityEngine;
using AIBridge.Core;

namespace AIBridge.Agent
{
    /// <summary>
    /// Unity 平台的 IEngineBridge 具体实现。
    /// 通过委托给原有 AgentBridge、SceneObserver 等静态工具类，完成核心功能的接口封装。
    /// </summary>
    public class UnityEngineBridge : IEngineBridge
    {
        private static UnityEngineBridge _instance;
        public static UnityEngineBridge Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new UnityEngineBridge();
                return _instance;
            }
        }

        public string QueryScene()
        {
            return SceneObserver.GetSceneHierarchy();
        }

        public string QueryObject(string identifier)
        {
            return SceneObserver.GetGameObjectInfo(identifier);
        }

        public string FindAssets(string filter)
        {
            return SceneObserver.FindAssets(filter);
        }

        public string GetCompileErrors()
        {
            return CompileWatcher.GetErrorsJson();
        }

        public string GetRecentLogs(int count)
        {
            return AgentLogBuffer.GetRecentLogsJson(count);
        }
    }

    /// <summary>
    /// Unity 平台的 IBridgeLogger 日志实现。
    /// </summary>
    public class UnityBridgeLogger : IBridgeLogger
    {
        public void Log(string message) => Debug.Log(message);
        public void LogWarning(string message) => Debug.LogWarning(message);
        public void LogError(string message) => Debug.LogError(message);
    }
}
