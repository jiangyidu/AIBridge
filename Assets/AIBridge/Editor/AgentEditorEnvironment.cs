using System;
using System.Reflection;
using UnityEditor;

namespace AIBridge.Agent
{
    /// <summary>
    /// Editor 进程环境探测。通过反射调用较新 Unity 的导入 Worker API，
    /// 保持 Unity 2018.4 编译兼容，同时避免在资源导入子进程中启动 Agent 状态机。
    /// </summary>
    internal static class AgentEditorEnvironment
    {
        private static readonly bool AssetImportWorker = DetectAssetImportWorker();

        public static bool IsAssetImportWorker
        {
            get { return AssetImportWorker; }
        }

        private static bool DetectAssetImportWorker()
        {
            try
            {
                MethodInfo method = typeof(AssetDatabase).GetMethod(
                    "IsAssetImportWorkerProcess",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);
                return method != null && (bool)method.Invoke(null, null);
            }
            catch
            {
                return false;
            }
        }
    }
}
