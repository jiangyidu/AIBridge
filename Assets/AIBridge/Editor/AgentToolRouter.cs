using System;
using System.IO;
using System.Reflection;
using System.Text;
using LitJson;
using UnityEditor;
using UnityEngine;
using AIBridge.Core;

namespace AIBridge.Agent
{
    /// <summary>
    /// Unity 执行端工具路由器。
    /// 这里只做 Unity 侧能力执行，不包含任何 LLM 请求、Prompt 或 Agent 决策逻辑。
    /// JSON 解析/构造统一使用 LitJson/AgentJson。
    ///
    /// 2026-06 修复点：
    /// 1. compile_temp_method 不再把 AI 生成代码插入主 AITempCommands.cs，改为 partial 分文件隔离。
    /// 2. 对 code 字段做源码级标准化，修复 LLM/JSON 双重转义导致的 \" 被原样写入 C# 文件的问题。
    /// 3. 编译失败时删除本次生成文件并触发刷新，避免一个坏方法污染整个 Editor 编译域。
    /// </summary>
    public static class AgentToolRouter
    {
        private static bool _isWaitingCompile = false;
        private static string _pendingCompileTarget = null;
        private static string _pendingCompileKind = null; // method / type
        private static double _compileStartTime = 0;
        private const double COMPILE_TIMEOUT_SECONDS = 90.0;

        private static readonly string[] ForbiddenCodeFragments =
        {
            "Process.Start",
            "System.Diagnostics.Process",
            "Application.Quit",
            "EditorApplication.Exit",
            "Directory.Delete",
            "File.Delete(\"/",
            "File.Delete(\"C:",
            "File.Delete(\"D:",
            "EditorApplication.update +="
        };

        private static void SaveCompileState(bool isWaiting, string target, string kind, double startTime, string sourcePath)
        {
            _isWaitingCompile = isWaiting;
            _pendingCompileTarget = target;
            _pendingCompileKind = kind;
            _compileStartTime = startTime;
            EditorPrefs.SetBool("ATR_IsWaiting", isWaiting);
            EditorPrefs.SetString("ATR_Target", target ?? "");
            EditorPrefs.SetString("ATR_Kind", kind ?? "");
            EditorPrefs.SetFloat("ATR_StartTime", (float)startTime);
            EditorPrefs.SetString("ATR_SourcePath", sourcePath ?? "");
        }

        private static void ClearCompileState()
        {
            SaveCompileState(false, null, null, 0, null);
        }

        private static IEngineBridge Bridge => UnityEngineBridge.Instance;

        public static string ExecuteTool(string toolName, string argsJson)
        {
            try
            {
                switch (toolName)
                {
                    case BridgeProtocol.TOOL_EXECUTE_COMMAND: return HandleExecuteCommand(argsJson);
                    case BridgeProtocol.TOOL_COMPILE_TEMP_METHOD: return HandleCompileTempMethod(argsJson);
                    case BridgeProtocol.TOOL_COMPILE_SCRIPT: return HandleCompileScript(argsJson);
                    case BridgeProtocol.TOOL_CHECK_COMPILE_STATUS: return HandleCheckCompileStatus(argsJson);
                    case BridgeProtocol.TOOL_QUERY_SCENE: return Bridge.QueryScene();
                    case BridgeProtocol.TOOL_QUERY_OBJECT: return Bridge.QueryObject(AgentJson.GetString(AgentJson.ParseObject(argsJson), "objectName"));
                    case BridgeProtocol.TOOL_FIND_ASSETS: return Bridge.FindAssets(AgentJson.GetString(AgentJson.ParseObject(argsJson), "filter"));
                    case BridgeProtocol.TOOL_LIST_COMMANDS: return SceneObserver.ListAvailableCommands();
                    case BridgeProtocol.TOOL_LIST_AI_SCRIPTS: return SceneObserver.ListAITempScripts();
                    case BridgeProtocol.TOOL_GET_COMPILE_ERRORS: return Bridge.GetCompileErrors();
                    case BridgeProtocol.TOOL_READ_COMMAND_SOURCE: return HandleReadCommandSource(argsJson);
                    case BridgeProtocol.TOOL_GET_CONSOLE_LOGS:
                        {
                            JsonData args = AgentJson.ParseObject(argsJson);
                            int count = AgentJson.GetInt(args, "count", 10);
                            count = Mathf.Clamp(count, 1, 50);
                            return Bridge.GetRecentLogs(count);
                        }
                    case BridgeProtocol.TOOL_CREATE_GAMEOBJECT: return HandleCreateGameObject(argsJson);
                    case BridgeProtocol.TOOL_CREATE_MATERIAL: return HandleCreateMaterial(argsJson);
                    case BridgeProtocol.TOOL_SET_MATERIAL: return HandleSetMaterial(argsJson);
                    case BridgeProtocol.TOOL_ATTACH_SCRIPT: return HandleAttachScript(argsJson);
                    default:
                        return BuildError("Unknown tool: " + toolName);
                }
            }
            catch (Exception ex)
            {
                return BuildError(ex.Message, ex);
            }
        }

