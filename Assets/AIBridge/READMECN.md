# AI Bridge 2.0

AI Bridge 是一个兼容 Unity 2018.4+ 的 Editor-only 纯 C# 智能体插件。它通过 `UnityWebRequest` 直接访问远程 OpenAI-compatible、Claude API 或本地 Ollama，并在 Unity 主线程内调用工具。

本版本不需要 Python、pip、本地代理服务、监听端口或额外可执行文件，适合通过经典 `.unitypackage` 分发。

## 核心设计

- `PureCSharpAgent` 由 `EditorApplication.update` 驱动，每次只推进一个持久化阶段，不把主循环寄托在 `Task`、后台线程或窗口实例上。
- 会话状态写入 `Library/AIBridge/agent-state.json`。API Key 不写入状态文件。
- LLM 请求若被 Domain Reload 中断，会使用同一决策 ID 最多重试 3 次；网络重试不额外消耗 Agent 步数。
- 普通工具在“已准备、结果未提交”期间发生重载时不会自动重放，而是向模型返回 `Uncertain`，避免重复创建、重复写入等副作用。
- 生成源码采用独立文件和两阶段编译事务：先保存事务与备份，再原子写入 Assets，最后触发刷新。
- 编译错误按完整编译周期累计。失败后恢复原文件，再等待恢复编译与第二次 Domain Reload 完成。
- 成功编译后必须检测到新的脚本域标识，并验证目标类型/方法已加载，才把成功结果交回 Agent。
- 编译与执行严格分离。AI 生成代码默认禁止自动执行，可在配置中显式开启。

## 安装

1. 在 Unity 2018.4 或更高版本中导入 `.unitypackage`，或把 `Assets/AIBridge` 复制到项目。
2. 等待 Unity 完成编译。
3. 打开 `Tools > AIBridge > AI Assistant`，配置模型、接口地址和 API Key。
4. 首次执行任务前可在助手窗口点击“环境检查”，验证工具定义和 `Library/AIBridge` 写入权限。

无需安装任何第三方运行时。Ollama 模式仍需要用户自己运行 Ollama，因为它本身就是所选的模型服务，而不是 AI Bridge 的内部依赖。

## 使用

输入任务后，Agent 会优先探查场景，再使用固定工具。只有固定工具无法满足需求时，才会请求 `compile_temp_method` 或 `compile_script`。

常用工具包括：

- `query_scene` / `query_object`：只读探查。
- `create_gameobject` / `create_material` / `set_material`：可撤销的常见编辑操作。
- `compile_temp_method`：为 `AITempCommands` 生成独立 partial 文件。
- `compile_script`：在 `Assets/Scripts/AITemp` 生成 MonoBehaviour 源码。
- `execute_command`：执行已注册 `[AgentCommand]`；生成命令受“执行生成代码”开关保护。

## 安全边界

系统会拒绝生成源码中的进程启动、网络访问、原生调用、程序集动态加载、原始文件操作、脚本加载回调、编辑器永久事件和危险资产移动/删除等片段，也会校验标识符、目标类型和大括号。

这些检查是风险收敛措施，不是完整的 C# 安全沙箱。启用“执行生成代码”前应查看生成文件，并使用版本控制保护项目。用户自己注册的 `[AgentCommand]` 也应保持最小权限、支持 Undo、可重复调用并验证参数。

## 持久化文件

以下运行数据位于 `Library/AIBridge`，不应提交或打包：

- `agent-state.json`：Agent 阶段、消息、待处理工具和重试状态。
- `compile-state.json`：源码哈希、备份路径、编译周期和回滚状态。
- `chat-history.json`：窗口聊天历史。
- `backups/`：编译事务的短期源码备份。

## 验证

在修改插件后至少执行：

1. Unity Console 无 C# 编译错误。
2. `dotnet build AIBridge.Editor.csproj --no-restore`（该文件由当前 Unity 生成时）。
3. 测试正常生成脚本：编译、Domain Reload、目标验证、Agent 继续。
4. 测试故意生成语法错误：记录原始错误、恢复源码、恢复编译、Agent 收到失败。
5. 在等待 LLM 和执行普通工具的提交边界分别触发脚本重载，确认有限重试和不重放策略。

## 文档导航

- [项目操作手册](Documentation/PROJECT_OPERATIONS_CN.md)：安装、配置、工具使用、生成代码、状态恢复、扩展和验证。
- [架构与逻辑流程](Documentation/ARCHITECTURE_AND_FLOWS_CN.md)：组件职责、会话状态机、LLM/工具链路、编译事务、Domain Reload 和数据流。
- [Asset Store 上架与发布清单](Documentation/ASSET_STORE_RELEASE_CN.md)：官方规则映射、当前差距、P0/P1 清单、测试矩阵和提交步骤。
- [API 与扩展简要指南](Documentation~/api_guideCN.md) 与 [故障排查](Documentation~/troubleshootingCN.md)：面向开发者的快速参考。

经典 `.unitypackage` 应把正常 `Documentation` 目录作为可离线读取的正式文档；`Documentation~` 中保留的是现有 UPM 风格简版参考，正式制包时需根据最终发行形态检查是否重复或遗漏。
