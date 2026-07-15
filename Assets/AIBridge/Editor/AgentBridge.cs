//  * =============================================================================
//  * 项目名称：Unity AI Agent Bridge Framework
//  * 版权所有：Copyright © 2026 LegendMars 保留所有权利
//  * 作者：LegendMars
//  * 创建时间：2026-06-23
//  * 联系邮箱：legendmars201512@gmail.com
//  * 代码作用：Unity 编辑器端的核心本地 HTTP 通信桥梁，负责连接外部 Python Agent 服务与 Unity 内部 API。
//  *           1. 多线程/主线程协同机制：使用后台线程异步轮询（BeginGetContext）接收网络请求以避免 UI 阻塞，
//  *              非线程安全的 Unity API 调用则通过 ConcurrentQueue 分发回 Unity 主线程（EditorApplication.update）安全执行。
//  *              对于 Ping 请求则在后台线程直接快速返回，防止因主线程繁忙导致 Agent 离线误判。
//  *           2. 端口动态探测与防冲突：根据当前项目路径的 MD5 哈希计算出一个相对固定的端口（8000-9999），
//  *              支持端口冲突时自动递增探测，并缓存活动端口至 agent_port.txt，支持多开 Unity 实例而不冲突。
//  *           3. 自生命周期管理：通过 [InitializeOnLoad] 随编辑器自动启动，监听程序集重载及编辑器退出事件，
//  *              在重载前后自动重新扫描注册表和释放/重建监听器，避免端口泄漏或句柄残留。
//  *           4. 强类型反射匹配与参数解析引擎：解析 JSON Payload 并通过反射安全调用注册的静态方法（[AgentCommand]）。
//  *              支持参数默认值与重载匹配；内置对基础类型、Enum、Unity 结构体（Vector、Color、Bounds 等）、
//  *              场景中 GameObject/Component 检索、AssetDatabase 资源加载、复杂 JSON 反序列化及静态委托的转换解析。
//  *           5. 自动化构建与扩展性支持：自动初始化生成 AITempCommands.cs（AI 动态编译临时指令区）与
//  *              AntigravityTasks.cs（用户自定义命令引导区），为 AI 及开发者编写自定义命令提供标准工作流。
//  *           6. 无头模式 (Batchmode) 支持：提供命令行执行入口（ExecuteBatchTask），允许通过文件传递并执行任务，
//  *              方便在 CI/CD 或批处理任务中自动化操控 Unity 编辑器。
//  * =============================================================================

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using LitJson;
using System.Linq;
using UnityEditor.Compilation;
using System.Security.Cryptography;
using AIBridge.Core;

namespace AIBridge.Agent
{
    [InitializeOnLoad]
    public static class AgentBridge
    {
        private static HttpListener listener;
        private static Thread listenerThread;
        private static ConcurrentQueue<Action> dispatchQueue = new ConcurrentQueue<Action>();
        private static volatile bool isRunning = false;
        private static bool updateHooked = false;
        private static int activePort = -1;

        public static int GetActivePort()
        {
            return activePort > 0 ? activePort : GetProjectPort();
        }