        private static string HandleExecuteCommand(string argsJson)
        {
            JsonData argsData = AgentJson.ParseObject(argsJson);
            string cls = AgentJson.GetString(argsData, "className");
            string method = AgentJson.GetString(argsData, "methodName");
            string[] args = AgentJson.GetStringArray(argsData, "args");
            JsonData payload = AgentJson.NewObject();
            payload["ClassName"] = cls;
            payload["MethodName"] = method;
            payload["Args"] = AgentJson.StringArray(args);
            return AgentBridge.ExecuteCommandJson(JsonMapper.ToJson(payload));
        }

        private static string HandleCompileTempMethod(string argsJson)
        {
            JsonData argsData = AgentJson.ParseObject(argsJson);
            string methodName = AgentJson.GetString(argsData, "methodName");
            string code = NormalizeGeneratedSource(AgentJson.GetString(argsData, "code"));
            string[] runArgs = AgentJson.GetStringArray(argsData, "args");

            if (string.IsNullOrEmpty(methodName) || string.IsNullOrEmpty(code))
                return BuildError("methodName 或 code 字段不能为空");
            if (!IsSafeIdentifier(methodName))
                return BuildError("methodName 不是合法的 C# 方法名：" + methodName);
            if (!CodeDeclaresMethod(code, methodName))
                return BuildError("code 字段中没有声明目标方法：" + methodName + "。请让 code 包含完整的 public static 方法声明。");

            string safetyError = ValidateGeneratedCode(code);
            if (!string.IsNullOrEmpty(safetyError)) return BuildError(safetyError);

            if (IsTempMethodLoaded(methodName))
            {
                JsonData payload = AgentJson.NewObject();
                payload["ClassName"] = "AIBridge.Agent.AITempCommands";
                payload["MethodName"] = methodName;
                payload["Args"] = AgentJson.StringArray(runArgs);
                string execResult = AgentBridge.ExecuteCommandJson(JsonMapper.ToJson(payload));
                JsonData response = AgentJson.NewObject();
                response["Success"] = true;
                response["Skipped"] = true;
                response["Message"] = "方法 " + methodName + " 已存在并已加载，已跳过编译直接执行";
                response["ExecuteResult"] = JsonMapper.ToObject(execResult);
                return JsonMapper.ToJson(response);
            }

            string generatedDir = GetGeneratedCommandsDir();
            if (!Directory.Exists(generatedDir)) Directory.CreateDirectory(generatedDir);
            string sourcePath = Path.Combine(generatedDir, "AITempCommands_" + SanitizeFileName(methodName) + ".cs");
            string sourceText = BuildGeneratedTempCommandFile(methodName, code);

            CompileWatcher.Reset();
            File.WriteAllText(sourcePath, sourceText, Encoding.UTF8);
            AssetDatabase.Refresh();
            SaveCompileState(true, methodName, "method", EditorApplication.timeSinceStartup, sourcePath);

            JsonData result = AgentJson.NewObject();
            result["Success"] = true;
            result["Status"] = "compiling";
            result["Message"] = "已将方法 " + methodName + " 写入独立 partial 文件并触发编译，请调用 check_compile_status 轮询编译结果";
            result["TargetMethod"] = methodName;
            result["TargetClass"] = "AIBridge.Agent.AITempCommands";
            result["SourcePath"] = ToAssetRelativePath(sourcePath);
            result["Args"] = AgentJson.StringArray(runArgs);
            return JsonMapper.ToJson(result);
        }

