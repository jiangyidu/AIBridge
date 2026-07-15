# 基础设置示例 (Basic Setup)

本示例包含一个极简的预配置场景，用于测试您的环境并向 AI Agent 发送第一条指令。

## 使用说明

1. 在 Unity 编辑器中打开 [SampleScene.unity](file:///f:/otherProject/AIBridge2026617/Assets/AIBridge/Samples/BasicSetup/SampleScene.unity) 场景。
2. 点击顶部菜单栏的 **`AIBridge -> Environment Setup Wizard`**，运行环境向导检测您的 Python 与第三方库依赖。
3. 确认环境无误后，点击顶部菜单栏的 **`AIBridge -> AI Assistant`** 打开 AI 聊天窗口。
4. 展开 **配置 (Configuration)** 面板：
   * 粘贴您的 API Key 并选择云端服务商（例如 DeepSeek、Gemini、Claude 等），或者
   * 配置您的本地 Ollama 参数。
5. 在聊天输入框中输入提示词，例如：
   > “在场景中心创建一个红色立方体。”
6. 观察 AI Agent 自动进行 ReAct 思考，并在 Unity 中自动生成并编译相关操作代码，最后在您的场景原点创建出该 Cube！
