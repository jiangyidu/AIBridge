using System;
using System.IO;
using UnityEditor;
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

        public string EngineName => "Unity";
        public string EngineVersion => Application.unityVersion;
        public string ProjectPath => Path.GetDirectoryName(Application.dataPath);
        public string DataPath => Application.dataPath;

        public bool StartServer()
        {
            return AgentBridge.EnsureServerRunning();
        }

        public void StopServer()
        {
            AgentBridge.StopServer();
        }

        public bool IsServerRunning => AgentBridge.IsServerRunning();

        public int ActivePort => AgentBridge.GetActivePort();

        public void EnqueueMainThread(Action action)
        {
            AgentBridge.EnqueueMainThread(action);
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

        public string ExecuteCommand(string className, string methodName, string[] args)
        {
            LitJson.JsonData payload = new LitJson.JsonData();
            payload["ClassName"] = className;
            payload["MethodName"] = methodName;
            payload["Args"] = new LitJson.JsonData();
            if (args != null)
            {
                foreach (var arg in args)
                {
                    payload["Args"].Add(arg);
                }
            }
            return AgentBridge.ExecuteCommandJson(LitJson.JsonMapper.ToJson(payload));
        }

        public bool IsCompiling => EditorApplication.isCompiling;

        public bool LastCompileSucceeded => CompileWatcher.LastCompileSucceeded;

        public string GetCompileErrors()
        {
            return CompileWatcher.GetErrorsJson();
        }

        public void RefreshAssets()
        {
            AssetDatabase.Refresh();
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
