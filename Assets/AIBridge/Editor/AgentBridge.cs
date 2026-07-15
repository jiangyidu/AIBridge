using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LitJson;
using UnityEditor;
using UnityEngine;
using AIBridge.Core;

namespace AIBridge.Agent
{
    /// <summary>
    /// 纯 C# Unity 执行宿主。旧版 HttpListener、端口发现和后台监听线程已移除；
    /// Agent 与工具路由器在同一 Editor 进程内直接调用。
    /// </summary>
    [InitializeOnLoad]
    public static class AgentBridge
    {
        private static readonly ConcurrentQueue<Action> DispatchQueue = new ConcurrentQueue<Action>();

        static AgentBridge()
        {
            BridgeLog.SetLogger(new UnityBridgeLogger());
            EnsureUserTemplateFilesExist();
            AgentCommandRegistry.Invalidate();
            AgentCommandRegistry.Scan();
            EnsureUpdateHooked();
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnAfterAssemblyReload()
        {
            AgentCommandRegistry.Invalidate();
            AgentCommandRegistry.Scan();
            EnsureUpdateHooked();
        }

        private static void EnsureUpdateHooked()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            Action action;
            int count = 0;
            while (count++ < 100 && DispatchQueue.TryDequeue(out action))
            {
                try { if (action != null) action(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        /// <summary>兼容旧接口：纯 C# 模式无需服务器，执行宿主始终可用。</summary>
        public static bool EnsureServerRunning() { EnsureUpdateHooked(); return true; }
        public static bool IsServerRunning() { return true; }
        public static int GetActivePort() { return 0; }
        public static string GetAgentUrl() { return "in-process://aibridge"; }
        public static void StopServer() { }

        public static void EnqueueMainThread(Action action)
        {
            if (action != null) DispatchQueue.Enqueue(action);
        }

        /// <summary>解析 JSON 载荷，利用反射调用指定静态方法。</summary>
        public static string ExecuteCommandJson(string jsonPayload)
        {
            JsonData responseData = AgentJson.NewObject();
            try
            {
                JsonData req = JsonMapper.ToObject(jsonPayload);
                string className = req.Keys.Contains("ClassName") ? req["ClassName"].ToString() : "";
                string methodName = req.Keys.Contains("MethodName") ? req["MethodName"].ToString() : "";
                if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(methodName))
                    throw new Exception("ClassName or MethodName is missing.");

                string[] rawArgs = ParseRawArguments(req);
                Type targetType = AgentUtility.ResolveType(className);
                if (targetType == null) throw new Exception("Class not found: " + className);

                MethodInfo method = ResolveMethod(targetType, methodName, rawArgs == null ? 0 : rawArgs.Length);
                if (method == null)
                    throw new Exception("Static method '" + methodName + "' not found or parameter count mismatch in class '" + className + "'");

                object[] invokeArgs = PrepareInvokeArguments(method.GetParameters(), rawArgs);
                object result = method.Invoke(null, invokeArgs);
                responseData["Success"] = true;
                responseData["Message"] = "Executed successfully.";
                responseData["Result"] = result != null ? result.ToString() : "null";
            }
            catch (Exception exception)
            {
                Exception actual = exception.InnerException ?? exception;
                responseData["Success"] = false;
                responseData["Message"] = actual.Message;
                responseData["ExceptionType"] = actual.GetType().Name;
                responseData["StackTrace"] = actual.StackTrace ?? "";
                if (actual.InnerException != null)
                {
                    responseData["InnerException"] = actual.InnerException.Message;
                    responseData["InnerStackTrace"] = actual.InnerException.StackTrace ?? "";
                }
            }
            return JsonMapper.ToJson(responseData);
        }

        private static string[] ParseRawArguments(JsonData req)
        {
            if (!req.Keys.Contains("Args") || !req["Args"].IsArray) return null;
            JsonData argsJson = req["Args"];
            string[] args = new string[argsJson.Count];
            for (int i = 0; i < argsJson.Count; i++) args[i] = argsJson[i] == null ? null : argsJson[i].ToString();
            return args;
        }

        private static MethodInfo ResolveMethod(Type type, string methodName, int argCount)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => method.Name == methodName).ToArray();
            MethodInfo exact = methods.FirstOrDefault(method => method.GetParameters().Length == argCount);
            if (exact != null) return exact;
            return methods.FirstOrDefault(method => method.GetParameters().Length > argCount &&
                method.GetParameters().Skip(argCount).All(parameter => parameter.HasDefaultValue));
        }

        private static object[] PrepareInvokeArguments(ParameterInfo[] parameters, string[] rawArgs)
        {
            if (parameters.Length == 0) return null;
            object[] invokeArgs = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (rawArgs != null && i < rawArgs.Length && rawArgs[i] != null)
                    invokeArgs[i] = ParseArgumentValue(rawArgs[i], parameters[i].ParameterType);
                else if (parameters[i].HasDefaultValue)
                    invokeArgs[i] = parameters[i].DefaultValue;
                else
                    invokeArgs[i] = parameters[i].ParameterType.IsValueType
                        ? Activator.CreateInstance(parameters[i].ParameterType) : null;
            }
            return invokeArgs;
        }

        private static object ParseArgumentValue(string value, Type parameterType)
        {
            if (parameterType == typeof(string)) return value;
            if (string.IsNullOrEmpty(value))
                return parameterType.IsValueType ? Activator.CreateInstance(parameterType) : null;

