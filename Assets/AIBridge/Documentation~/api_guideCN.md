# API 与扩展指南

## 注册命令

在 Editor 或 Runtime 程序集中声明公有静态方法，并添加 `[AgentCommand]`：

```csharp
using AIBridge.Agent;
using UnityEditor;
using UnityEngine;

public static class MyEditorCommands
{
    [AgentCommand("创建一个可撤销的空物体", category: "Custom")]
    public static string CreateObject(string objectName)
    {
        GameObject value = new GameObject(string.IsNullOrEmpty(objectName) ? "Generated" : objectName);
        Undo.RegisterCreatedObjectUndo(value, "Create object");
        return value.name;
    }
}
```

Agent 在脚本域加载后扫描命令。参数应使用简单、可验证的类型，方法应支持 Undo、避免访问项目外路径，并尽量可重复调用。

## 固定工具

工具 Schema 位于 `Editor/AgentTools.json`，路由实现在 `AgentToolRouter`。新增工具时需要：

1. 在 `BridgeProtocol` 增加稳定名称。
2. 在 Schema 中描述参数和必填项。
3. 在路由器实现主线程逻辑、参数验证、Undo 和结构化 JSON 结果。
4. 明确工具是否有副作用；不要把长任务、网络或后台线程藏在工具中。

## 编译工具

生成代码必须通过 `CompileTransactionManager.BeginGeneratedSource`，不能先写文件再保存状态。调用者提供稳定 `operationId`、目标文件、目标类型/方法；结果为 `compiling` 时由 Agent 宿主等待最终事务结果。

编译工具不得隐式执行目标方法。执行必须作为后续独立工具调用，并遵守生成代码执行开关。

## 状态兼容

持久化阶段使用字符串常量，新增字段应提供安全默认值。不要把 API Key、令牌或完整敏感响应写入 `AgentSessionState`。
