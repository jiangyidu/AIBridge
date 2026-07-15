using System;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace AIBridge.Agent
{
    /// <summary>
    /// 场景感知工具：提供一组用 [AgentCommand] 标注的查询方法，
    /// 让 Agent 在执行操作前可以"眼见为实"地了解当前 Unity 场景状态。
    ///
    /// 解决原有系统"盲猜物体名称"的问题：
    ///   Agent 先调用 GetSceneHierarchy() 获取场景结构，
    ///   再调用 GetGameObjectInfo(name) 获取精确坐标与组件，
    ///   最后才基于实际数据生成操作代码，成功率大幅提升。
    /// </summary>
    public static class SceneObserver
    {
        // ─── 场景层级查询 ──────────────────────────────────────

        /// <summary>
        /// 获取当前场景中所有顶级 GameObject 的名称树（深度最多 2 层）。
        /// 返回 JSON 数组：[{"name":"...","active":true,"children":[...]},...]
        /// </summary>
        [AgentCommand("获取当前场景层级结构（顶级物体及其子物体列表，深度2层）", category: "Query")]
        public static string GetSceneHierarchy()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var sb = new StringBuilder("[");
            for (int i = 0; i < roots.Length; i++)
            {
                if (i > 0) sb.Append(",");
                AppendGoNode(sb, roots[i], depth: 0, maxDepth: 2);
            }
            sb.Append("]");
            return sb.ToString();
        }

        // ─── 单物体详情查询 ────────────────────────────────────

        /// <summary>
        /// 获取场景中指定名称 GameObject 的详细信息
        /// （世界坐标、旋转、缩放、组件列表、子物体数量）。
        /// 物体不存在时返回含 "Error" 字段的 JSON。
        /// </summary>
        [AgentCommand("获取场景中指定 GameObject 的详细信息（位置/旋转/缩放/组件列表）", category: "Query")]
        public static string GetGameObjectInfo(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            if (go == null)
                return $"{{\"Error\":\"GameObject '{AgentUtility.EscJ(objectName)}' not found in scene\"}}";

            var pos = go.transform.position;
            var rot = go.transform.eulerAngles;
            var scale = go.transform.localScale;

            var sb = new StringBuilder("{");
            sb.Append($"\"Name\":\"{AgentUtility.EscJ(go.name)}\",");
            sb.Append($"\"Active\":{(go.activeSelf ? "true" : "false")},");
            sb.Append($"\"Position\":\"({pos.x:F3},{pos.y:F3},{pos.z:F3})\",");
            sb.Append($"\"Rotation\":\"({rot.x:F1},{rot.y:F1},{rot.z:F1})\",");
            sb.Append($"\"Scale\":\"({scale.x:F3},{scale.y:F3},{scale.z:F3})\",");

            // 组件列表
            sb.Append("\"Components\":[");
            var comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{AgentUtility.EscJ(comps[i].GetType().Name)}\"");
            }
            sb.Append("],");

            sb.Append($"\"ChildCount\":{go.transform.childCount}");
            sb.Append("}");
            return sb.ToString();
        }

        // ─── 资产查询 ──────────────────────────────────────────

        /// <summary>
        /// 在 Assets 目录中搜索资产，支持类型过滤。
        /// 例如 filter = "AI_Mat t:Material" 或 "MyPrefab t:Prefab"。
        /// 最多返回 20 条路径，多余时附加提示。
        /// </summary>
        [AgentCommand("在项目 Assets 目录搜索资产（支持 t:Material / t:Prefab 等类型过滤）", category: "Query")]
        public static string FindAssets(string filter)
        {
            string[] guids = AssetDatabase.FindAssets(filter);
            var sb = new StringBuilder("[");
            int cap = Mathf.Min(guids.Length, 20);
            for (int i = 0; i < cap; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{AgentUtility.EscJ(AssetDatabase.GUIDToAssetPath(guids[i]))}\"");
            }
            if (guids.Length > cap)
                sb.Append($",\"...以及另外 {guids.Length - cap} 项结果\"");
            sb.Append("]");
            return sb.ToString();
        }

        // ─── 已注册命令查询 ────────────────────────────────────

        /// <summary>
        /// 获取当前已注册的所有 [AgentCommand] 命令摘要，
        /// 让 Agent 决策时知道哪些命令可以直接调用（无需编译新代码）。
        /// </summary>
        [AgentCommand("列出当前所有已注册的 AgentCommand 命令（类名/方法名/描述/参数）", category: "Query")]
        public static string ListAvailableCommands()
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var kv in AgentCommandRegistry.Commands)
            {
                if (!first) sb.Append(",");
                first = false;
                var info = kv.Value;
                sb.Append("{");
                sb.Append($"\"class\":\"{AgentUtility.EscJ(info.ClassName)}\",");
                sb.Append($"\"method\":\"{AgentUtility.EscJ(info.MethodName)}\",");
                sb.Append($"\"description\":\"{AgentUtility.EscJ(info.Description)}\",");
                sb.Append($"\"category\":\"{AgentUtility.EscJ(info.Category)}\",");
                sb.Append("\"parameters\":[");
                for (int i = 0; i < info.ParameterNames.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("{");
                    sb.Append($"\"name\":\"{AgentUtility.EscJ(info.ParameterNames[i])}\",");
                    sb.Append($"\"type\":\"{AgentUtility.EscJ(info.ParameterTypes[i])}\"");
                    if (info.ParameterDefaults[i] != null && info.ParameterDefaults[i] != DBNull.Value)
                    {
                        sb.Append($",\"default\":\"{AgentUtility.EscJ(info.ParameterDefaults[i].ToString())}\"");
                    }
                    sb.Append("}");
                }
                sb.Append("]");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        // ─── AITemp 脚本查询 ───────────────────────────────────

        /// <summary>
        /// 列出 Assets/Scripts/AITemp/ 目录下的 MonoBehaviour 脚本信息。
        /// 每条记录包含 name（类型名）和 compiled（类型是否已在当前域加载）两个字段。
        /// Agent 可根据 compiled 字段决定是否直接挂载或需要等待编译完成。
        /// </summary>
        [AgentCommand("列出 AITemp 目录下已有的 MonoBehaviour 脚本列表（避免重复生成）", category: "Query")]
        public static string ListAITempScripts()
        {
            string dir = System.IO.Path.Combine(Application.dataPath, "Scripts", "AITemp");
            if (!System.IO.Directory.Exists(dir))
                return "[]";

            string[] files = System.IO.Directory.GetFiles(dir, "*.cs");
            var sb = new StringBuilder("[");
            for (int i = 0; i < files.Length; i++)
            {
                if (i > 0) sb.Append(",");
                string typeName = System.IO.Path.GetFileNameWithoutExtension(files[i]);
                bool compiled = AgentUtility.IsTypeLoaded(typeName);
                sb.Append($"{{\"name\":\"{AgentUtility.EscJ(typeName)}\",\"compiled\":{(compiled ? "true" : "false")}}}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        // ─── 内部辅助 ──────────────────────────────────────────

        private static void AppendGoNode(StringBuilder sb, GameObject go, int depth, int maxDepth)
        {
            sb.Append("{");
            sb.Append($"\"name\":\"{AgentUtility.EscJ(go.name)}\",");
            sb.Append($"\"active\":{(go.activeSelf ? "true" : "false")}");

            int childCount = go.transform.childCount;
            if (depth < maxDepth && childCount > 0)
            {
                sb.Append(",\"children\":[");
                for (int i = 0; i < childCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    AppendGoNode(sb, go.transform.GetChild(i).gameObject, depth + 1, maxDepth);
                }
                sb.Append("]");
            }
            else if (childCount > 0)
            {
                sb.Append($",\"childCount\":{childCount}");
            }
            sb.Append("}");
        }
    }
}
