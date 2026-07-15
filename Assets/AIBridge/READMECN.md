# AI Bridge (AI 桥接器)

`AI Bridge`（UPM 包名：`com.LegendMars.aibridge`）是一个功能强大、轻量且可扩展的 Unity 编辑器 AI Agent 桥接框架。它连接本地大模型（如 Ollama） and 云端大模型（如 DeepSeek、Gemini、Claude 等），用于在 Unity 编辑器中直接执行 C# 命令并运行智能 ReAct 代理（Agent）。

该框架设计有独立的引擎抽象层（Engine-agnostic core），未来可以轻松扩展到其他 CAD/3D 软件环境（如 AutoCAD、Tekla、Unreal Engine 等）。

---

## 🏗 系统架构

AI Bridge 在 **Unity 编辑器 (C#)** 主机和 **Python Agent 服务**之间划分了职责。它们使用轻量级 HTTP 协议在动态分配的端口上进行双向通信。

### 架构工作流

```mermaid
graph TD
    subgraph Unity 编辑器 (C# 端)
        Window[AIAssistantWindow UI 窗口]
        BridgeClient[PythonAgentClient 客户端]
        Server[AgentBridge HTTP 服务端]
        Router[AgentToolRouter 工具路由器]
        Registry[AgentCommandRegistry 命令注册表]
        Compiler[CompileWatcher / UnityEngineBridge 编译器与网桥]
    end

    subgraph 后端服务 (Python 端)
        Service[run_agent_service.py]
        Runner[AgentRunner 会话执行器]
        Engine[UnityClient / EngineClient 引擎客户端]
    end

    subgraph 大语言模型提供商
        LLM[DeepSeek / Gemini / Claude / Ollama]
    end

    %% 用户交互
    Window -- 1. 启动会话 / 发送聊天 --> Service
    Service -- 2. 派生 --> Runner
    
    %% Agent 循环
    Runner -- 3. 请求 LLM 决策 --> LLM
    LLM -- 4. 工具调用 / 文本回复 --> Runner
    
    %% 引擎执行
    Runner -- 5. 执行工具请求 --> Engine
    Engine -- 6. HTTP POST /agent/tool --> Server
    Server -- 7. 路由并执行 --> Router
    Router -- 8. 读取场景 / 运行 C# 命令 --> Registry
    Router -- 9. 编译代码 / 挂载脚本 --> Compiler
    
    %% 响应循环
    Server -- 10. HTTP 响应 (工具结果) --> Engine
    Engine -- 11. 将结果喂回上下文 --> Runner
    
    %% UI 轮询
    Window -- 12. 轮询会话事件 --> Service
    Service -- 13. UI 事件流 / 聊天历史 --> Window
```

---

## 🌟 核心特性

1. **引擎无关核心 (`EngineClient`)**
   - Python 后端将通用的协议逻辑（HTTP 通信、连接重试、事件日志、动态编译轮询）与特定引擎细节解耦。通过继承 `EngineClient`，可方便地将该框架移植到 AutoCAD、Tekla、Unreal Engine 等其他软件中。

2. **双向 HTTP IPC 与哈希端口自动发现**
   - Unity 端托管一个本地 HTTP 服务器（`AgentBridge`）。其监听端口由当前项目路径的 MD5 哈希动态计算（范围 `8000-9999`），以防止在一台机器上同时运行多个 Unity 项目时发生端口冲突。
   - 活动端口会自动写入 `AgentController/agent_port.txt`，供 Python 后端自动读取和连接。

3. **动态代码编译与执行**
   - **临时方法**：Agent 可以在运行时自动编写 C# 静态方法并将其写入独立的 partial 文件中（注入 `AITempCommands.cs`）。
   - **MonoBehaviour 脚本**：Agent 可以将完整的 C# 脚本写入 `Assets/Scripts/AITemp/` 目录并触发 Unity 同步编译。
   - **编译状态轮询**：后端通过 `/agent/tool`（子命令 `check_compile_status`）轮询编译进度，并在失败时通过 `get_compile_errors` 返回详细的报错信息供 LLM 自行修正。

4. **丰富的编辑器聊天界面 (`AIAssistantWindow`)**
   - 功能完备的 Unity 编辑器窗口，支持远程 LLM API 和 Ollama 本地模型。
   - 支持展示现代推理模型（如 DeepSeek R1）的推理思索过程内容（`reasoning_content`）。
   - 包含手动执行面板，用于搜索和手动运行任何标有 `[AgentCommand]` 特性的 C# 方法。

5. **环境配置向导 (`AIBridgeSetupWizard`)**
   - 自动检测本地 Python 安装路径，检查 pip，并一键自动安装所需的 Python `requests` 依赖包。

6. **双模执行机制**
   - **交互模式**：直接在 Unity 编辑器 GUI 窗口中运行。
   - **Batchmode CLI 模式**：在 Unity 未打开时，通过 `unity_agent_controller.py` 使用 Unity 的 `-batchmode` 命令行静默启动执行自动化任务。

---

## 🚀 快速上手

### 前提条件
- **Unity**：2018.4.36f1 或更高版本。
- **Python**：3.8 或更高版本。

### 第一步：安装并设置 Python 环境
1. 在 Unity 中打开项目。
2. 在 Unity 顶部菜单栏中，点击 **`AIBridge -> Environment Setup Wizard`**。
3. 按照向导步骤操作，自动检测系统中的 Python 环境并安装所需的 `requests` 依赖库。

### 第二步：打开 AI 助手窗口
1. 在 Unity 顶部菜单栏中，点击 **`AIBridge -> AI Assistant`**。
2. 展开 **配置 (Configuration)** 面板。
3. 配置您的 API 终点：
   - **远程 API (Remote API)**：选择大模型供应商（DeepSeek、Gemini、Claude、通义千问、Kimi、智谱 GLM 等）并填写 **API Key**。
   - **Ollama 本地**：指定本地 Ollama 的 URL（如 `http://localhost:11434`）和模型名称。
4. 隐藏或保持配置面板展开。在输入框中键入您的任务或提问，然后点击 **发送 (Send)** 即可！

---

## 🛠 开发者指南

### 向 AI 暴露 C# 命令
您可以轻松向 AI 代理暴露自定义的 C# 编辑器任务。只需编写一个 `public static` 方法并用 `[AgentCommand]` 特性进行装饰：

```csharp
using UnityEngine;
using AIBridge.Agent;

namespace AIBridge.Agent
{
    public static class CustomEditorTasks
    {
        [AgentCommand("在场景中生成一个预制体阵列", category: "Custom")]
        public static string GenerateGrid(string prefabName, int rows, int cols, float spacing)
        {
            // 加载预制体，循环实例化
            // 向 LLM 返回执行状态或日志
            return $"成功生成 {rows}x{cols} 的 '{prefabName}' 阵列。";
        }
    }
}
```

项目每次重新载入程序集时，系统会自动扫描所有带该特性的方法，并将其注册到命令注册表（`AgentCommandRegistry`）中，供 Python 侧通过 `/agent/commands` 拉取。

### Python 命令行控制
要在命令行中直接控制 Unity（如果 Unity 正在运行，则通过 HTTP IPC 快速执行；如果已关闭，则自动以 batchmode 启动 Unity）：

```bash
# 列出所有在 Unity 中注册的 [AgentCommand] 命令
python AgentController/unity_agent_controller.py list

# 执行指定命令
python AgentController/unity_agent_controller.py run --class "AntigravityTasks" --method "CreateCustomCube" --args "MyProceduralCube"
```

---

## 📁 项目目录结构

```text
AIBridge/
├── Assets/
│   ├── Editor/
│   │   ├── AntigravityTasks.cs       # 自定义任务容器
│   │   └── AITempCommands.cs         # AI 临时命令 Partial 类定义
│   └── AIBridge/                     # UPM 核心包目录
│       ├── package.json              # 包配置清单
│       ├── LICENSE.md                # 许可协议
│       ├── Editor/                   # 编辑器 C# 脚本
│       │   ├── AIAssistantWindow.cs  # 聊天窗口 GUI
│       │   ├── AgentBridge.cs        # HTTP 本地服务器
│       │   ├── AgentToolRouter.cs    # 工具请求路由
│       │   ├── SceneObserver.cs      # 场景层级查看器
│       │   └── ...                   
│       ├── Runtime/                  # 运行时 C# 脚本
│       │   ├── AgentCommandAttribute.cs
│       │   └── ...
│       └── Samples/                 # 基础配置和示例资源
└── AgentController/                  # Python 后端
    ├── run_agent_service.py          # 后端 HTTP 会话服务启动器
    ├── unity_agent_controller.py     # 命令行 CLI 执行入口
    └── python_agent/                 # Python 核心源码
        ├── agent_core.py             # ReAct 循环编排
        ├── engine_client.py          # 引擎抽象通信基类
        ├── unity_client.py           # Unity 专有客户端实现
        ├── service.py                # 会话管理 HTTP 服务
        └── providers.py              # LLM 客户端包装类
```

---

## 📄 许可协议
本项目基于 MIT 许可协议开源。详情请参阅 [LICENSE.md](file:///f:/otherProject/AIBridge2026617/Assets/AIBridge/LICENSE.md) 文件。
