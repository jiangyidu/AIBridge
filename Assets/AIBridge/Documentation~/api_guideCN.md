# AI Bridge API 与自定义命令开发指南

向 AI Agent 暴露自定义的 Unity 编辑器任务极其简单，只需要使用 `[AgentCommand]` 特性声明即可。

## 1. 暴露自定义 C# 方法
要注册一个自定义命令：
1. 在 Editor 脚本中（或任何运行时脚本中）编写一个 `public static`（公共静态）方法。
2. 为该方法标上 `[AgentCommand("任务的功能描述", category: "分类名称")]` 特性。
3. 方法的返回值推荐使用 `string` 类型，以便向 AI 返回执行日志或状态反馈。

### 代码示例
```csharp
using UnityEngine;
using UnityEditor;
using AIBridge.Agent;

namespace AIBridge.Agent
{
    public static class LevelDesignCommands
    {
        [AgentCommand("为选中的 GameObject 批量添加随机点光源", category: "Level Design")]
        public static string AddLightsToSelection(float intensity, string colorHex)
        {
            var selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0)
            {
                return "Failed: 当前场景中没有选中任何 GameObject。";
            }

            Color lightColor = Color.white;
            ColorUtility.TryParseHtmlString(colorHex, out lightColor);

            int count = 0;
            foreach (var go in selectedObjects)
            {
                Light light = go.AddComponent<Light>();
                light.intensity = intensity;
                light.color = lightColor;
                count++;
            }

            return $"Success: 成功为 {count} 个选中的 GameObject 添加了 Light 组件。";
        }
    }
}
```

---

## 2. 参数解析与类型转换规则
C# HTTP 桥接服务端（`AgentBridge.cs`）会自动解析从 Python 传入的参数，并自动匹配和转换到方法的形参：
* **基础类型**：支持自动解析 `int`、`float`、`double`、`bool`、`string` 以及 `enum`（枚举，不区分大小写）。
* **UnityEngine 常用类型**：
  * `Vector2`、`Vector3`、`Vector4`、`Color`、`Quaternion`、`Bounds`（支持从标准字符串格式或 JSON 数组自动转换）。
  * `GameObject`：系统会自动使用 `GameObject.Find()` 在当前场景中查找对应名称的 GameObject 并自动传入。
* **后备方案**：支持传入复杂的 JSON 字符串，您可以在方法内使用 `JsonMapper` (LitJson) 自行反序列化。
