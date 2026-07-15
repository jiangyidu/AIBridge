using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using AIBridge.Agent;

namespace AIBridge.Agent
{
    /// <summary>
    /// 用户自定义 AI 任务命令类。
    /// 在这里编写的方法只要标记了 [AgentCommand] 特性，就会自动被 AI 识别并供其调用。
    /// 
    /// 💡 使用规范与指南：
    /// 1. 方法必须是公有的（public）、静态的（static）方法。
    /// 2. 使用 [AgentCommand("功能描述", category: "Custom")] 装饰方法，以便 AI 理解其作用与参数。
    /// 3. 推荐的返回值类型为 string，用于向 AI 返回操作日志或执行状态。
    /// 4. 尽量保持方法职责单一、参数清晰，方便 AI 智能传参。
    /// </summary>
    public static class AntigravityTasks
    {
        // 示例自定义命令：
        // [AgentCommand("示例：在场景原点创建一个带有自定义名字的立方体", category: "Custom")]
        // public static string CreateCustomCube(string cubeName)
        // {
        //     GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        //     cube.name = string.IsNullOrEmpty(cubeName) ? "CustomCube" : cubeName;
        //     cube.transform.position = Vector3.zero;
        //     return $"成功创建立方体并命名为: {cube.name}";
        // }
    }
}
