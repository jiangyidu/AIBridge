using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

namespace AIBridge.Agent
{
    /// <summary>
    /// AI 临时命令类
    /// 这是一个专供 AI 在对话时动态编写和执行临时任务的类。
    /// 工作流程：
    /// 1. AI 决定需要执行一个不在现有 AgentBridge 注册表中的复杂操作。
    /// 2. AI 将对应逻辑写成一个静态方法，并附带 [AgentCommand] 特性，写入到本文件中。
    /// 3. 保存文件后，Unity 自动开始重新编译，AgentBridge 服务会短暂离线并重启。
    /// 4. AI 使用 python unity_agent_controller.py wait_and_run --class AITempCommands --method <MethodName>
    ///    来等待 Unity 编译完成并立即执行该临时逻辑。
    /// 5. AI要优先使用现有的逻辑，在执行流程时要充分的考虑现有逻辑的能力边界，方法没有被移除掉就代表值得被考虑。
    /// 6. 执行完成后，AI 可根据需要保留或清除该方法。
    /// </summary>
    public static partial class AITempCommands
    {

    }
}
