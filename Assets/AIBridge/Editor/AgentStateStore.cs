using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LitJson;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>
    /// 将 Agent 续跑状态保存在 Library 中。Library 不进入 unitypackage，也不会污染用户 Assets。
    /// 使用临时文件 + 备份替换，防止 Editor 崩溃时留下半个 JSON 文件。
    /// </summary>
    public static class AgentStateStore
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, LoadFailure> LoadFailures =
            new Dictionary<string, LoadFailure>(StringComparer.OrdinalIgnoreCase);

        private sealed class LoadFailure
        {
            public string fingerprint = "";
            public string message = "";
        }

        static AgentStateStore()
        {
            // LitJson 将 JSON 中处于 Int32 范围的整数标记为 Int，而不会自动拓宽到 Int64。
            // Agent 状态中的 tick/revision 字段即使当前值为 0，也必须能恢复为 long。
            JsonMapper.RegisterImporter<int, long>(delegate(int value) { return Convert.ToInt64(value); });
        }

        public static event Action StateSaved;

        public static string LastSessionLoadError { get; private set; }

        public static bool HasSessionFile
        {
            get { return File.Exists(SessionPath) || File.Exists(SessionPath + ".bak"); }
        }

        public static string StateDirectory
        {
            get { return Path.Combine(ProjectRoot, "Library", "AIBridge"); }
        }

        public static string SessionPath
        {
            get { return Path.Combine(StateDirectory, "agent-state.json"); }
        }

        public static string CompilePath
        {
            get { return Path.Combine(StateDirectory, "compile-state.json"); }
        }

        public static string HistoryPath
        {
            get { return Path.Combine(StateDirectory, "chat-history.json"); }
        }

        public static string ProjectRoot
        {
            get { return Path.GetDirectoryName(Application.dataPath); }
        }

        public static AgentSessionState LoadSession()
        {
            string loadError;
            AgentSessionState state = LoadWithBackup<AgentSessionState>(SessionPath, out loadError);
            LastSessionLoadError = loadError;
            if (state != null && state.schemaVersion < 2)
            {
                // v1 没有 requireApiKey。升级中的远程会话按安全默认值处理，防止
                // Domain Reload 后因内存凭据丢失而发送空值或旧密文。
                state.requireApiKey = !string.Equals(state.provider, "openai-compatible", StringComparison.OrdinalIgnoreCase) &&
                                      !IsLoopbackUrl(state.apiUrl);
                state.schemaVersion = 2;
            }
            return state;
        }

        public static void SaveSession(AgentSessionState state)
        {
            if (state == null) return;
            state.schemaVersion = 2;
            state.revision++;
            state.updatedUtcTicks = DateTime.UtcNow.Ticks;
            SaveObject(SessionPath, state);
            RaiseStateSaved();
        }

        public static AgentCompileTransactionState LoadCompile()
        {
            string ignored;
            return LoadWithBackup<AgentCompileTransactionState>(CompilePath, out ignored);
        }

        public static void SaveCompile(AgentCompileTransactionState state)
        {
            if (state == null) return;
            state.schemaVersion = 1;
            state.updatedUtcTicks = DateTime.UtcNow.Ticks;
            SaveObject(CompilePath, state);
            RaiseStateSaved();
        }

        public static void DeleteCompileState()
        {
            lock (Gate)
            {
                DeleteIfExists(CompilePath);
                DeleteIfExists(CompilePath + ".bak");
                DeleteIfExists(CompilePath + ".tmp");
            }
            RaiseStateSaved();
        }

        public static void AppendHistory(string role, string content, string reasoning)
        {
            lock (Gate)
            {
                JsonData root = AgentJson.NewObject();
                if (File.Exists(HistoryPath))
                {
                    try { root = AgentJson.ParseObject(File.ReadAllText(HistoryPath, Encoding.UTF8)); }
                    catch { root = AgentJson.NewObject(); }
                }

                JsonData messages;
                if (AgentJson.HasKey(root, "messages") && root["messages"].IsArray)
                    messages = root["messages"];
                else
                    messages = AgentJson.NewArray();

                JsonData item = AgentJson.NewObject();
                item["role"] = role ?? "system";
                item["content"] = content ?? "";
                item["reasoning_content"] = reasoning ?? "";
                item["time"] = DateTime.Now.ToString("HH:mm:ss");
                messages.Add(item);
                root["messages"] = messages;
                WriteAtomic(HistoryPath, JsonMapper.ToJson(root));
            }
            RaiseStateSaved();
        }

        public static void WriteTextAtomic(string path, string text)
        {
            lock (Gate) WriteAtomic(path, text ?? "");
        }

        private static T LoadWithBackup<T>(string path, out string loadError) where T : class
        {
            lock (Gate)
            {
                string primaryError;
                T value = TryLoad<T>(path, out primaryError);
                if (value != null)
                {
                    loadError = "";
                    return value;
                }

                string backupError;
                value = TryLoad<T>(path + ".bak", out backupError);
                if (value != null)
                {
                    loadError = "";
                    return value;
                }

                if (!string.IsNullOrEmpty(primaryError) && !string.IsNullOrEmpty(backupError))
                    loadError = primaryError + "；备份同样无法读取：" + backupError;
                else
                    loadError = !string.IsNullOrEmpty(primaryError) ? primaryError : backupError;
                return null;
            }
        }

        private static T TryLoad<T>(string path, out string loadError) where T : class
        {
            loadError = "";
            try
            {
                if (!File.Exists(path))
                {
                    LoadFailures.Remove(path);
                    return null;
                }

                FileInfo info = new FileInfo(path);
                string fingerprint = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
                LoadFailure cached;
                if (LoadFailures.TryGetValue(path, out cached) && cached.fingerprint == fingerprint)
                {
                    loadError = cached.message;
                    return null;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrEmpty(json)) throw new JsonException("状态文件为空");
                T value = JsonMapper.ToObject<T>(json);
                LoadFailures.Remove(path);
                return value;
            }
            catch (Exception ex)
            {
                string fingerprint = "unavailable";
                try
                {
                    FileInfo info = new FileInfo(path);
                    fingerprint = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
                }
                catch { }

                loadError = "无法读取状态文件 " + path + ": " + ex.Message;
                LoadFailure previous;
                bool shouldLog = !LoadFailures.TryGetValue(path, out previous) || previous.fingerprint != fingerprint;
                LoadFailures[path] = new LoadFailure { fingerprint = fingerprint, message = loadError };
                if (shouldLog) Debug.LogWarning("[AIBridge] " + loadError);
                return null;
            }
        }

        private static void SaveObject(string path, object value)
        {
            lock (Gate)
            {
                string json = JsonMapper.ToJson(value);
                WriteAtomic(path, json);
            }
        }

        private static void WriteAtomic(string path, string text)
        {
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            string temp = path + ".tmp";
            string backup = path + ".bak";
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, backup, true);
                    return;
                }
                catch
                {
                    File.Copy(path, backup, true);
                    File.Delete(path);
                }
            }
            File.Move(temp, path);
        }

        private static void DeleteIfExists(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Debug.LogWarning("[AIBridge] 删除状态文件失败: " + ex.Message); }
        }

        private static void RaiseStateSaved()
        {
            Action handler = StateSaved;
            if (handler == null) return;
            try { handler(); }
            catch { }
        }

        private static bool IsLoopbackUrl(string url)
        {
            url = (url ?? "").Trim().ToLowerInvariant();
            return url.StartsWith("http://localhost") || url.StartsWith("https://localhost") ||
                   url.StartsWith("http://127.0.0.1") || url.StartsWith("https://127.0.0.1") ||
                   url.StartsWith("http://[::1]") || url.StartsWith("https://[::1]");
        }
    }
}
