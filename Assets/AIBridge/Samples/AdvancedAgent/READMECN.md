# 进阶 Agent 示例 (Advanced Agent)

本示例展示了如何编写自定义 C# 命令并使用 `[AgentCommand]` 特性将其注册并暴露给 AI Agent，以及 Agent 如何在编辑器中动态生成和编译 MonoBehaviour 脚本。

## 包含的文件

* **[CustomAgentCommandsDemo.cs](file:///f:/otherProject/AIBridge2026617/Assets/AIBridge/Samples/AdvancedAgent/Editor/CustomAgentCommandsDemo.cs)**：向 AI 暴露了两个自定义命令：
  * `CreateCircleOfObjects`：支持输入参数，在场景中环形排列创建 primitive（3D 基础几何体）。
  * `ColorObjectsWithPrefix`：搜索并改变所有名称匹配指定前缀的物体的材质颜色。
* **[AdvancedDemoScene.unity](file:///f:/otherProject/AIBridge2026617/Assets/AIBridge/Samples/AdvancedAgent/AdvancedDemoScene.unity)**：预配置的进阶测试场景。

## 如何测试

1. 在 Unity 中打开 [AdvancedDemoScene.unity](file:///f:/otherProject/AIBridge2026617/Assets/AIBridge/Samples/AdvancedAgent/AdvancedDemoScene.unity) 场景。
2. 打开 AI 助手窗口（`AIBridge -> AI Assistant`）。
3. 向 AI 发送一条自然语言指令，命令其使用您的自定义命令。例如：
   > “请在场景里环形创建 8 个球体（Sphere），半径设为 5。”
4. 观察 AI 如何识别并调用 [CustomAgentCommandsDemo.cs](file:///f:/otherProject/AIBridge2026617/Assets/AIBridge/Samples/AdvancedAgent/Editor/CustomAgentCommandsDemo.cs) 类中的 `CreateCircleOfObjects` 方法。
5. 接下来，继续发送指令：
   > “把我们刚刚创建的所有 Sphere 物体的颜色改成蓝色。”
6. AI 将调用 `ColorObjectsWithPrefix` 命令，将 `Sphere` 传入前缀参数，将 `blue` 传入颜色参数，从而一键染蓝物体。
