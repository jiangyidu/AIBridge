using System;
using System.IO;
using System.Text;
using LitJson;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>
    /// Agent 工具定义：从 AgentTools.json 加载工具定义。
    /// 每个工具对应 Unity 端的一个可执行能力，由 AgentToolRouter 负责分派执行。
    ///
    /// 扩展方法：在 Assets/Editor/AgentTools.json 中新增一项即可，
    ///           同时在 AgentToolRouter.ExecuteTool() 中添加对应的 case。
    /// </summary>
    public static class AgentToolDefinitions
    {
        private static string _dataPath;
        private static string _toolsPath;
        private static string _cachedJson;
        private static long _cachedWriteTicks;
        private static long _cachedLength;

        /// <summary>
        /// 初始化工具定义类，缓存主线程的数据路径以支持后台线程访问。
        /// </summary>
        public static void Init(string dataPath)
        {
            if (string.Equals(_dataPath, dataPath, StringComparison.OrdinalIgnoreCase)) return;
            _dataPath = dataPath;
            _toolsPath = "";
            _cachedJson = null;
            _cachedWriteTicks = 0;
            _cachedLength = 0;
        }

        /// <summary>
        /// 返回工具定义 JSON 数组字符串，直接嵌入到 LLM 请求体的 "tools" 字段中。
        /// </summary>
        public static string GetToolsJson()
        {
            string path = ResolveToolsPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError("[AgentToolDefinitions] Tools definition file not found: " + path);
                return "";
            }

            FileInfo info = new FileInfo(path);
            if (_cachedJson != null && _cachedWriteTicks == info.LastWriteTimeUtc.Ticks && _cachedLength == info.Length)
                return _cachedJson;

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                JsonData tools = JsonMapper.ToObject(json);
                if (tools == null || !tools.IsArray || tools.Count == 0)
                    throw new JsonException("根节点必须是非空工具数组");

                _cachedJson = json;
                _cachedWriteTicks = info.LastWriteTimeUtc.Ticks;
                _cachedLength = info.Length;
                return _cachedJson;
            }
            catch (Exception ex)
            {
                _cachedJson = null;
                Debug.LogError("[AgentToolDefinitions] Invalid tools definition: " + ex.Message);
                return "";
            }
        }

        private static string ResolveToolsPath()
        {
            if (!string.IsNullOrEmpty(_toolsPath) && File.Exists(_toolsPath)) return _toolsPath;

            string baseDir = _dataPath;
            if (string.IsNullOrEmpty(baseDir))
            {
                try { baseDir = Application.dataPath; }
                catch { return ""; }
            }

            string path = Path.Combine(baseDir, "AIBridge", "Editor", "AgentTools.json");
            if (File.Exists(path))
            {
                _toolsPath = path;
                return _toolsPath;
            }

            path = Path.Combine(baseDir, "Editor", "AgentTools.json");
            if (File.Exists(path))
            {
                _toolsPath = path;
                return _toolsPath;
            }

#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("AgentTools");
            foreach (string guid in guids)
            {
                string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith("AgentTools.json", StringComparison.OrdinalIgnoreCase))
                {
                    path = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
                    if (File.Exists(path))
                    {
                        _toolsPath = path;
                        return _toolsPath;
                    }
                }
            }
#endif
            return "";
        }
    }
}
