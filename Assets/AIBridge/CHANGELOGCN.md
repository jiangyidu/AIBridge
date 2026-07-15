# 更新日志

本项目的所有重大更改都将记录在此文件中。

本文件格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，
并且本项目遵循 [语义化版本 2.0.0](https://semver.org/lang/zh-CN/) 规范。

---

## [1.0.0] - 2026-06-18

这是 **AI Bridge** 框架的初始发布版本，建立了本地/云端 LLM 代理（Agent）与 Unity 编辑器之间的稳定双向通信网桥。

### 新增
- **核心架构与通信 (Core Architecture & Communications)**:
  - 实现了基于 Unity `HttpListener` 的本地 HTTP 服务器（`AgentBridge`），在后台线程中运行，并注册了安全的生命周期回调（`InitializeOnLoad`）。
  - 添加了基于项目绝对路径 MD5 哈希计算监听端口的算法，使在一台电脑上多开不同 Unity 项目时能自动规避端口冲突。
  - 实现了 Unity 启动时自动生成 `AgentController/agent_port.txt` 端口记录文件，方便本地 Python 工具发现当前的 Unity 实例。
- **Python Agent 编排调度 (`python_agent`)**:
  - 创建了本地 Python 端 HTTP 服务（`service.py`），用于管理异步 Agent 会话状态，流式传输步骤事件，并维护持久的 JSON 聊天记录（`ai_chat_history.json`）。
  - 添加了包含完整 ReAct 循环的 `AgentRunner`（`agent_core.py`），支持模型工具链的并行解析与执行，并包含可用于中断会话的取消标志（Cancel token）。
  - 添加了引擎无关的 `EngineClient` 基类，用以封装常规的 HTTP 路由、超时时间以及请求重试逻辑。
  - 添加了继承自 `EngineClient` 的 `UnityClient`，重写了 Unity 特有的路径定位、端口解析及编译规范。
- **动态 C# 代码编译**:
  - 集成了 `compile_temp_method` 工具，允许 AI Agent 在运行时自动将自定义的 `public static` 辅助方法注入 `AITempCommands.cs` 临时 partial 类并触发 Unity 编译。
  - 集成了 `compile_script` 工具，允许 AI Agent 编写完整的 `MonoBehaviour` 脚本并将其保存至 Assets 文件夹中触发编译。
  - 添加了编译状态轮询工作流，通过 `check_compile_status` 和 `get_compile_errors` 查询编译器详细的警告和错误日志，帮助模型在编译失败时自纠错。
- **Unity 编辑器 GUI 与向导**:
  - 编写了 `AIAssistantWindow.cs` 对话窗口，提供直观的聊天界面、大模型提供商（Ollama、DeepSeek、Claude、Gemini、通义千问等）下拉配置框，并支持一键搜索及手动执行在编辑器中注册的 C# 命令。
  - 支持渲染现代推理大模型（如 DeepSeek R1）的推理思索过程内容（`reasoning_content`）。
  - 编写了 `AIBridgeSetupWizard.cs` 环境配置向导，自动检测系统中的 Python 环境并一键安装 pip 缺失的 `requests` 包。
- **命令行 CLI 工具**:
  - 编写了 `unity_agent_controller.py`，支持在终端控制台下列出命令清单、同步等待编译、以及执行 Unity 命令。
  - 支持在 Unity 编辑器关闭时，通过 Unity 的 `-batchmode` 命令行静默启动并运行任务的离线回退机制。
