using System;
using System.IO;
using UnityEngine;

namespace AIBridge.Agent
{
    internal sealed class AgentEnvironmentCheckResult
    {
        public bool Success;
        public string UnityVersion = "";
        public string StateDirectory = "";
        public string Domain = "";
        public bool HasActiveSession;
        public string Error = "";
    }

    /// <summary>
    /// 主窗口使用的纯 C# 运行环境检查，集中验证执行流程的必要前置条件。
    /// </summary>
    internal static class AgentEnvironmentValidator
    {
        public static AgentEnvironmentCheckResult Validate()
        {
            AgentEnvironmentCheckResult result = new AgentEnvironmentCheckResult
            {
                UnityVersion = Application.unityVersion,
                StateDirectory = AgentStateStore.StateDirectory,
                Domain = AgentDomainIdentity.Current
            };
            string writeProbePath = null;

            try
            {
                if (!IsUnity2018_4OrNewer(result.UnityVersion))
                    throw new InvalidOperationException("最低支持 Unity 2018.4，当前版本为 " + result.UnityVersion);

                AgentToolDefinitions.Init(Application.dataPath);
                if (string.IsNullOrEmpty(AgentToolDefinitions.GetToolsJson()))
                    throw new InvalidOperationException("AgentTools.json 未加载或内容为空");

                if (!Directory.Exists(result.StateDirectory))
                    Directory.CreateDirectory(result.StateDirectory);
                writeProbePath = Path.Combine(
                    result.StateDirectory,
                    "write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(writeProbePath, "ok");

                AIBridgeSettings.GetOrCreateSettings();
                if (!AgentBridge.EnsureInitialized())
                    throw new InvalidOperationException("进程内 AgentBridge 初始化失败");

                AgentSessionState session = PureCSharpAgent.CurrentState;
                result.HasActiveSession = session != null && session.active;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                if (!string.IsNullOrEmpty(writeProbePath) && File.Exists(writeProbePath))
                {
                    try { File.Delete(writeProbePath); }
                    catch { /* 临时探针清理失败不覆盖实际检测结果。 */ }
                }
            }

            return result;
        }

        private static bool IsUnity2018_4OrNewer(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            string[] parts = version.Split('.');
            int major;
            int minor;
            if (parts.Length < 2 ||
                !int.TryParse(parts[0], out major) ||
                !int.TryParse(parts[1], out minor))
                return false;
            return major > 2018 || (major == 2018 && minor >= 4);
        }
    }
}
