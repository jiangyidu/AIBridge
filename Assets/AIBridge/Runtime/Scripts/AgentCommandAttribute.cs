using System;
using System.Collections.Generic;
using System.Reflection;
using AIBridge.Core;

namespace AIBridge.Agent
{
    /// <summary>
    /// 标记一个静态方法为可供 AI 代理直接调用的命令。
    /// 带有此特性的方法会在 AgentBridge 启动时自动注册到命令注册表中，
    /// 无需手动修改 AgentBridge.cs 或工具路由器。
    /// 用法示例：
    ///   [AgentCommand("在指定位置创建一个 Cube", category: "Scene")]
    ///   public static string CreateCube(float x, float y, float z) { ... }
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class AgentCommandAttribute : Attribute
    {
        /// <summary>命令功能描述，会显示在 AI 弹窗的命令清单中。</summary>
        public string Description { get; private set; }

        /// <summary>命令分类（如 "Scene"、"Asset"、"General"），用于在弹窗中分组显示。</summary>
        public string Category { get; private set; }

        public AgentCommandAttribute(string description, string category = "General")
        {
            Description = description;
            Category = category;
        }
    }

    /// <summary>
    /// 命令信息：描述一个已注册的 AgentCommand 方法的元数据。
    /// </summary>
    public class AgentCommandInfo
    {
        /// <summary>类的完整名称（含命名空间），用于 AgentBridge 反射调用。</summary>
        public string ClassName;

        /// <summary>方法名称。</summary>
        public string MethodName;

        /// <summary>命令描述（来自 AgentCommandAttribute）。</summary>
        public string Description;

        /// <summary>命令分类（来自 AgentCommandAttribute）。</summary>
        public string Category;

        /// <summary>参数名称列表。</summary>
        public string[] ParameterNames;

        /// <summary>参数类型名称列表（与 ParameterNames 一一对应）。</summary>
        public string[] ParameterTypes;

        /// <summary>实际参数类型列表。</summary>
        public Type[] ParameterTypesReal;

        /// <summary>参数默认值列表（无默认值则为 null）。</summary>
        public object[] ParameterDefaults;

        /// <summary>对方法信息的直接引用，用于本地直接调用。</summary>
        public MethodInfo Method;

        /// <summary>格式化后的调用签名，例如 "CreateCube(x: float, y: float, z: float)"。</summary>
        public string Signature
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(MethodName).Append("(");
                for (int i = 0; i < ParameterNames.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(ParameterTypes[i]).Append(" ").Append(ParameterNames[i]);
                }
                sb.Append(")");
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// 命令注册表：在 AgentBridge 启动时自动扫描所有程序集，
    /// 收集所有带有 [AgentCommand] 特性的静态公开方法，构建命令字典。
    /// 扩展方式：只需在任意静态方法上添加 [AgentCommand] 特性，无需修改此文件或 AgentBridge.cs。
    /// </summary>
    public static class AgentCommandRegistry
    {
        private static Dictionary<string, AgentCommandInfo> _commands;
        private static bool _initialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// 获取所有已注册命令的字典。Key 格式为 "ClassName.MethodName"。
        /// 首次访问时会自动触发扫描。
        /// </summary>
        public static Dictionary<string, AgentCommandInfo> Commands
        {
            get
            {
                lock (_lock)
                {
                    if (!_initialized) Scan();
                    return _commands;
                }
            }
        }

        /// <summary>
        /// 扫描当前 AppDomain 中所有程序集，收集带 [AgentCommand] 特性的静态方法。
        /// 通常在 AgentBridge 静态构造函数中调用，也可以手动调用刷新。
        /// </summary>
        public static void Scan()
        {
            var result = new Dictionary<string, AgentCommandInfo>();
            Assembly attributeAssembly = typeof(AgentCommandAttribute).Assembly;
            string attributeAssemblyName = attributeAssembly.GetName().Name;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!CanContainAgentCommands(asm, attributeAssembly, attributeAssemblyName)) continue;
                string asmName = asm.FullName ?? asm.GetName().Name;

