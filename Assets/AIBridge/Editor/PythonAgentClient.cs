using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using LitJson;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>
    /// Unity UI 访问 Python Agent Service 的极薄客户端。
    /// 这里不包含任何 LLM、Prompt、Agent 决策逻辑，只负责启动服务、发起会话、轮询事件和取消会话。
    /// </summary>
    public static class PythonAgentClient
    {
        public static int GetServicePort(string projectPath)
        {
            string normalizedPath = Path.GetFullPath(projectPath).Replace('\\', '/').ToLower();
            using (MD5 md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
                uint hash = (uint)hashBytes[0] | ((uint)hashBytes[1] << 8) | ((uint)hashBytes[2] << 16) | ((uint)hashBytes[3] << 24);
                return 11000 + (int)(hash % 2000);
            }
        }

        public static string GetServiceUrl(string projectPath)
        {
            return "http://127.0.0.1:" + GetServicePort(projectPath);
        }

        public static bool IsHealthy(string serviceUrl)
        {
            try
            {
                string json = Get(serviceUrl + "/health", 1500);
                JsonData data = AgentJson.ParseObject(json);
                return AgentJson.HasKey(data, "Success") && data["Success"].ToString().ToLower() == "true";
            }
            catch { return false; }
        }

        public static string Get(string url, int timeoutMs = 10000)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.Proxy = null;
            req.KeepAlive = false;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        public static string PostJson(string url, JsonData body, int timeoutMs = 10000)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.Proxy = null;
            req.KeepAlive = false;
            string json = JsonMapper.ToJson(body ?? AgentJson.NewObject());
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            req.ContentLength = bytes.Length;
            using (Stream stream = req.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }
    }
}
