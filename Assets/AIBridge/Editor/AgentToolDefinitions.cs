using System.IO;
using System.Text;
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

        /// <summary>
        /// 初始化工具定义类，缓存主线程的数据路径以支持后台线程访问。
        /// </summary>
        public static void Init(string dataPath)
        {
            _dataPath = dataPath;
        }

        /// <summary>
        /// 返回工具定义 JSON 数组字符串，直接嵌入到 LLM 请求体的 "tools" 字段中。
        /// </summary>
        public static string GetToolsJson()
        {
            string path = "";
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("AgentTools");
            foreach (var guid in guids)
            {
                string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith("AgentTools.json"))
                {
                    path = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
                    break;
                }
            }
#endif

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                string baseDir = _dataPath;
                if (string.IsNullOrEmpty(baseDir))
                {
                    try { baseDir = Application.dataPath; }
                    catch { return "[]"; }
                }

                path = Path.Combine(baseDir, "AIBridge", "Editor", "AgentTools.json");
                if (!File.Exists(path))
                {
                    path = Path.Combine(baseDir, "Editor", "AgentTools.json");
                }
            }

            if (File.Exists(path))
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
            Debug.LogError($"[AgentToolDefinitions] Tools definition file not found at: {path}");
            return "[]";
        }
    }
}