            if (parameterType.IsEnum) return Enum.Parse(parameterType, value, true);
            if (parameterType == typeof(int))
            {
                int integerValue;
                float floatValue;
                if (int.TryParse(value, out integerValue)) return integerValue;
                if (float.TryParse(value, out floatValue)) return (int)floatValue;
                throw new FormatException("Cannot parse '" + value + "' as int");
            }
            if (parameterType == typeof(float))
            {
                float floatValue;
                if (float.TryParse(value, out floatValue)) return floatValue;
                throw new FormatException("Cannot parse '" + value + "' as float");
            }
            if (parameterType == typeof(bool))
            {
                bool boolValue;
                if (bool.TryParse(value, out boolValue)) return boolValue;
                if (value == "1") return true;
                if (value == "0") return false;
                throw new FormatException("Cannot parse '" + value + "' as bool");
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(parameterType))
            {
                GameObject gameObject = GameObject.Find(value);
                if (gameObject != null)
                {
                    if (parameterType == typeof(GameObject)) return gameObject;
                    Component component = gameObject.GetComponent(parameterType);
                    if (component != null) return component;
                }
                string[] guids = AssetDatabase.FindAssets(value + " t:" + parameterType.Name);
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    return AssetDatabase.LoadAssetAtPath(path, parameterType);
                }
                return null;
            }

            if (parameterType.Namespace == "UnityEngine" && parameterType.IsValueType)
            {
                string[] parts = value.Trim('(', ')', ' ').Split(',');
                float[] values = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++) float.TryParse(parts[i].Trim(), out values[i]);
                if (parameterType == typeof(Vector2) && values.Length >= 2) return new Vector2(values[0], values[1]);
                if (parameterType == typeof(Vector3) && values.Length >= 3) return new Vector3(values[0], values[1], values[2]);
                if (parameterType == typeof(Vector4) && values.Length >= 4) return new Vector4(values[0], values[1], values[2], values[3]);
                if (parameterType == typeof(Color) && values.Length >= 3) return new Color(values[0], values[1], values[2], values.Length > 3 ? values[3] : 1f);
                if (parameterType == typeof(Quaternion) && values.Length >= 4) return new Quaternion(values[0], values[1], values[2], values[3]);
                if (parameterType == typeof(Bounds) && values.Length >= 6)
                    return new Bounds(new Vector3(values[0], values[1], values[2]), new Vector3(values[3], values[4], values[5]));
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            {
                MethodInfo mapper = typeof(JsonMapper).GetMethods()
                    .FirstOrDefault(method => method.Name == "ToObject" && method.IsGenericMethod && method.GetParameters().Length == 1);
                if (mapper != null) return mapper.MakeGenericMethod(parameterType).Invoke(null, new object[] { trimmed });
            }
            return Convert.ChangeType(value, parameterType);
        }

        private static void EnsureUserTemplateFilesExist()
        {
            try
            {
                string editorDirectory = Path.Combine(Application.dataPath, "Editor");
                if (!Directory.Exists(editorDirectory)) Directory.CreateDirectory(editorDirectory);

                string commandsPath = Path.Combine(editorDirectory, "AITempCommands.cs");
                if (!File.Exists(commandsPath))
                {
                    string content =
@"using UnityEngine;
using UnityEditor;

namespace AIBridge.Agent
{
    /// <summary>AI 临时命令容器。生成方法保存在 AITempCommandsGenerated 下的独立 partial 文件中。</summary>
    public static partial class AITempCommands
    {
    }
}";
                    File.WriteAllText(commandsPath, content, Encoding.UTF8);
                    AssetDatabase.ImportAsset("Assets/Editor/AITempCommands.cs");
                }

                string customPath = Path.Combine(editorDirectory, "AntigravityTasks.cs");
                if (!File.Exists(customPath))
                {
                    string content =
@"using UnityEngine;
using UnityEditor;
using AIBridge.Agent;

namespace AIBridge.Agent
{
    /// <summary>用户自定义 AI 任务命令类。</summary>
    public static class AntigravityTasks
    {
        // [AgentCommand(""示例命令"", category: ""Custom"")]
        // public static string Example() { return ""ok""; }
    }
}";
                    File.WriteAllText(customPath, content, Encoding.UTF8);
                    AssetDatabase.ImportAsset("Assets/Editor/AntigravityTasks.cs");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AgentBridge] 创建用户命令模板失败: " + ex.Message);
            }
        }

        public static void ExecuteBatchTask()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string commandFile = "";
                string outputFile = "";
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-agentCmdFile" && i + 1 < args.Length) commandFile = args[i + 1];
                    if (args[i] == "-agentOutFile" && i + 1 < args.Length) outputFile = args[i + 1];
                }
                if (string.IsNullOrEmpty(commandFile) || string.IsNullOrEmpty(outputFile) || !File.Exists(commandFile))
                    throw new Exception("Missing valid -agentCmdFile or -agentOutFile argument.");
                File.WriteAllText(outputFile, ExecuteCommandJson(File.ReadAllText(commandFile)));
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[AgentBridge] Batch execution failed: " + ex.Message);
                EditorApplication.Exit(1);
            }
        }
    }

    public static class AgentTestAPI
    {
        public static string Hello(string name)
        {
            return "Unity 2018.4 says Hello to " + name;
        }
    }
}
