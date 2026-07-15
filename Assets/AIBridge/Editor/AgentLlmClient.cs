using System;
using System.Collections.Generic;
using System.Text;
using LitJson;
using UnityEngine.Networking;

namespace AIBridge.Agent
{
    public sealed class AgentLlmResponse
    {
        public bool success;
        public string content = "";
        public string reasoningContent = "";
        public string error = "";
        public List<AgentToolCallState> toolCalls = new List<AgentToolCallState>();
    }

    /// <summary>一次非阻塞 LLM 请求。对象本身不跨 Domain Reload；请求意图已保存在 AgentSessionState。</summary>
    public sealed class AgentLlmOperation : IDisposable
    {
        private readonly UnityWebRequest _request;
        private readonly UnityWebRequestAsyncOperation _operation;
        private readonly bool _anthropic;

        public AgentLlmOperation(UnityWebRequest request, bool anthropic)
        {
            _request = request;
            _anthropic = anthropic;
            _operation = request.SendWebRequest();
        }

        public bool IsDone
        {
            get { return _operation != null && _operation.isDone; }
        }

        public void Abort()
        {
            try { _request.Abort(); }
            catch { }
        }

        public AgentLlmResponse GetResponse()
        {
            AgentLlmResponse result = new AgentLlmResponse();
            if (!IsDone)
            {
                result.error = "LLM 请求尚未完成";
                return result;
            }

#if UNITY_2020_2_OR_NEWER
            bool failed = _request.result == UnityWebRequest.Result.ConnectionError ||
                          _request.result == UnityWebRequest.Result.ProtocolError ||
                          _request.result == UnityWebRequest.Result.DataProcessingError;
#else
            bool failed = _request.isNetworkError || _request.isHttpError;
#endif
            string responseText = _request.downloadHandler == null ? "" : _request.downloadHandler.text;
            if (failed)
            {
                result.error = "HTTP " + _request.responseCode + ": " + (_request.error ?? "请求失败") +
                               (string.IsNullOrEmpty(responseText) ? "" : "\n" + Limit(responseText, 4000));
                return result;
            }

            try
            {
                return _anthropic ? ParseAnthropic(responseText) : ParseOpenAi(responseText);
            }
            catch (Exception ex)
            {
                result.error = "无法解析 LLM 响应: " + ex.Message + "\n" + Limit(responseText, 4000);
                return result;
            }
        }

        public void Dispose()
        {
            if (_request != null) _request.Dispose();
        }

        private static AgentLlmResponse ParseOpenAi(string json)
        {
            AgentLlmResponse result = new AgentLlmResponse();
            JsonData root = AgentJson.ParseObject(json);
            if (!AgentJson.HasKey(root, "choices") || !root["choices"].IsArray || root["choices"].Count == 0)
            {
                result.error = AgentJson.HasKey(root, "error") ? JsonMapper.ToJson(root["error"]) : "响应中没有 choices";
                return result;
            }

            JsonData choice = root["choices"][0];
            JsonData message = AgentJson.GetObject(choice, "message");
            result.content = AgentJson.GetString(message, "content");
            result.reasoningContent = AgentJson.GetString(message, "reasoning_content");
            if (AgentJson.HasKey(message, "tool_calls") && message["tool_calls"].IsArray)
            {
                JsonData calls = message["tool_calls"];
                for (int i = 0; i < calls.Count; i++)
                {
                    JsonData function = AgentJson.GetObject(calls[i], "function");
                    AgentToolCallState call = new AgentToolCallState();
                    call.id = AgentJson.GetString(calls[i], "id", "call_" + i);
                    call.name = AgentJson.GetString(function, "name");
                    if (AgentJson.HasKey(function, "arguments"))
                    {
                        JsonData arguments = function["arguments"];
                        call.argumentsJson = arguments != null && arguments.IsObject
                            ? JsonMapper.ToJson(arguments) : (arguments == null ? "{}" : arguments.ToString());
                    }
                    result.toolCalls.Add(call);
                }
            }
            result.success = true;
            return result;
        }

