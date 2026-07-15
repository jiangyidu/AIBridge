using System;
using System.Collections.Generic;
using LitJson;

namespace AIBridge.Agent
{
    /// <summary>
    /// Unity 端 JSON 统一工具。所有 Editor/AgentBridge/AgentToolRouter 相关 JSON 解析、构造尽量通过 LitJson 完成，
    /// 避免手写字符串截取在代码字段、转义字符、嵌套对象中出错。
    /// </summary>
    public static class AgentJson
    {
        public static JsonData ParseObject(string json)
        {
            if (string.IsNullOrEmpty(json)) return new JsonData();
            return JsonMapper.ToObject(json);
        }

        public static bool HasKey(JsonData data, string key)
        {
            if (data == null || !data.IsObject || string.IsNullOrEmpty(key)) return false;
            return data.Keys.Contains(key);
        }

        public static string GetString(JsonData data, string key, string defaultValue = "")
        {
            try
            {
                if (!HasKey(data, key) || data[key] == null) return defaultValue;
                return data[key].ToString();
            }
            catch { return defaultValue; }
        }

        public static int GetInt(JsonData data, string key, int defaultValue = 0)
        {
            string raw = GetString(data, key, null);
            int value;
            return int.TryParse(raw, out value) ? value : defaultValue;
        }

        public static bool GetBool(JsonData data, string key, bool defaultValue = false)
        {
            string raw = GetString(data, key, null);
            if (raw == null) return defaultValue;
            bool value;
            return bool.TryParse(raw, out value) ? value : defaultValue;
        }

        public static string[] GetStringArray(JsonData data, string key)
        {
            if (!HasKey(data, key) || data[key] == null || !data[key].IsArray) return new string[0];
            JsonData arr = data[key];
            string[] result = new string[arr.Count];
            for (int i = 0; i < arr.Count; i++) result[i] = arr[i] == null ? "" : arr[i].ToString();
            return result;
        }

        public static JsonData GetObject(JsonData data, string key)
        {
            if (!HasKey(data, key) || data[key] == null || !data[key].IsObject) return NewObject();
            return data[key];
        }

        public static string GetObjectJson(JsonData data, string key)
        {
            JsonData obj = GetObject(data, key);
            return JsonMapper.ToJson(obj);
        }

        public static JsonData NewObject()
        {
            JsonData data = new JsonData();
            data.SetJsonType(JsonType.Object);
            return data;
        }

        public static JsonData NewArray()
        {
            JsonData data = new JsonData();
            data.SetJsonType(JsonType.Array);
            return data;
        }

        public static JsonData StringArray(IEnumerable<string> values)
        {
            JsonData arr = NewArray();
            if (values != null)
            {
                foreach (string value in values) arr.Add(value ?? "");
            }
            return arr;
        }

        public static string ToJson(JsonData data)
        {
            return JsonMapper.ToJson(data ?? NewObject());
        }

        public static string Error(string message, Exception ex = null)
        {
            JsonData data = NewObject();
            data["Success"] = false;
            data["Error"] = message ?? "Unknown error";
            if (ex != null)
            {
                data["ExceptionType"] = ex.GetType().Name;
                data["StackTrace"] = ex.StackTrace ?? "";
            }
            return ToJson(data);
        }

        public static string SuccessMessage(string message)
        {
            JsonData data = NewObject();
            data["Success"] = true;
            data["Message"] = message ?? "";
            return ToJson(data);
        }
    }
}
