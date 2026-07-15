using System;
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

        public static event Action StateSaved;

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
            return LoadWithBackup<AgentSessionState>(SessionPath);
        }

        public static void SaveSession(AgentSessionState state)
        {
            if (state == null) return;
            state.schemaVersion = 1;
            state.revision++;
            state.updatedUtcTicks = DateTime.UtcNow.Ticks;
            SaveObject(SessionPath, state);
            RaiseStateSaved();
        }

        public static AgentCompileTransactionState LoadCompile()
        {
            return LoadWithBackup<AgentCompileTransactionState>(CompilePath);
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

        private static T LoadWithBackup<T>(string path) where T : class
        {
            lock (Gate)
            {
                T value = TryLoad<T>(path);
                if (value != null) return value;
                return TryLoad<T>(path + ".bak");
            }
        }

        private static T TryLoad<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrEmpty(json)) return null;
                return JsonMapper.ToObject<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AIBridge] 无法读取状态文件 " + path + ": " + ex.Message);
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
    }
}
