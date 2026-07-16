# AI Bridge 2.0 项目操作手册

> 文档基线：AI Bridge `2.0.0`，代码审阅日期 `2026-07-16`。本文描述当前仓库中的纯 C# 实现，不适用于已经删除的 Python、本地代理或端口监听版本。

## 1. 产品定位与使用边界

AI Bridge 是运行在 Unity Editor 内的 AI Agent。它通过 `UnityWebRequest` 直接连接用户选择的远程模型服务或本地 Ollama，并在 Unity 主线程调用一组受控工具来查询场景、创建资源、修改对象、注册命令或生成 C# 源码。

当前实现具有以下边界：

- 插件本体不需要 Python、pip、本地代理、监听端口或额外可执行文件。
- AI 请求会把完成任务所需的会话内容和工具结果发送到用户配置的模型服务；这不是离线功能。
- Ollama 模式需要用户自行安装并运行 Ollama。Ollama 是用户选择的模型服务，不属于插件内置依赖。
- 生成代码的静态检查用于降低风险，不是完整 C# 沙箱。
- 生成代码默认只编译、不自动执行。用户必须显式开启“允许自动执行生成代码”，或在命令面板中人工执行。
- Agent 是 Editor 工作流工具；不要把 API Key、聊天历史或 Agent 状态加入 Player 构建或发布包。

## 2. 目录与运行产物

| 路径 | 用途 | 是否应进入发布包 |
|---|---|---|
| `Assets/AIBridge/Editor` | 窗口、Agent 状态机、LLM 客户端、工具路由、编译事务 | 是 |
| `Assets/AIBridge/Runtime` | `[AgentCommand]`、桥接协议与公共接口 | 是，但应验证 Player 构建影响 |
| `Assets/AIBridge/Plugins/LitJson` | 内置 JSON 解析源码 | 是，并配套第三方声明 |
| `Assets/AIBridge/Samples` | 基础场景和扩展命令示例 | 是 |
| `Assets/AIBridge/Documentation` | 经典 `.unitypackage` 可包含的离线文档 | 是 |
| `Assets/AIBridge/Documentation~` | 旧的 UPM 风格简版开发说明 | 当前不作为经典包的唯一文档来源 |
| `Library/AIBridge` | 会话、编译事务、聊天历史和备份 | 否 |
| `Assets/Editor/AITempCommandsGenerated` | AI 生成的临时命令 partial 文件 | 否 |
| `Assets/Editor/*.cs` | 用户自行编写的 `[AgentCommand]` 扩展（若有） | 否 |
| `Assets/Scripts/AITemp` | AI 生成的 MonoBehaviour 脚本 | 否 |
| `Assets/Materials` | 固定工具生成的材质 | 否 |
| `Assets/AIBridgeSettings.asset` | 当前项目的非敏感配置 | 否，不应随商品包带入其他项目 |

发布时只选择商品根目录 `Assets/AIBridge`。当前开发工程里的 `Assets/Materials`、`Assets/Scenes`、`Assets/Editor`、`Assets/Scripts/AITemp` 和 `Assets/AIBridgeSettings.asset` 都是测试或用户运行产物，不能误打进商店包。

## 3. 安装与首次检查

1. 在受版本控制保护的 Unity 项目中导入 `Assets/AIBridge` 对应的 `.unitypackage`。
2. 等待 Unity 完成首次编译，确认 Console 没有由 AI Bridge 引起的 Error、Warning 或 Missing Script。
3. 打开菜单 `Tools > AIBridge > AI Assistant`。
4. 点击窗口顶部“环境检查”。检查项包括：
   - Unity 版本不低于代码声明的 `2018.4`；
   - `AgentTools.json` 能加载且不是空数组；
   - `Library/AIBridge` 可创建并写入；
   - `AIBridgeSettings.asset` 可读取或创建；
   - 进程内 `AgentBridge` 已初始化。
5. 在提交 Asset Store 前，还必须使用 Unity `2022.3` 或更高版本制作和验证上传包。当前开发工程版本是 `2023.1.17f1c1`。

导入插件本身不会在 `Assets/AIBridge` 之外创建模板、脚本、材质或配置；只有用户明确执行环境检查、固定工具或代码生成操作后，才会按对应功能写入运行产物。

## 4. 模型配置

### 4.1 远程 API

1. 选择“远程 API”。
2. 选择预设服务商，或选择“自定义”。
3. 核对接口地址和模型名称。预设值只是默认配置，不代表服务商长期有效或免费。
4. 输入该服务商签发的 API Key，不要包含 `Bearer ` 前缀、引号或 API 地址。
5. 设置最大步骤数。代码会把值限制在 `1` 到 `80`，默认 `30`。
6. 在发送任务前阅读服务商的 API 条款、数据使用政策和计费规则。