        /// <summary>
        /// 静态构造函数，在Unity编辑器加载或脚本重新编译时自动执行。
        /// 负责启动HTTP服务并注册生命周期事件，确保资源正确分配与释放。
        /// </summary>
        static AgentBridge()
        {
            // 注册 Unity 平台的日志记录器
            BridgeLog.SetLogger(new UnityBridgeLogger());

            // 确保用户模板文件和文件夹存在
            EnsureUserTemplateFilesExist();

            // 扫描所有带 [AgentCommand] 特性的方法，构建命令注册表
            AgentCommandRegistry.Scan();

            EnsureUpdateHooked();
            EnsureServerRunning();
            // 确保在编辑器关闭或脚本重新编译前释放端口，避免端口占用
            EditorApplication.quitting -= StopServer;
            EditorApplication.quitting += StopServer;
            AssemblyReloadEvents.beforeAssemblyReload -= StopServer;
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
            // 程序集重载后，注册表需要重新扫描并确保桥接服务恢复
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnAfterAssemblyReload()
        {
            EnsureUserTemplateFilesExist();
            AgentCommandRegistry.Invalidate();
            AgentCommandRegistry.Scan();
            EnsureUpdateHooked();
            EnsureServerRunning();
        }

        public static string GetAgentUrl()
        {
            return "http://127.0.0.1:" + GetActivePort() + "/agent";
        }

        public static bool IsServerRunning()
        {
            return listener != null && listener.IsListening && isRunning;
        }

        public static bool EnsureServerRunning()
        {
            EnsureUpdateHooked();
            if (IsServerRunning()) return true;
            StartServer();
            return IsServerRunning();
        }

        private static void EnsureUpdateHooked()
        {
            // Unity 编译/Domain Reload 后，EditorApplication.update 订阅可能丢失；
            // 先移除再添加可避免重复订阅，同时确保主线程 dispatchQueue 能继续执行。
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            updateHooked = true;
        }

        [MenuItem("AIBridge/Start Agent Bridge")]
        public static void ManualStart()
        {
            if (IsServerRunning())
            {
                Debug.Log("[AgentBridge] Server is already running.");
                return;
            }
            EnsureServerRunning();
            Debug.Log("[AgentBridge] Manual start triggered.");
        }

        [MenuItem("AIBridge/Stop Agent Bridge")]
        public static void ManualStop()
        {
            if (!isRunning)
            {
                Debug.Log("[AgentBridge] Server is not running.");
                return;
            }
            StopServer();
            Debug.Log("[AgentBridge] Manual stop triggered.");
        }

        /// <summary>
        /// 启动本地 HTTP 监听服务。
        /// 在后台线程中监听来自 localhost 的请求，以实现外部代理与 Unity 编辑器的通信。
        /// </summary>
        private static void StartServer()
        {
            EnsureUpdateHooked();
            if (listener != null && listener.IsListening)
            {
                isRunning = true;
                return;
            }

            int basePort = GetProjectPort();
            System.Collections.Generic.List<int> portsToTry = new System.Collections.Generic.List<int>();

            // 优先尝试上次成功的端口，减少重载后端口频繁偏移
            try
            {
                string projDir = Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrEmpty(projDir))
                {
                    string portFilePath = Path.Combine(projDir, "AgentController/agent_port.txt");
                    if (File.Exists(portFilePath))
                    {
                        string content = File.ReadAllText(portFilePath).Trim();
                        int parsedPort;
                        if (int.TryParse(content, out parsedPort) && parsedPort >= 8000 && parsedPort <= 9999)
                        {
                            portsToTry.Add(parsedPort);
                        }
                    }
                }
            }
            catch { }

            for (int offset = 0; offset < 10; offset++)
            {
                int port = basePort + offset;
                if (!portsToTry.Contains(port))
                {
                    portsToTry.Add(port);
                }
            }

            foreach (int port in portsToTry)
            {
                try
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add($"http://127.0.0.1:{port}/agent/");
                    listener.Start();
                    isRunning = true;
                    activePort = port;

                    listenerThread = new Thread(Listen);
                    listenerThread.IsBackground = true;
                    listenerThread.Start();

                    try
                    {
                        string projDir = Path.GetDirectoryName(Application.dataPath);
                        if (!string.IsNullOrEmpty(projDir))
                        {
                            string portFilePath = Path.Combine(projDir, "AgentController/agent_port.txt");
                            string dir = Path.GetDirectoryName(portFilePath);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            File.WriteAllText(portFilePath, port.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[AgentBridge] Failed to write agent_port.txt: " + ex.Message);
                    }

                    Debug.Log($"[AgentBridge] HTTP Server started on http://127.0.0.1:{port}/agent/ for AI Control.");
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AgentBridge] Failed to start HTTP server on port {port}: {e.Message}");
                    try
                    {
                        if (listener != null) listener.Close();
                    }
                    catch { }
                    listener = null;
                }
            }

            isRunning = false;
            Debug.LogError("[AgentBridge] Failed to start HTTP server after trying multiple ports.");
        }

