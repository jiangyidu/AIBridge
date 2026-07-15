# 贡献指南 (Contributing to AI Bridge)

我们非常欢迎您为改进 **AI Bridge** 做出贡献！无论是修复 Bug、添加新的 Unity 编辑器指令、还是扩展 Python 后端以支持更多的大模型与三维/CAD 引擎，请阅读并遵循以下指南。

---

## 🏗 代码规范与契约

### C# / Unity 开发规范
- **Unity 版本兼容性**：请保持对旧版 Unity（最低支持到 Unity `2018.4.36f1`）的向下兼容性。避免使用在 Mono/C# 6（.NET 4.x 运行时）中不支持的现代 C# 特性。
- **命名空间**：所有包体内的编辑器 C# 脚本均应归属于 `AIBridge.Agent` 或 `AIBridge.Core` 命名空间下。
- **程序集定义 (`asmdef`)**：请严格分离编辑器逻辑与运行时逻辑：
  - 编辑器（Editor）脚本放入 `AIBridge.Editor.asmdef` 关联的目录。
  - 运行时（Runtime）脚本放入 `AIBridge.Runtime.asmdef` 关联的目录。
- **主线程安全**：Unity 内置的 HTTP 服务（`AgentBridge`）运行在后台非主线程中。所有涉及 Unity 引擎 API 操作（如场景查询、资产搜索、修改物体等）的操作，**必须**使用 `AgentBridge.EnqueueMainThread(action)` 排入主线程队列中执行。

### Python 后端规范
- **Python 版本兼容性**：支持 Python 3.8 或更高版本。
- **代码格式化**：遵循 PEP 8 编码规范。代码需带有清晰的函数说明（Docstring）和注释。
- **文件编码**：所有新建的 Python 源文件请在文件头部添加 `# -*- coding: utf-8 -*-`。
- **依赖范围控制**：尽量避免引入复杂的第三方库。目前 Python 侧仅依赖标准库及 `requests`。

---

## 🛠 核心扩展方向

### 1. 编写自定义静态命令
您可以在 [Assets/Editor/AntigravityTasks.cs](file:///f:/otherProject/AIBridge2026617/Assets/Editor/AntigravityTasks.cs) 或项目的任何 C# 类中编写供 AI Agent 调用的接口：
1. 声明一个公共静态（`public static`）方法。
2. 为方法打上 `[AgentCommand]("功能描述", category: "分类名称")]` 属性标签。
3. 强烈建议方法返回 `string` 类型，以便直接将执行状态和操作日志反馈给大模型上下文。

```csharp
[AgentCommand("查询并整理项目特定的资产状态", category: "Custom")]
public static string QueryResources(string searchPattern)
{
    // C# 逻辑实现
    return "日志结果或操作结果详情";
}
```

### 2. 适配和添加新软件/三维引擎
AI Bridge 在 Python 后端被设计为“引擎无关”架构。如果您希望添加对其他环境（如 AutoCAD、Tekla、Unreal Engine）的支持：
1. 继承 `EngineClient` 基类（在 [AgentController/python_agent/engine_client.py](file:///f:/otherProject/AIBridge2026617/AgentController/python_agent/engine_client.py) 中定义）。
2. 重写（Override）以下钩子方法：
   - `engine_name`：该软件引擎的英文简称/显示名称。
   - `_resolve_agent_url()`：获取对应软件中运行的 HTTP Server 端口。
   - `_tools_schema_fallback_paths()`：定义本地磁盘上无法连接 HTTP 时兜底读取的 API Schema JSON 路径。
   - `_compile_tool_names()`：指明哪些工具调用在触发后需要同步轮询编译器状态。
   - `_normalize_source()`：自定义对大模型生成的源码的清洗规范。
3. 在 [AgentController/python_agent/agent_core.py](file:///f:/otherProject/AIBridge2026617/AgentController/python_agent/agent_core.py) 中的 `create_engine_client` 工厂方法中注册新子类：

```python
elif engine == "autocad":
    from .autocad_client import AutoCADClient
    return AutoCADClient(**kwargs)
```

---

## 🚀 本地测试与提交流程

1. **Fork 并克隆仓库**：克隆项目并在 Unity 中打开。
2. **初始化环境**：在 Unity 中运行 **AIBridge 环境向导**（`AIBridge -> Environment Setup Wizard`）以确保依赖就绪。
3. **测试 CLI 通信**：运行控制台测试，确保能正常拉取 Unity 中的命令：
   ```bash
   python AgentController/unity_agent_controller.py list
   ```
4. **提交 PR**：提交 Pull Request 并详细说明修改细节、验证方式及应用效果。