        private static AgentLlmResponse ParseAnthropic(string json)
        {
            AgentLlmResponse result = new AgentLlmResponse();
            JsonData root = AgentJson.ParseObject(json);
            if (!AgentJson.HasKey(root, "content") || !root["content"].IsArray)
            {
                result.error = AgentJson.HasKey(root, "error") ? JsonMapper.ToJson(root["error"]) : "响应中没有 content";
                return result;
            }

            StringBuilder text = new StringBuilder();
            JsonData content = root["content"];
            for (int i = 0; i < content.Count; i++)
            {
                string type = AgentJson.GetString(content[i], "type");
                if (type == "text")
                {
                    if (text.Length > 0) text.AppendLine();
                    text.Append(AgentJson.GetString(content[i], "text"));
                }
                else if (type == "tool_use")
                {
                    AgentToolCallState call = new AgentToolCallState();
                    call.id = AgentJson.GetString(content[i], "id", "tool_call_" + i);
                    call.name = AgentJson.GetString(content[i], "name");
                    call.argumentsJson = AgentJson.HasKey(content[i], "input")
                        ? JsonMapper.ToJson(content[i]["input"]) : "{}";
                    result.toolCalls.Add(call);
                }
            }
            result.content = text.ToString();
            result.success = true;
            return result;
        }

        private static string Limit(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
            return text.Substring(0, max) + "...";
        }
    }