        private static string HandleReadCommandSource(string argsJson)
        {
            JsonData argsData = AgentJson.ParseObject(argsJson);
            string methodName = AgentJson.GetString(argsData, "methodName");
            string scriptName = AgentJson.GetString(argsData, "scriptName");

            if (string.IsNullOrEmpty(methodName) && string.IsNullOrEmpty(scriptName))
                return BuildError("methodName 和 scriptName 不能同时为空");

            JsonData result = AgentJson.NewObject();
            result["Success"] = false;

            if (!string.IsNullOrEmpty(methodName))
            {
                string generatedDir = GetGeneratedCommandsDir();
                string sourcePath = Path.Combine(generatedDir, "AITempCommands_" + SanitizeFileName(methodName) + ".cs");
                if (File.Exists(sourcePath))
                {
                    result["Success"] = true;
                    result["MethodName"] = methodName;
                    result["Code"] = File.ReadAllText(sourcePath, Encoding.UTF8);
                    return JsonMapper.ToJson(result);
                }
            }

            if (!string.IsNullOrEmpty(scriptName))
            {
                if (!scriptName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    scriptName += ".cs";
                string scriptsDir = Path.Combine(Application.dataPath, "Scripts", "AITemp");
                string scriptPath = Path.Combine(scriptsDir, scriptName);
                if (File.Exists(scriptPath))
                {
                    result["Success"] = true;
                    result["ScriptName"] = scriptName;
                    result["Code"] = File.ReadAllText(scriptPath, Encoding.UTF8);
                    return JsonMapper.ToJson(result);
                }
            }

            result["Message"] = "未找到指定的方法或脚本源文件";
            return JsonMapper.ToJson(result);
        }

        private static string HandleCompileScript(string argsJson)
        {
            JsonData argsData = AgentJson.ParseObject(argsJson);
            string filename = AgentJson.GetString(argsData, "filename");
            string code = NormalizeGeneratedSource(AgentJson.GetString(argsData, "code"));

            if (string.IsNullOrEmpty(filename) || string.IsNullOrEmpty(code))
                return BuildError("filename 或 code 字段不能为空");

            string safetyError = ValidateGeneratedCode(code);
            if (!string.IsNullOrEmpty(safetyError)) return BuildError(safetyError);

            if (!filename.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) filename += ".cs";
            filename = SanitizeFileName(Path.GetFileNameWithoutExtension(filename)) + ".cs";
            string typeName = Path.GetFileNameWithoutExtension(filename);
            string scriptsDir = Path.Combine(Application.dataPath, "Scripts", "AITemp");
            string scriptPath = Path.Combine(scriptsDir, filename);

            if (File.Exists(scriptPath) && AgentUtility.IsTypeLoaded(typeName))
            {
                bool forceOverwrite = AgentJson.GetBool(argsData, "forceOverwrite", false);
                if (!forceOverwrite)
                {
                    JsonData response = AgentJson.NewObject();
                    response["Success"] = true;
                    response["Skipped"] = true;
                    response["TypeName"] = typeName;
                    response["Message"] = "脚本 " + filename + " 已存在且类型已加载，可直接挂载。若要强制覆盖并重新编译，请传入 forceOverwrite 为 true 的参数。";
                    return JsonMapper.ToJson(response);
                }
            }

            if (!Directory.Exists(scriptsDir)) Directory.CreateDirectory(scriptsDir);
            CompileWatcher.Reset();
            File.WriteAllText(scriptPath, code, Encoding.UTF8);
            AssetDatabase.Refresh();
            SaveCompileState(true, typeName, "type", EditorApplication.timeSinceStartup, scriptPath);

            JsonData result = AgentJson.NewObject();
            result["Success"] = true;
            result["Status"] = "compiling";
            result["Message"] = "已写入或重新触发脚本 " + filename + " 编译，请调用 check_compile_status 轮询编译结果";
            result["TypeName"] = typeName;
            result["SourcePath"] = ToAssetRelativePath(scriptPath);
            return JsonMapper.ToJson(result);
        }

        private static string HandleCheckCompileStatus(string argsJson)
        {
            if (!_isWaitingCompile && EditorPrefs.GetBool("ATR_IsWaiting", false))
            {
                _isWaitingCompile = true;
                _pendingCompileTarget = EditorPrefs.GetString("ATR_Target", "");
                _pendingCompileKind = EditorPrefs.GetString("ATR_Kind", "");
                _compileStartTime = EditorPrefs.GetFloat("ATR_StartTime", 0f);
            }

            if (!_isWaitingCompile)
            {
                if (!CompileWatcher.LastCompileSucceeded)
                {
                    JsonData err = AgentJson.NewObject();
                    err["Status"] = "error";
                    err["Success"] = false;
                    err["Message"] = "最近一次编译失败";
                    err["CompileErrors"] = JsonMapper.ToObject(CompileWatcher.GetErrorsJson());
                    return JsonMapper.ToJson(err);
                }
                JsonData idle = AgentJson.NewObject();
                idle["Status"] = "idle";
                idle["Success"] = true;
                idle["Message"] = "当前没有进行中的编译任务";
                return JsonMapper.ToJson(idle);
            }

            double elapsed = EditorApplication.timeSinceStartup - _compileStartTime;
            string kind = _pendingCompileKind;
            string targetName = _pendingCompileTarget;

            if (elapsed > COMPILE_TIMEOUT_SECONDS)
            {
                ClearCompileState();
                JsonData timeout = AgentJson.NewObject();
                timeout["Status"] = "timeout";
                timeout["Success"] = false;
                timeout["Message"] = "编译等待超时，目标：" + targetName;
                timeout["CompileErrors"] = JsonMapper.ToObject(CompileWatcher.GetErrorsJson());
                return JsonMapper.ToJson(timeout);
            }

            if (EditorApplication.isCompiling)
            {
                JsonData compiling = AgentJson.NewObject();
                compiling["Status"] = "compiling";
                compiling["Success"] = true;
                compiling["Message"] = "Unity 正在编译中，请稍后再次调用 check_compile_status";
                compiling["ElapsedSeconds"] = elapsed;
                return JsonMapper.ToJson(compiling);
            }

            if (!CompileWatcher.LastCompileSucceeded)
            {
                string rollbackMessage = RollbackPendingGeneratedSource();
                ClearCompileState();
                JsonData err = AgentJson.NewObject();
                err["Status"] = "error";
                err["Success"] = false;
                err["Message"] = "编译失败，已回滚本次生成文件。请修正代码后重试";
                err["Rollback"] = rollbackMessage;
                err["CompileErrors"] = JsonMapper.ToObject(CompileWatcher.GetErrorsJson());
                return JsonMapper.ToJson(err);
            }

            bool ready = false;
            if (kind == "type")
            {
                ready = AgentUtility.IsTypeLoaded(targetName);
            }
            else if (kind == "method")
            {
                AgentCommandRegistry.Invalidate();
                AgentCommandRegistry.Scan();
                if (IsTempMethodLoaded(targetName)) ready = true;
            }

            if (!ready)
            {
                JsonData wait = AgentJson.NewObject();
                wait["Status"] = "compiling";
                wait["Success"] = true;
                wait["Message"] = "编译已完成但目标尚未加载，Domain Reload 可能仍在进行，请稍后再次调用";
                wait["ElapsedSeconds"] = elapsed;
                return JsonMapper.ToJson(wait);
            }

            ClearCompileState();
            JsonData done = AgentJson.NewObject();
            done["Status"] = "done";
            done["Success"] = true;
            if (kind == "method")
            {
                done["Message"] = "方法 " + targetName + " 编译完成并已注册，现在可以调用 execute_command 执行";
                done["TargetMethod"] = targetName;
                done["TargetClass"] = "AIBridge.Agent.AITempCommands";
            }
            else
            {
                done["Message"] = "脚本 " + targetName + " 编译完成，现在可以调用 execute_command AttachScriptToGameObject 挂载";
                done["TypeName"] = targetName;
            }
            return JsonMapper.ToJson(done);
        }

        private static bool TryParseVector3(string s, out Vector3 vec)
        {
            vec = Vector3.zero;
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Split(',');
            if (parts.Length < 3) return false;
            float x, y, z;
            if (float.TryParse(parts[0], out x) && float.TryParse(parts[1], out y) && float.TryParse(parts[2], out z))
            {
                vec = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        private static bool TryParseColor(string s, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.StartsWith("#"))
            {
                return ColorUtility.TryParseHtmlString(s, out color);
            }
            string[] parts = s.Split(',');
            if (parts.Length >= 3)
            {
                float r, g, b, a = 1f;
                if (float.TryParse(parts[0], out r) && float.TryParse(parts[1], out g) && float.TryParse(parts[2], out b))
                {
                    if (parts.Length >= 4) float.TryParse(parts[3], out a);
                    color = new Color(r, g, b, a);
                    return true;
                }
            }
            return false;
        }

        private static string HandleCreateGameObject(string argsJson)
        {
            JsonData args = AgentJson.ParseObject(argsJson);
            string type = AgentJson.GetString(args, "type", "Cube");
            string name = AgentJson.GetString(args, "name");
            if (string.IsNullOrEmpty(name))
                return BuildError("GameObject 名称 (name) 不能为空");

            GameObject go = null;
            if (type.Equals("Cube", StringComparison.OrdinalIgnoreCase))
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            else if (type.Equals("Sphere", StringComparison.OrdinalIgnoreCase))
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            else if (type.Equals("Cylinder", StringComparison.OrdinalIgnoreCase))
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            else if (type.Equals("Plane", StringComparison.OrdinalIgnoreCase))
                go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            else
                go = new GameObject();

            go.name = name;

            Vector3 pos;
            if (TryParseVector3(AgentJson.GetString(args, "position"), out pos))
                go.transform.position = pos;
            else
                go.transform.position = Vector3.zero;

            Vector3 rot;
            if (TryParseVector3(AgentJson.GetString(args, "rotation"), out rot))
                go.transform.rotation = Quaternion.Euler(rot);
            else
                go.transform.rotation = Quaternion.identity;

            Vector3 scale;
            if (TryParseVector3(AgentJson.GetString(args, "scale"), out scale))
                go.transform.localScale = scale;
            else
                go.transform.localScale = Vector3.one;

            string parentName = AgentJson.GetString(args, "parent");
            if (!string.IsNullOrEmpty(parentName))
            {
                GameObject parentGo = GameObject.Find(parentName);
                if (parentGo != null)
                {
                    go.transform.SetParent(parentGo.transform);
                }
                else
                {
                    BridgeLog.LogWarning($"[AgentToolRouter] Parent GameObject '{parentName}' not found.");
                }
            }

            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);

            JsonData res = AgentJson.NewObject();
            res["Success"] = true;
            res["Message"] = $"成功创建 GameObject: {name} (类型: {type})";
            res["ObjectName"] = name;
            return JsonMapper.ToJson(res);
        }

        private static string HandleCreateMaterial(string argsJson)
        {
            JsonData args = AgentJson.ParseObject(argsJson);
            string materialName = AgentJson.GetString(args, "materialName");
            if (string.IsNullOrEmpty(materialName))
                return BuildError("材质名称 (materialName) 不能为空");

            string shaderName = AgentJson.GetString(args, "shaderName", "Standard");
            string colorStr = AgentJson.GetString(args, "color", "1,1,1,1");

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
            }

            if (shader == null)
                return BuildError("在项目中找不到指定的 Shader: " + shaderName);

            Material mat = new Material(shader);

            Color color;
            if (TryParseColor(colorStr, out color))
            {
                if (mat.HasProperty("_Color")) mat.color = color;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            }

            string dir = Path.Combine(Application.dataPath, "Materials");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string assetPath = "Assets/Materials/" + materialName + ".mat";
            AssetDatabase.CreateAsset(mat, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            JsonData res = AgentJson.NewObject();
            res["Success"] = true;
            res["Message"] = $"成功创建材质 {materialName} 并保存至 {assetPath}";
            res["MaterialPath"] = assetPath;
            return JsonMapper.ToJson(res);
        }

        private static string HandleSetMaterial(string argsJson)
        {
            JsonData args = AgentJson.ParseObject(argsJson);
            string objectName = AgentJson.GetString(args, "objectName");
            string materialPath = AgentJson.GetString(args, "materialPath");

            if (string.IsNullOrEmpty(objectName) || string.IsNullOrEmpty(materialPath))
                return BuildError("objectName 和 materialPath 不能为空");

            GameObject go = GameObject.Find(objectName);
            if (go == null)
                return BuildError($"在场景中找不到 GameObject: '{objectName}'");

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return BuildError($"GameObject '{objectName}' 上没有 Renderer 组件，无法设置材质");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
            {
                string path = materialPath;
                if (!path.StartsWith("Assets/")) path = "Assets/Materials/" + path;
                if (!path.EndsWith(".mat")) path += ".mat";
                mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            }

            if (mat == null)
            {
                string filter = Path.GetFileNameWithoutExtension(materialPath) + " t:Material";
                string[] guids = AssetDatabase.FindAssets(filter);
                if (guids != null && guids.Length > 0)
                {
                    string foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    mat = AssetDatabase.LoadAssetAtPath<Material>(foundPath);
                }
            }

            if (mat == null)
                return BuildError($"找不到材质资源: '{materialPath}'");

            Undo.RecordObject(renderer, "Apply Material");
            renderer.sharedMaterial = mat;
            EditorUtility.SetDirty(renderer);

            JsonData res = AgentJson.NewObject();
            res["Success"] = true;
            res["Message"] = $"成功将材质 {mat.name} 应用到 {objectName} 上";
            return JsonMapper.ToJson(res);
        }

        private static string HandleAttachScript(string argsJson)
        {
            JsonData args = AgentJson.ParseObject(argsJson);
            string objectName = AgentJson.GetString(args, "objectName");
            string scriptName = AgentJson.GetString(args, "scriptName");

            if (string.IsNullOrEmpty(objectName) || string.IsNullOrEmpty(scriptName))
                return BuildError("objectName 和 scriptName 不能为空");

            GameObject go = GameObject.Find(objectName);
            if (go == null)
                return BuildError($"在场景中找不到 GameObject: '{objectName}'");

            Type type = AgentUtility.ResolveType(scriptName);
            if (type == null)
                return BuildError($"在当前域中找不到类型 '{scriptName}'。如果该类型是刚生成的，请等待编译完成");

            if (!typeof(MonoBehaviour).IsAssignableFrom(type))
                return BuildError($"类型 '{scriptName}' 不是 MonoBehaviour，无法挂载到 GameObject 上");

            Component comp = go.GetComponent(type);
            string msg;
            if (comp == null)
            {
                comp = Undo.AddComponent(go, type);
                msg = $"成功将脚本 '{scriptName}' 挂载到 GameObject '{objectName}'";
            }
            else
            {
                msg = $"GameObject '{objectName}' 已挂载了脚本 '{scriptName}'，跳过挂载";
            }

            EditorUtility.SetDirty(go);

            JsonData res = AgentJson.NewObject();
            res["Success"] = true;
            res["Message"] = msg;
            return JsonMapper.ToJson(res);
        }

        private static string NormalizeGeneratedSource(string code)
        {
            if (code == null) return "";
            code = code.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

            // 新增：HTML 实体反编码
            code = code.Replace("&lt;", "<").Replace("&gt;", ">")
                       .Replace("&amp;", "&").Replace("&quot;", "\"");

            if (code.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = code.IndexOf('\n');
                if (firstNewline >= 0) code = code.Substring(firstNewline + 1);
                int fence = code.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0) code = code.Substring(0, fence);
                code = code.Trim();
            }

            // 有些模型会把已经处于 JSON 字符串中的 C# 代码再次按 JSON 字符串转义，
            // 结果 LitJson 解析后源码中仍残留 \"，写入 .cs 后会触发 CS1056 / CS1010。
            if (LooksOverEscaped(code))
            {
                for (int i = 0; i < 3; i++)
                {
                    string before = code;
                    code = code.Replace("\\\\\"", "\""); // 两个反斜杠 + 引号
                    code = code.Replace("\\\"", "\"");     // 一个反斜杠 + 引号
                    if (before == code) break;
                }
            }

            if (code.IndexOf("\\n", StringComparison.Ordinal) >= 0 && code.IndexOf('\n') < 0)
            {
                code = code.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\t", "\t");
            }

            return code.Trim();
        }