                try
                {
                    foreach (Type type in asm.GetTypes())
                    {
                        foreach (MethodInfo method in type.GetMethods(
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        {
                            var attr = method.GetCustomAttribute<AgentCommandAttribute>();
                            if (attr == null) continue;

                            ParameterInfo[] parameters = method.GetParameters();
                            int paramCount = parameters.Length;

                            var info = new AgentCommandInfo
                            {
                                ClassName = type.FullName ?? type.Name,
                                MethodName = method.Name,
                                Description = attr.Description,
                                Category = attr.Category,
                                ParameterNames = new string[paramCount],
                                ParameterTypes = new string[paramCount],
                                ParameterTypesReal = new Type[paramCount],
                                ParameterDefaults = new object[paramCount],
                                Method = method
                            };

                            for (int i = 0; i < paramCount; i++)
                            {
                                info.ParameterNames[i] = parameters[i].Name;
                                info.ParameterTypes[i] = parameters[i].ParameterType.Name;
                                info.ParameterTypesReal[i] = parameters[i].ParameterType;
                                info.ParameterDefaults[i] = parameters[i].HasDefaultValue
                                    ? parameters[i].DefaultValue : null;
                            }

                            string key = $"{type.Name}.{method.Name}";
                            int suffix = 1;
                            while (result.ContainsKey(key))
                                key = $"{type.Name}.{method.Name}_{suffix++}";

                            result[key] = info;
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // 部分程序集加载类型失败（如缺少引用）是正常的，通常不是用户脚本
                    // 但如果是 Assembly-CSharp-Editor 加载失败，就需要注意
                    if (asmName.Contains("Assembly-CSharp"))
                        BridgeLog.LogWarning($"[AgentCommandRegistry] 无法加载程序集 {asmName} 的部分类型: {ex.Message}");
                }
                catch (Exception) { /* 忽略其他程序集异常 */ }
            }

            // 按 Category 和 MethodName 排序，确保 UI 分组显示正确
            var sortedResult = new Dictionary<string, AgentCommandInfo>();
            var sortedKeys = new List<string>(result.Keys);
            sortedKeys.Sort((a, b) =>
            {
                var cmdA = result[a];
                var cmdB = result[b];
                int catCompare = string.Compare(cmdA.Category, cmdB.Category, StringComparison.OrdinalIgnoreCase);
                if (catCompare != 0) return catCompare;
                return string.Compare(cmdA.MethodName, cmdB.MethodName, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var key in sortedKeys)
            {
                sortedResult[key] = result[key];
            }

            lock (_lock)
            {
                _commands = sortedResult;
                _initialized = true;
            }

#if AIBRIDGE_VERBOSE_LOGS
            BridgeLog.Log($"[AgentCommandRegistry] 扫描完成，共找到 {sortedResult.Count} 个 AgentCommand 方法。");
#endif
        }

        private static bool CanContainAgentCommands(
            Assembly assembly,
            Assembly attributeAssembly,
            string attributeAssemblyName)
        {
            if (assembly == attributeAssembly) return true;
            try
            {
                AssemblyName[] references = assembly.GetReferencedAssemblies();
                for (int i = 0; i < references.Length; i++)
                {
                    if (string.Equals(references[i].Name, attributeAssemblyName, StringComparison.Ordinal))
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 将整个命令注册表序列化为 JSON 字符串，供 list_commands 工具响应。
        /// </summary>
        public static string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"Success\":true,\"Commands\":[");
            bool first = true;
            foreach (var kv in Commands)
            {
                if (!first) sb.Append(",");
                first = false;
                AgentCommandInfo info = kv.Value;
                sb.Append("{");
                sb.Append($"\"Key\":\"{EscapeJson(kv.Key)}\",");
                sb.Append($"\"ClassName\":\"{EscapeJson(info.ClassName)}\",");
                sb.Append($"\"MethodName\":\"{EscapeJson(info.MethodName)}\",");
                sb.Append($"\"Description\":\"{EscapeJson(info.Description)}\",");
                sb.Append($"\"Category\":\"{EscapeJson(info.Category)}\",");
                sb.Append("\"Parameters\":[");
                for (int i = 0; i < info.ParameterNames.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"{{\"Name\":\"{EscapeJson(info.ParameterNames[i])}\",");
                    sb.Append($"\"Type\":\"{EscapeJson(info.ParameterTypes[i])}\"}}");
                }
                sb.Append("]}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
