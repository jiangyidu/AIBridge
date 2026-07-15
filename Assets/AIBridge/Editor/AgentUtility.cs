using System;
using System.Linq;
using System.Reflection;

namespace AIBridge.Agent
{
    /// <summary>
    /// 通用工具类，包含诸如 JSON 字符转义、类型解析等重复的逻辑，方便外部调用。
    /// </summary>
    public static class AgentUtility
    {
        /// <summary>
        /// JSON 字符串的特殊字符转义（原 EscJ 方法）
        /// </summary>
        public static string EscJ(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        /// <summary>
        /// 查找类类型，支持全名和短类名。
        /// </summary>
        public static Type ResolveType(string className)
        {
            if (string.IsNullOrEmpty(className)) return null;

            Type t = Type.GetType(className);
            if (t != null) return t;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(className);
                    if (t != null) return t;

                    if (!className.Contains("."))
                    {
                        t = asm.GetTypes().FirstOrDefault(x => x.Name == className);
                        if (t != null) return t;
                    }
                    else
                    {
                        t = asm.GetTypes().FirstOrDefault(x => x.FullName == className);
                        if (t != null) return t;
                    }
                }
                catch { /* 忽略无法加载的程序集 */ }
            }
            return null;
        }

        /// <summary>
        /// 检查指定类型名是否已在当前域的 Assembly 中加载。
        /// </summary>
        public static bool IsTypeLoaded(string typeName)
        {
            return ResolveType(typeName) != null;
        }
    }
}
