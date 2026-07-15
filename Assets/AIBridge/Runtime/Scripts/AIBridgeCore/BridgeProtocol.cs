namespace AIBridge.Core
{
    /// <summary>
    /// AI Bridge HTTP 协议常量。
    /// 
    /// 定义了 Python Agent 与引擎端桥接服务之间的 HTTP 端点路径、
    /// JSON 字段名和工具名称常量，确保 Python 侧和 C# 侧使用一致的协议。
    /// 
    /// 各引擎的 HTTP 桥接服务实现必须遵守这些端点规范。
    /// </summary>
    public static class BridgeProtocol
    {
        // ─── HTTP 端点路径 ──────────────────────────────────────────

        /// <summary>心跳检测端点（GET）。</summary>
        public const string ENDPOINT_PING = "/agent/ping";

        /// <summary>反射执行命令端点（POST）。</summary>
        public const string ENDPOINT_EXECUTE = "/agent/execute";

        /// <summary>工具调用端点（POST）。Body: {"tool":"xxx","args":{}}。</summary>
        public const string ENDPOINT_TOOL = "/agent/tool";

        /// <summary>工具 Schema 端点（GET），返回 AgentTools.json 内容。</summary>
        public const string ENDPOINT_TOOLS_SCHEMA = "/agent/tools_schema";

        /// <summary>已注册命令列表端点（GET）。</summary>
        public const string ENDPOINT_COMMANDS = "/agent/commands";

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

        /// <summary>检查编译状态。</summary>
        public const string TOOL_CHECK_COMPILE_STATUS = "check_compile_status";

        /// <summary>读取命令源码。</summary>
        public const string TOOL_READ_COMMAND_SOURCE = "read_command_source";

        /// <summary>列出 AI 生成的脚本（Unity 特有，其他引擎可选实现）。</summary>
        public const string TOOL_LIST_AI_SCRIPTS = "list_ai_scripts";

        // ─── JSON 响应字段名 ──────────────────────────────────────

        public const string FIELD_SUCCESS = "Success";
        public const string FIELD_MESSAGE = "Message";
        public const string FIELD_ERROR = "Error";
        public const string FIELD_STATUS = "Status";

        public const string STATUS_COMPILING = "compiling";
        public const string STATUS_DONE = "done";
        public const string STATUS_ERROR = "error";
        public const string STATUS_TIMEOUT = "timeout";
        public const string STATUS_IDLE = "idle";

        // ─── 端口范围 ──────────────────────────────────────────────

        /// <summary>引擎桥接 HTTP 端口基址（8000-9999）。</summary>
        public const int BRIDGE_PORT_BASE = 8000;

        /// <summary>引擎桥接 HTTP 端口范围。</summary>
        public const int BRIDGE_PORT_SPAN = 2000;

        /// <summary>Python Agent Service 端口基址（11000-12999）。</summary>
        public const int SERVICE_PORT_BASE = 11000;

        /// <summary>Python Agent Service 端口范围。</summary>
        public const int SERVICE_PORT_SPAN = 2000;
    }
}