远程模式下，API Key 经过规范化后，以带版本前缀的 AES 格式存入本机 `EditorPrefs`，用于 Domain Reload 后恢复请求。Key 不写入 `Library/AIBridge/agent-state.json`，也不写入 `AIBridgeSettings.asset`。这是一种本机凭据保护措施，不应描述为操作系统级密钥库。

### 4.2 Ollama

1. 用户自行安装并启动 Ollama。
2. 选择“Ollama 本地”。
3. 默认地址为 `http://localhost:11434`，窗口会补全 `/v1/chat/completions`。
4. 填写本地已安装的模型名称。
5. Ollama 模式不要求 API Key，但仍会把任务内容发送给该本地服务进程。

## 5. 标准操作流程

### 5.1 开始任务

1. 提交或暂存当前项目改动，确保可以比较和回退。
2. 打开 AI Assistant，执行“环境检查”。
3. 核对模型、接口、最大步骤数及“允许自动执行生成代码”开关。安全默认值应为关闭。
4. 用明确目标描述任务，注明允许修改的场景、对象和目录。
5. 发送消息后，不要同时启动第二个 Agent 会话。窗口会阻止并发会话。
6. 观察状态栏中的步骤、当前阶段和工具结果。
7. 任务完成后检查：Hierarchy、Inspector、Project 资源、Console、生成源码和聊天记录。

### 5.2 Agent 实际执行顺序

通常一次任务按以下顺序运行：

1. `query_scene` / `query_object` 探查现状；
2. `list_commands` / `list_ai_scripts` 查找可复用能力；
3. 优先使用固定工具完成常见编辑；
4. 固定工具不足时才调用 `compile_temp_method` 或 `compile_script`；
5. Unity 编译并发生 Domain Reload；
6. 宿主恢复事务并验证目标类型或方法已加载；
7. 需要执行生成命令时，再单独调用 `execute_command`；
8. 模型没有继续返回工具调用时，会话完成。

## 6. 工具分类与副作用

| 类别 | 工具 | 主要行为 | 风险/恢复策略 |
|---|---|---|---|
| 只读查询 | `query_scene`、`query_object`、`find_assets`、`list_commands`、`list_ai_scripts`、`get_compile_errors`、`get_console_logs`、`read_command_source` | 读取场景、资源、日志或源码 | 结果会进入会话，并可能发送给模型服务 |
| 固定编辑 | `create_gameobject`、`create_material`、`set_material`、`attach_script` | 创建或修改 Unity 对象/资源 | 主要操作支持 Undo；跨重载提交不确定时不自动重放 |
| 注册命令 | `execute_command` | 反射调用带 `[AgentCommand]` 的公有静态方法 | 只允许已注册方法；用户命令应自行做参数校验、Undo 和权限收敛 |
| 代码生成 | `compile_temp_method`、`compile_script` | 校验、写入 C#、触发编译和 Domain Reload | 使用持久化事务、哈希冲突保护、失败回滚；编译不等于执行 |

### 6.1 手动命令面板

命令面板显示当前 AppDomain 中扫描到的 `[AgentCommand]`。用户可以按分类或名称检索、填写参数并手动调用。调用链为：

`AIAssistantWindow.ExecuteCommand` → `AgentBridge.ExecuteCommandJson` → 查找带 `[AgentCommand]` 的静态方法 → 转换参数 → `MethodInfo.Invoke`。

命令面板不会绕过方法上的 `[AgentCommand]` 要求。生成命令是否允许由 Agent 自动执行，仍受 `AllowGeneratedCodeExecution` 控制。

## 7. 三种典型工作流

### 7.1 只修改场景

适合创建基础物体、材质和挂载已存在的脚本：

1. 查询场景和目标物体；
2. 使用固定工具；
3. 检查 Undo、场景脏标记和资源路径；
4. 保存场景前人工确认结果。

### 7.2 生成 MonoBehaviour

1. 先调用 `list_ai_scripts`，避免重复文件名；
2. `compile_script` 只能写入 `Assets/Scripts/AITemp/<TypeName>.cs`；
3. 源码必须声明与文件同名的 class 或 struct；
4. 编译成功并在新脚本域加载后，再调用 `attach_script`；
5. 已存在且已加载的脚本默认跳过覆盖，只有显式 `forceOverwrite=true` 才重编译；
6. 检查脚本公开参数、注释、Undo/初始化逻辑和 Player 构建兼容性。