        private static bool LooksOverEscaped(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            int escapedQuotes = CountOccurrences(code, "\\\"");
            if (escapedQuotes == 0) return false;
            int plainQuotes = CountUnescapedQuotes(code);
            if (plainQuotes == 0) return true;

            string[] patterns =
            {
                "Find(\\\"",
                "new GameObject(\\\"",
                "Shader.Find(\\\"",
                "ToString(\\\"",
                "return \\\"",
                "name = \\\"",
                "Debug.Log(\\\""
            };
            for (int i = 0; i < patterns.Length; i++)
            {
                if (code.IndexOf(patterns[i], StringComparison.Ordinal) >= 0) return true;
            }
            return escapedQuotes > plainQuotes * 2;
        }

        private static int CountOccurrences(string text, string needle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle)) return 0;
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        private static int CountUnescapedQuotes(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '"') continue;
                int slashCount = 0;
                int j = i - 1;
                while (j >= 0 && text[j] == '\\') { slashCount++; j--; }
                if (slashCount % 2 == 0) count++;
            }
            return count;
        }

        private static string BuildGeneratedTempCommandFile(string methodName, string methodCode)
        {
            string code = methodCode.Trim();
            if (code.IndexOf("[AgentCommand", StringComparison.Ordinal) < 0)
            {
                code = "        [AgentCommand(\"AI动态生成临时命令: " + methodName + "。\", category: \"Temp\")]\n" + Indent(code, 8);
            }
            else
            {
                code = Indent(code, 8);
            }

            return
                "// <auto-generated>\n" +
                "// This file is generated by AI Bridge compile_temp_method.\n" +
                "// One generated method per file prevents bad AI code from corrupting AITempCommands.cs.\n" +
                "// </auto-generated>\n" +
                "using System;\n" +
                "using System.IO;\n" +
                "using System.Linq;\n" +
                "using System.Collections.Generic;\n" +
                "using UnityEngine;\n" +
                "using UnityEditor;\n\n" +
                "namespace AIBridge.Agent\n{\n" +
                "    public static partial class AITempCommands\n    {\n" +
                code + "\n" +
                "    }\n" +
                "}\n";
        }

        private static string Indent(string code, int spaces)
        {
            string prefix = new string(' ', spaces);
            string[] lines = code.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                if (lines[i].Length > 0) sb.Append(prefix);
                sb.Append(lines[i]);
            }
            return sb.ToString();
        }

        private static bool CodeDeclaresMethod(string code, string methodName)
        {
            return code.IndexOf(" " + methodName + "(", StringComparison.Ordinal) >= 0
                || code.IndexOf("\t" + methodName + "(", StringComparison.Ordinal) >= 0
                || code.IndexOf("\n" + methodName + "(", StringComparison.Ordinal) >= 0;
        }

        private static bool IsTempMethodLoaded(string methodName)
        {
            Type t = AgentUtility.ResolveType("AIBridge.Agent.AITempCommands");
            if (t == null) return false;
            MethodInfo method = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return method != null;
        }

        private static string RollbackPendingGeneratedSource()
        {
            string sourcePath = EditorPrefs.GetString("ATR_SourcePath", "");
            if (string.IsNullOrEmpty(sourcePath)) return "没有记录本次生成文件，未执行文件回滚";
            try
            {
                string full = Path.GetFullPath(sourcePath);
                string assets = Path.GetFullPath(Application.dataPath);
                bool underProjectAssets = full.StartsWith(assets, StringComparison.OrdinalIgnoreCase);
                if (!underProjectAssets) return "拒绝删除 Assets 目录外文件：" + sourcePath;
                if (File.Exists(full))
                {
                    File.Delete(full);
                    string meta = full + ".meta";
                    if (File.Exists(meta)) File.Delete(meta);
                    AssetDatabase.Refresh();
                    return "已删除本次生成文件：" + ToAssetRelativePath(full);
                }
                return "本次生成文件不存在，无需删除：" + sourcePath;
            }
            catch (Exception ex)
            {
                return "回滚失败：" + ex.Message;
            }
        }

        private static string GetGeneratedCommandsDir()
        {
            return Path.Combine(Application.dataPath, "Editor", "AITempCommandsGenerated");
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Generated";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_') sb.Append(c);
            }
            if (sb.Length == 0) sb.Append("Generated");
            if (sb[0] >= '0' && sb[0] <= '9') sb.Insert(0, '_');
            return sb.ToString();
        }

        private static bool IsSafeIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (!((value[0] >= 'a' && value[0] <= 'z') || (value[0] >= 'A' && value[0] <= 'Z') || value[0] == '_')) return false;
            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }
            return true;
        }

        private static string ToAssetRelativePath(string fullPath)
        {
            try
            {
                string full = Path.GetFullPath(fullPath).Replace('\\', '/');
                string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
                if (full.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                    return "Assets" + full.Substring(assets.Length);
            }
            catch { }
            return fullPath;
        }

        private static string ValidateGeneratedCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return "生成代码为空";
            for (int i = 0; i < ForbiddenCodeFragments.Length; i++)
            {
                if (code.IndexOf(ForbiddenCodeFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return "生成代码包含被禁止的危险片段：" + ForbiddenCodeFragments[i];
            }

            if (LooksOverEscaped(code))
                return "生成代码仍疑似包含过度转义的引号，请重新生成 code 字段";

            return null;
        }

        private static string BuildError(string message, Exception ex = null)
        {
            return AgentJson.Error(message, ex);
        }
    }
}
