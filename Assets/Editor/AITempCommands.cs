using UnityEngine;
using UnityEditor;

namespace AIBridge.Agent
{
    /// <summary>
    /// AI 临时命令容器。每个生成方法写入 AITempCommandsGenerated 下的独立 partial 文件。
    /// 纯 C# Agent 会在改写 Assets 前持久化编译事务，跨 Domain Reload 恢复，并在失败时回滚。
    /// 编译不会隐式执行生成代码；自动执行默认关闭，需用户显式开启。
    /// </summary>
    public static partial class AITempCommands
    {

    }
}
