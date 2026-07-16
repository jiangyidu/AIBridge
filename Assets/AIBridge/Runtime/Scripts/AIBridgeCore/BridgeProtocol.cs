namespace AIBridge.Core
{
    /// <summary>
    /// AI Bridge 工具协议常量。
    /// 
    /// 定义进程内工具名称。
    /// </summary>
    public static class BridgeProtocol
    {
        // ─── 内置工具名称 ──────────────────────────────────────────

        /// <summary>执行已注册的 [AgentCommand] 方法。</summary>
        public const string TOOL_EXECUTE_COMMAND = "execute_command";

        /// <summary>查询场景/文档层级结构。</summary>
        public const string TOOL_QUERY_SCENE = "query_scene";

        /// <summary>查询指定对象详情。</summary>
        public const string TOOL_QUERY_OBJECT = "query_object";

        /// <summary>搜索项目资产。</summary>
        public const string TOOL_FIND_ASSETS = "find_assets";

        /// <summary>列出已注册命令。</summary>
        public const string TOOL_LIST_COMMANDS = "list_commands";

        /// <summary>创建基础 GameObject（Cube/Sphere/Cylinder/Plane/Empty）。</summary>
        public const string TOOL_CREATE_GAMEOBJECT = "create_gameobject";

        /// <summary>创建材质资源并保存到 Assets/Materials/。</summary>
        public const string TOOL_CREATE_MATERIAL = "create_material";

        /// <summary>将指定材质设置到场景中的 GameObject 上。</summary>
        public const string TOOL_SET_MATERIAL = "set_material";

        /// <summary>将 AITemp 目录中的脚本挂载到场景物体上。</summary>
        public const string TOOL_ATTACH_SCRIPT = "attach_script";

        /// <summary>获取编译错误。</summary>
        public const string TOOL_GET_COMPILE_ERRORS = "get_compile_errors";

        /// <summary>获取控制台日志。</summary>
        public const string TOOL_GET_CONSOLE_LOGS = "get_console_logs";

        /// <summary>编译临时方法（Unity 特有，其他引擎可选实现）。</summary>
        public const string TOOL_COMPILE_TEMP_METHOD = "compile_temp_method";

        /// <summary>编译脚本文件（Unity 特有，其他引擎可选实现）。</summary>
        public const string TOOL_COMPILE_SCRIPT = "compile_script";

        /// <summary>读取命令源码。</summary>
        public const string TOOL_READ_COMMAND_SOURCE = "read_command_source";

        /// <summary>列出 AI 生成的脚本（Unity 特有，其他引擎可选实现）。</summary>
        public const string TOOL_LIST_AI_SCRIPTS = "list_ai_scripts";

    }
}