### 7.3 生成临时命令

1. 先 `list_commands`，发现相似命令时用 `read_command_source` 阅读现有实现；
2. `compile_temp_method` 接收完整 `public static` 方法；
3. 方法写入 `Assets/Editor/AITempCommandsGenerated/AITempCommands_<Method>.cs`；
4. 编译成功后，方法在 `AIBridge.Agent.AITempCommands` 中加载；
5. 默认不会执行。审查源码后再手动执行，或临时开启生成代码执行开关；
6. 用完后建议关闭开关，并将生成文件作为项目代码单独评审。

## 8. Domain Reload、取消与恢复

- LLM 网络对象不能跨 Domain Reload。状态机会保存同一逻辑决策 ID，并最多重试 `3` 次；网络重试不额外增加 Agent 步数。
- 普通工具在“Prepared 已保存、结果尚未 Committed”的间隙发生重载时，系统无法证明副作用是否已发生，因此返回 `Uncertain`，不会自动重放。
- 代码生成有稳定 `operationId`，可以从持久化事务继续等待、验证或回滚。
- 进入 Play Mode 前，Agent 暂停推进；退出后从持久化阶段继续。
- 点击停止会取消调度和当前网络请求，但已经开始的编译事务仍会完成验证或安全回滚。
- 活跃状态超过 `5` 分钟没有更新时，系统安全停止旧会话，避免重放过期操作。

## 9. 本地状态与清理

| 文件 | 内容 | 敏感性/处理 |
|---|---|---|
| `Library/AIBridge/agent-state.json` | 阶段、消息、工具调用、重试和状态 | 可能包含用户消息、项目路径和工具结果；不要提交 |
| `Library/AIBridge/compile-state.json` | 生成文件、哈希、备份、编译错误和回滚阶段 | 不要在事务活动时删除 |
| `Library/AIBridge/chat-history.json` | 窗口聊天历史和推理内容 | 可能包含项目和业务信息；不要提交 |
| `Library/AIBridge/backups` | 被代码生成事务覆盖前的短期源码备份 | 事务结束后清理；不要打包 |
| `EditorPrefs/AIAss_Api_Key` | 带版本标识的本机 AES 密文 | 不进入项目 Assets；无法解密时重新输入 |

只有在 Agent 已停止、Unity 不在编译且不存在活动回滚时，才可备份后删除 `Library/AIBridge`。状态文件损坏时系统会先尝试 `.bak`，并以低频重试方式保留现场。

## 10. 扩展方式

### 10.1 增加 `[AgentCommand]`

1. 在用户程序集定义公有静态方法；
2. 添加 `[AgentCommand("说明", category: "分类")]`；
3. 参数使用易验证的简单类型；
4. 有副作用的方法支持 Undo、限制可写路径并尽量可重复调用；
5. 等待 Domain Reload 后在命令面板确认已经注册；
6. Agent 调用前必须先通过 `list_commands` 发现该方法。

### 10.2 增加固定工具

需要同步修改四个位置：

1. `BridgeProtocol`：增加稳定工具名；
2. `AgentTools.json`：定义参数 Schema、必填项和真实副作用；
3. `AgentToolRouter.ExecuteTool`：增加路由和主线程实现；
4. 文档与测试：明确幂等性、Undo、数据外发和 Domain Reload 行为。

不要在固定工具内部隐藏网络请求、后台线程、长时间阻塞或项目外文件访问。

## 11. 开发验证

每次修改至少完成：

1. Unity Console 中包自身 `0 Error / 0 Warning / 0 Missing Script`；
2. `dotnet build AIBridge.Editor.csproj --no-restore`；
3. 环境检查通过；
4. 固定工具任务可完成并可 Undo；
5. 生成正确脚本：编译、Domain Reload、目标验证、Agent 继续；
6. 生成错误脚本：记录原始错误、恢复文件、恢复编译、向 Agent 返回失败；
7. Waiting LLM 阶段触发重载：有限重试，无无限请求；
8. 普通工具提交边界触发重载：返回 `Uncertain`，不重复副作用；
9. 关闭自动执行时，生成命令不能被 Agent 自动执行；
10. 新建空白工程导入最终包并重复以上核心流程。

更详细的类关系、状态流与编译事务见 [架构与逻辑流程](ARCHITECTURE_AND_FLOWS_CN.md)，送审规则和 Go/No-Go 门槛见 [Asset Store 上架与发布清单](ASSET_STORE_RELEASE_CN.md)。