        /// <summary>
        /// 基于当前项目路径计算一个固定的哈希端口号（8000-9999）
        /// </summary>
        private static int GetProjectPort()
        {
            string dataPath = Application.dataPath;
            string projDir = Path.GetDirectoryName(dataPath);
            if (string.IsNullOrEmpty(projDir)) projDir = dataPath;

            string normalizedPath = projDir.Replace('\\', '/').ToLower();

            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(normalizedPath);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                // 取前4个字节组成无符号整数，避免跨平台端序和符号位溢出问题
                uint hash = (uint)hashBytes[0] | ((uint)hashBytes[1] << 8) | ((uint)hashBytes[2] << 16) | ((uint)hashBytes[3] << 24);
                return 8000 + (int)(hash % 2000);
            }
        }

        /// <summary>
        /// 停止 HTTP 服务，关闭监听器并释放后台线程。
        /// </summary>
        public static void StopServer()
        {
            isRunning = false;
            EditorApplication.update -= Update;

            try
            {
                string projDir = Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrEmpty(projDir))
                {
                    string portFilePath = Path.Combine(projDir, "AgentController/agent_port.txt");
                    if (File.Exists(portFilePath))
                    {
                        File.Delete(portFilePath);
                    }
                }
            }
            catch { }
            activePort = -1;

            if (listener != null)
            {
                try
                {
                    // 优化：采用异步轮询后，后台线程能优雅退出。
                    // 此时可以使用更安全的 Stop() 和 Close()，避免 Abort() 导致的底层套接字句柄泄漏
                    listener.Stop();
                    listener.Close();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[AgentBridge] Exception during listener shutdown: " + e.Message);
                }
                finally
                {
                    listener = null;
                }
            }

            if (listenerThread != null)
            {
                try
                {
                    if (listenerThread.IsAlive)
                    {
                        listenerThread.Join(500);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[AgentBridge] Exception during thread join: " + e.Message);
                }
                finally
                {
                    listenerThread = null;
                }
            }
        }