    public static class AgentLlmClient
    {
        public static AgentLlmOperation Start(AgentSessionState state, string apiKey)
        {
            bool anthropic = IsAnthropic(state);
            string url = state.apiUrl ?? "";
            if (anthropic && !url.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
                url = url.TrimEnd('/') + "/v1/messages";

            JsonData body = anthropic ? BuildAnthropicBody(state) : BuildOpenAiBody(state);
            byte[] payload = Encoding.UTF8.GetBytes(JsonMapper.ToJson(body));
            UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 120;
            request.SetRequestHeader("Content-Type", "application/json");
            if (anthropic)
            {
                request.SetRequestHeader("x-api-key", apiKey ?? "");
                request.SetRequestHeader("anthropic-version", "2023-06-01");
            }
            else if (!string.IsNullOrEmpty(apiKey))
            {
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            }
            return new AgentLlmOperation(request, anthropic);
        }

        private static JsonData BuildOpenAiBody(AgentSessionState state)
        {
            JsonData body = AgentJson.NewObject();
            body["model"] = state.model ?? "";
            body["max_tokens"] = 8192;
            body["messages"] = BuildOpenAiMessages(state.messages);
            try { body["tools"] = JsonMapper.ToObject(AgentToolDefinitions.GetToolsJson()); }
            catch { body["tools"] = AgentJson.NewArray(); }
            return body;
        }

        private static JsonData BuildOpenAiMessages(IList<AgentMessageState> messages)
        {
            JsonData array = AgentJson.NewArray();
            if (messages == null) return array;
            for (int i = 0; i < messages.Count; i++)
            {
                AgentMessageState source = messages[i];
                JsonData message = AgentJson.NewObject();
                message["role"] = source.role ?? "user";
                message["content"] = source.content ?? "";
                if (!string.IsNullOrEmpty(source.reasoningContent))
                    message["reasoning_content"] = source.reasoningContent;
                if (!string.IsNullOrEmpty(source.toolCallId))
                    message["tool_call_id"] = source.toolCallId;
                if (!string.IsNullOrEmpty(source.name))
                    message["name"] = source.name;
                if (source.toolCalls != null && source.toolCalls.Count > 0)
                {
                    JsonData calls = AgentJson.NewArray();
                    for (int j = 0; j < source.toolCalls.Count; j++)
                        calls.Add(BuildOpenAiToolCall(source.toolCalls[j]));
                    message["tool_calls"] = calls;
                }
                array.Add(message);
            }
            return array;
        }

        private static JsonData BuildOpenAiToolCall(AgentToolCallState call)
        {
            JsonData item = AgentJson.NewObject();
            item["id"] = call.id ?? "";
            item["type"] = "function";
            JsonData function = AgentJson.NewObject();
            function["name"] = call.name ?? "";
            function["arguments"] = call.argumentsJson ?? "{}";
            item["function"] = function;
            return item;
        }

        private static JsonData BuildAnthropicBody(AgentSessionState state)
        {
            JsonData body = AgentJson.NewObject();
            body["model"] = state.model ?? "";
            body["max_tokens"] = 8192;
            JsonData converted = AgentJson.NewArray();
            StringBuilder system = new StringBuilder();

            for (int i = 0; i < state.messages.Count; i++)
            {
                AgentMessageState source = state.messages[i];
                if (source.role == "system")
                {
                    if (system.Length > 0) system.AppendLine().AppendLine();
                    system.Append(source.content ?? "");
                    continue;
                }

                JsonData message = AgentJson.NewObject();
                if (source.role == "tool")
                {
                    message["role"] = "user";
                    JsonData blocks = AgentJson.NewArray();
                    JsonData block = AgentJson.NewObject();
                    block["type"] = "tool_result";
                    block["tool_use_id"] = source.toolCallId ?? "";
                    block["content"] = source.content ?? "";
                    blocks.Add(block);
                    message["content"] = blocks;
                }
                else if (source.role == "assistant" && source.toolCalls != null && source.toolCalls.Count > 0)
                {
                    message["role"] = "assistant";
                    JsonData blocks = AgentJson.NewArray();
                    if (!string.IsNullOrEmpty(source.content))
                    {
                        JsonData textBlock = AgentJson.NewObject();
                        textBlock["type"] = "text";
                        textBlock["text"] = source.content;
                        blocks.Add(textBlock);
                    }
                    for (int j = 0; j < source.toolCalls.Count; j++)
                    {
                        AgentToolCallState call = source.toolCalls[j];
                        JsonData tool = AgentJson.NewObject();
                        tool["type"] = "tool_use";
                        tool["id"] = call.id ?? "";
                        tool["name"] = call.name ?? "";
                        try { tool["input"] = JsonMapper.ToObject(call.argumentsJson ?? "{}"); }
                        catch { tool["input"] = AgentJson.NewObject(); }
                        blocks.Add(tool);
                    }
                    message["content"] = blocks;
                }
                else if (source.role == "user" || source.role == "assistant")
                {
                    message["role"] = source.role;
                    message["content"] = source.content ?? "";
                }
                else
                {
                    continue;
                }
                converted.Add(message);
            }

            if (system.Length > 0) body["system"] = system.ToString();
            body["messages"] = converted;
            body["tools"] = BuildAnthropicTools();
            return body;
        }

        private static JsonData BuildAnthropicTools()
        {
            JsonData result = AgentJson.NewArray();
            JsonData tools;
            try { tools = JsonMapper.ToObject(AgentToolDefinitions.GetToolsJson()); }
            catch { return result; }
            if (tools == null || !tools.IsArray) return result;
            for (int i = 0; i < tools.Count; i++)
            {
                JsonData source = tools[i];
                JsonData function = AgentJson.GetString(source, "type") == "function"
                    ? AgentJson.GetObject(source, "function") : source;
                string name = AgentJson.GetString(function, "name");
                if (string.IsNullOrEmpty(name)) continue;
                JsonData target = AgentJson.NewObject();
                target["name"] = name;
                target["description"] = AgentJson.GetString(function, "description");
                target["input_schema"] = AgentJson.HasKey(function, "parameters")
                    ? function["parameters"] : AgentJson.NewObject();
                result.Add(target);
            }
            return result;
        }

        private static bool IsAnthropic(AgentSessionState state)
        {
            return string.Equals(state.provider, "Claude", StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrEmpty(state.apiUrl) && state.apiUrl.IndexOf("anthropic.com", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