        /// <summary>
        /// 后台线程循环，负责接收 HTTP 请求。
        /// 使用 BeginGetContext 配合异步等待句柄，避免线程在底层原生代码中被无限阻塞。
        /// </summary>
        private static void Listen()
        {
            while (isRunning && listener != null && listener.IsListening)
            {
                try
                {
                    // 使用非阻塞的方式请求上下文
                    IAsyncResult result = listener.BeginGetContext(null, null);

                    // 只要还在运行，且请求没有到来，就每 200 毫秒醒来检查一次状态
                    while (isRunning && listener.IsListening && !result.AsyncWaitHandle.WaitOne(200))
                    {
                        // 循环等待，这样主线程随时可以将 isRunning 设为 false 来安全中断我们
                    }

                    // 如果是因为 isRunning 变为 false 或监听器关闭导致的退出，则直接跳出循环
                    if (!isRunning || !listener.IsListening)
                        break;

                    // 此时请求已经到来，获取上下文
                    HttpListenerContext context = listener.EndGetContext(result);

                    // ping 不触碰 Unity API，直接在监听线程快速返回，避免主线程忙碌时
                    // Python 误判 AgentBridge 离线。其他工具请求仍必须回到 Unity 主线程执行。
                    if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == BridgeProtocol.ENDPOINT_PING)
                    {
                        SendJsonResponse(context.Response, 200, "{\"Success\":true,\"Message\":\"pong\"}");
                    }
                    else
                    {
                        dispatchQueue.Enqueue(() => ProcessHttpRequest(context));
                    }
                }
                catch (Exception e)
                {
                    if (isRunning) Debug.LogError("[AgentBridge] Listen error: " + e.Message);
                }
            }
        }

        /// <summary>
        /// 绑定到 EditorApplication.update 的主线程更新函数。
        /// 负责从队列中取出并执行需要在 Unity 主线程上运行的任务。
        /// </summary>
        private static void Update()
        {
            while (dispatchQueue.TryDequeue(out Action action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError("[AgentBridge] Action execution error: " + e.Message);
                }
            }
        }

        /// <summary>
        /// 将一个操作排入主线程队列中执行。
        /// </summary>
        public static void EnqueueMainThread(Action action)
        {
            dispatchQueue.Enqueue(action);
        }

        /// <summary>
        /// 在主线程上处理 HTTP 请求。
        /// </summary>
        private static void ProcessHttpRequest(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            response.ContentType = "application/json";
            response.ContentEncoding = Encoding.UTF8;

            if (request.HttpMethod == "GET" && request.Url.AbsolutePath == BridgeProtocol.ENDPOINT_PING)
            {
                SendJsonResponse(response, 200, "{\"Success\":true,\"Message\":\"pong\"}");
                return;
            }

            // 返回所有已注册的 [AgentCommand] 命令清单
            if (request.HttpMethod == "GET" && request.Url.AbsolutePath == BridgeProtocol.ENDPOINT_COMMANDS)
            {
                string commandsJson = AgentCommandRegistry.ToJson();
                SendJsonResponse(response, 200, commandsJson);
                return;
            }

            if (request.HttpMethod == "POST" && request.Url.AbsolutePath == BridgeProtocol.ENDPOINT_EXECUTE)
            {
                string requestBody;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    requestBody = reader.ReadToEnd();
                }

                string resultJson = ExecuteCommandJson(requestBody);
                SendJsonResponse(response, 200, resultJson);
                return;
            }

            // ── 新增：外部 Agent 工具调用端点 ──────────────────
            // POST /agent/tool  Body: {"tool":"query_scene","args":{}}
            if (request.HttpMethod == "POST" && request.Url.AbsolutePath == BridgeProtocol.ENDPOINT_TOOL)
            {
                string requestBody;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    requestBody = reader.ReadToEnd();
                }

                JsonData toolReq = AgentJson.ParseObject(requestBody);
                string toolName = AgentJson.GetString(toolReq, "tool");
                string argsJson = AgentJson.GetObjectJson(toolReq, "args");

                if (string.IsNullOrEmpty(toolName))
                {
                    SendJsonResponse(response, 400, AgentJson.Error("Missing 'tool' field"));
                    return;
                }

                string result = AgentToolRouter.ExecuteTool(toolName, string.IsNullOrEmpty(argsJson) ? "{}" : argsJson);
                SendJsonResponse(response, 200, result);
                return;
            }

            // GET /agent/tools_schema → 返回 AgentTools.json 内容
            if (request.HttpMethod == "GET" && request.Url.AbsolutePath == BridgeProtocol.ENDPOINT_TOOLS_SCHEMA)
            {
                string toolsJson = AgentToolDefinitions.GetToolsJson();
                SendJsonResponse(response, 200, toolsJson);
                return;
            }

            SendJsonResponse(response, 404, "{\"Success\":false,\"Message\":\"Not Found\"}");
        }

        /// <summary>
        /// 向客户端发送 JSON 格式的 HTTP 响应。
        /// </summary>
        private static void SendJsonResponse(HttpListenerResponse response, int statusCode, string json)
        {
            response.StatusCode = statusCode;
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            try
            {
                using (Stream output = response.OutputStream)
                {
                    output.Write(buffer, 0, buffer.Length);
                }
            }
            catch (Exception) { /* Connection closed */ }
        }

        // ---------- Core Execution Logic ----------

        /// <summary>
        /// 核心执行逻辑：解析 JSON 载荷，利用反射调用指定的静态方法，并返回执行结果的 JSON 字符串。
        /// </summary>
        public static string ExecuteCommandJson(string jsonPayload)
        {
            JsonData responseData = new JsonData();
            try
            {
                JsonData req = JsonMapper.ToObject(jsonPayload);
                string className = req.Keys.Contains("ClassName") ? req["ClassName"].ToString() : "";
                string methodName = req.Keys.Contains("MethodName") ? req["MethodName"].ToString() : "";

                if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(methodName))
                    throw new Exception("ClassName or MethodName is missing.");

                // 1. 获取原始参数字符串数组
                string[] rawArgs = ParseRawArguments(req);

                // 2. 解析目标类
                Type targetType = AgentUtility.ResolveType(className);
                if (targetType == null)
                    throw new Exception("Class not found: " + className);

                // 3. 查找匹配方法
                MethodInfo method = ResolveMethod(targetType, methodName, rawArgs?.Length ?? 0);
                if (method == null)
                    throw new Exception($"Static method '{methodName}' not found or parameter count mismatch in class '{className}'");

                // 4. 转换并准备调用参数
                object[] invokeArgs = PrepareInvokeArguments(method.GetParameters(), rawArgs);

                // 5. 执行
                object result = method.Invoke(null, invokeArgs);

                responseData["Success"] = true;
                responseData["Message"] = "Executed successfully.";
                responseData["Result"] = result != null ? result.ToString() : "null";
            }
            catch (Exception e)
            {
                // 优先使用 InnerException（反射调用时真正的业务异常在 InnerException 里）
                Exception actual = e.InnerException ?? e;
                responseData["Success"] = false;
                responseData["Message"] = actual.Message;
                responseData["ExceptionType"] = actual.GetType().Name;
                responseData["StackTrace"] = actual.StackTrace ?? "";
                // 若还有更深层异常，一并记录
                if (actual.InnerException != null)
                {
                    responseData["InnerException"] = actual.InnerException.Message;
                    responseData["InnerStackTrace"] = actual.InnerException.StackTrace ?? "";
                }
            }

            return JsonMapper.ToJson(responseData);
        }

        /// <summary>
        /// 从 JsonData 中解析 Args 数组为字符串数组。
        /// </summary>
        private static string[] ParseRawArguments(JsonData req)
        {
            if (!req.Keys.Contains("Args") || !req["Args"].IsArray) return null;
            JsonData argsJson = req["Args"];
            string[] args = new string[argsJson.Count];
            for (int i = 0; i < argsJson.Count; i++)
                args[i] = argsJson[i]?.ToString();
            return args;
        }



        /// <summary>
        /// 查找静态方法，支持基本重载（参数数量匹配）。
        /// </summary>
        private static MethodInfo ResolveMethod(Type type, string methodName, int argCount)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                              .Where(m => m.Name == methodName).ToList();

            var exactMatch = methods.FirstOrDefault(m => m.GetParameters().Length == argCount);
            if (exactMatch != null) return exactMatch;

            return methods.FirstOrDefault(m =>
                m.GetParameters().Length > argCount &&
                m.GetParameters().Skip(argCount).All(p => p.HasDefaultValue)
            );
        }

        /// <summary>
        /// 根据方法参数定义，将原始字符串参数转换为目标类型。
        /// </summary>
        private static object[] PrepareInvokeArguments(ParameterInfo[] parameters, string[] rawArgs)
        {
            if (parameters.Length == 0) return null;

            object[] invokeArgs = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (rawArgs != null && i < rawArgs.Length && rawArgs[i] != null)
                {
                    invokeArgs[i] = ParseArgumentValue(rawArgs[i], parameters[i].ParameterType);
                }
                else
                {
                    if (parameters[i].HasDefaultValue) invokeArgs[i] = parameters[i].DefaultValue;
                    else if (parameters[i].ParameterType.IsValueType) invokeArgs[i] = Activator.CreateInstance(parameters[i].ParameterType);
                    else invokeArgs[i] = null;
                }
            }
            return invokeArgs;
        }

        /// <summary>
        /// 核心转换引擎：处理基础类型、Unity对象、数值类型、JSON 及委托。
        /// </summary>
        private static object ParseArgumentValue(string value, Type pType)
        {
            if (pType == typeof(string)) return value;
            if (string.IsNullOrEmpty(value)) return pType.IsValueType ? Activator.CreateInstance(pType) : null;

            try
            {
                if (pType.IsEnum) return Enum.Parse(pType, value, true);

                // 1. 基础类型解析 (增加 TryParse 容错)
                if (pType == typeof(int))
                {
                    if (int.TryParse(value, out int i)) return i;
                    if (float.TryParse(value, out float f)) return (int)f; // 容错：将 "1.0" 转为 1
                    throw new FormatException($"Cannot parse '{value}' as int");
                }
                if (pType == typeof(float))
                {
                    if (float.TryParse(value, out float f)) return f;
                    throw new FormatException($"Cannot parse '{value}' as float");
                }
                if (pType == typeof(bool))
                {
                    if (bool.TryParse(value, out bool b)) return b;
                    if (value == "1") return true;
                    if (value == "0") return false;
                    throw new FormatException($"Cannot parse '{value}' as bool");
                }

                // 2. Unity Object 解析 (场景查找 + 资源加载)
                if (typeof(UnityEngine.Object).IsAssignableFrom(pType))
                {
                    GameObject go = GameObject.Find(value);
                    if (go != null)
                    {
                        if (pType == typeof(GameObject)) return go;
                        var comp = go.GetComponent(pType);
                        if (comp != null) return comp;
                    }
#if UNITY_EDITOR
                    string[] guids = UnityEditor.AssetDatabase.FindAssets(value + " t:" + pType.Name);
                    if (guids.Length > 0)
                    {
                        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                        return UnityEditor.AssetDatabase.LoadAssetAtPath(path, pType);
                    }
#endif
                    return null;
                }

                // 3. Unity 数值类型解析 (Vector2/3/4, Color, Bounds, Quaternion)
                if (pType.Namespace == "UnityEngine" && pType.IsValueType)
                {
                    string[] parts = value.Trim('(', ')', ' ').Split(',');
                    float[] v = new float[parts.Length];
                    for (int j = 0; j < parts.Length; j++) float.TryParse(parts[j].Trim(), out v[j]);

                    if (pType == typeof(Vector2) && v.Length >= 2) return new Vector2(v[0], v[1]);
                    if (pType == typeof(Vector3) && v.Length >= 3) return new Vector3(v[0], v[1], v[2]);
                    if (pType == typeof(Vector4) && v.Length >= 4) return new Vector4(v[0], v[1], v[2], v[3]);
                    if (pType == typeof(Color) && v.Length >= 3) return new Color(v[0], v[1], v[2], v.Length > 3 ? v[3] : 1f);
                    if (pType == typeof(Quaternion) && v.Length >= 4) return new Quaternion(v[0], v[1], v[2], v[3]);
                    if (pType == typeof(Bounds) && v.Length >= 6) return new Bounds(new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5]));
                }

                // 4. JSON / Delegate / Fallback
                string trimmed = value.Trim();
                if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                {
                    var method = typeof(LitJson.JsonMapper).GetMethods()
                        .FirstOrDefault(m => m.Name == "ToObject" && m.IsGenericMethod && m.GetParameters().Length == 1);
                    return method?.MakeGenericMethod(pType).Invoke(null, new object[] { trimmed });
                }

                if (typeof(Delegate).IsAssignableFrom(pType))
                {
                    string[] parts = value.Split('.');
                    string cName = parts.Length > 1 ? parts[0] : "";
                    string mName = parts.Length > 1 ? parts[1] : parts[0];
                    Type t = AgentUtility.ResolveType(cName);
                    MethodInfo mi = t?.GetMethod(mName, BindingFlags.Public | BindingFlags.Static);
                    return mi != null ? Delegate.CreateDelegate(pType, mi) : null;
                }

                return Convert.ChangeType(value, pType);
            }
            catch { return null; }
        }

        /// <summary>
        /// 确保用于放置 AI 动态代码的文件夹以及相关的用户自定义命令引导文件存在。
        /// </summary>
        private static void EnsureUserTemplateFilesExist()
        {
            try
            {
                string editorDir = Path.Combine(Application.dataPath, "Editor");
                if (!Directory.Exists(editorDir))
                {
                    Directory.CreateDirectory(editorDir);
                }

                string aiTempCommandsPath = Path.Combine(editorDir, "AITempCommands.cs");
                if (!File.Exists(aiTempCommandsPath))
                {
                    string content =
@"using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

namespace AIBridge.Agent
{
    /// <summary>
    /// AI 临时命令类
    /// 这是一个专供 AI 在对话时动态编写和执行临时任务的类。
    /// 
    /// 💡 工作流程与规范：
    /// 1. 当 AI 发现当前已有命令库无法满足复杂任务时，会自动通过 compile_temp_method 动态生成静态方法写入该类。
    /// 2. 生成的方法会带有 [AgentCommand] 特性，保存在 AITempCommandsGenerated 文件夹下的独立 partial 文件中。
    /// 3. 项目编译完成后，AI 即可调用该方法执行任务。
    /// 4. 这里的类是 partial 的，请勿在此处手动编写容易被覆盖的临时方法。
    /// </summary>
    public static partial class AITempCommands
    {

    }
}";
                    File.WriteAllText(aiTempCommandsPath, content, Encoding.UTF8);
                    AssetDatabase.ImportAsset("Assets/Editor/AITempCommands.cs");
                }

                string antigravityTasksPath = Path.Combine(editorDir, "AntigravityTasks.cs");
                if (!File.Exists(antigravityTasksPath))
                {
                    string content =
@"using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using AIBridge.Agent;

namespace AIBridge.Agent
{
    /// <summary>
    /// 用户自定义 AI 任务命令类。
    /// 在这里编写的方法只要标记了 [AgentCommand] 特性，就会自动被 AI 识别并供其调用。
    /// 
    /// 💡 使用规范与指南：
    /// 1. 方法必须是公有的（public）、静态的（static）方法。
    /// 2. 使用 [AgentCommand(""功能描述"", category: ""Custom"")] 装饰方法，以便 AI 理解其作用与参数。
    /// 3. 推荐的返回值类型为 string，用于向 AI 返回操作日志或执行状态。
    /// 4. 尽量保持方法职责单一、参数清晰，方便 AI 智能传参。
    /// </summary>
    public static class AntigravityTasks
    {
        // 示例自定义命令：
        // [AgentCommand(""示例：在场景原点创建一个带有自定义名字的立方体"", category: ""Custom"")]
        // public static string CreateCustomCube(string cubeName)
        // {
        //     GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        //     cube.name = string.IsNullOrEmpty(cubeName) ? ""CustomCube"" : cubeName;
        //     cube.transform.position = Vector3.zero;
        //     return $""成功创建立方体并命名为: {cube.name}"";
        // }
    }
}";
                    File.WriteAllText(antigravityTasksPath, content, Encoding.UTF8);
                    AssetDatabase.ImportAsset("Assets/Editor/AntigravityTasks.cs");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AgentBridge] Failed to ensure user template files exist: " + ex.Message);
            }
        }

        // ---------- Batchmode Entry Point ----------

        /// <summary>
        /// Batchmode (无头模式) 入口函数。
        /// 读取命令行参数 `-agentCmdFile` 指定的 JSON 文件执行命令，并将结果写入 `-agentOutFile`。
        /// </summary>
        public static void ExecuteBatchTask()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string cmdFilePath = "";
                string outFilePath = "";

                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-agentCmdFile" && i + 1 < args.Length)
                        cmdFilePath = args[i + 1];
                    if (args[i] == "-agentOutFile" && i + 1 < args.Length)
                        outFilePath = args[i + 1];
                }

                if (string.IsNullOrEmpty(cmdFilePath) || string.IsNullOrEmpty(outFilePath))
                {
                    Debug.LogError("[AgentBridge] Missing -agentCmdFile or -agentOutFile arguments.");
                    EditorApplication.Exit(1);
                    return;
                }

                if (!File.Exists(cmdFilePath))
                {
                    Debug.LogError("[AgentBridge] Command file not found: " + cmdFilePath);
                    EditorApplication.Exit(1);
                    return;
                }

                string payload = File.ReadAllText(cmdFilePath);
                string resultJson = ExecuteCommandJson(payload);

                File.WriteAllText(outFilePath, resultJson);
                Debug.Log("[AgentBridge] Batch execution completed successfully.");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[AgentBridge] Batch execution failed: " + e.Message);
                EditorApplication.Exit(1);
            }
        }
    }

    // 一个简单的测试类，用于验证连接
    public static class AgentTestAPI
    {
        public static string Hello(string name)
        {
            Debug.Log("[AgentTestAPI] Received Hello request from " + name);
            return "Unity 2018.4 says Hello to " + name;
        }
    }
}
