# AI Bridge 常见故障排查指南

本文档列出了在使用过程中常见的环境配置问题、报错信息以及对应的解决方案。

---

## 1. Python 环境检测失败
**问题表现**：一键安装向导 (Setup Wizard) 提示找不到 Python 或 pip。
* **解决方案**：
  1. 确保您的操作系统上安装了 Python 3.8 或更高版本。
  2. 安装 Python 时，必须勾选 **"Add Python to PATH"**（将 Python 添加到系统环境变量，Windows 平台尤为重要）。
  3. 重新打开 Unity 编辑器并运行向导（`AIBridge -> Environment Setup Wizard`）。如果依然报错，您可以在向导的输入框中手动填入您系统上 `python.exe` 的绝对路径。

---

## 2. API Key 问题与存储安全
**问题表现**：在大模型对话中收到未授权错误（Unauthorized）或 API 密钥失效的警告。
* **解决方案**：
  * 点击 `AIBridge -> AI Assistant`，展开 **配置 (Configuration)** 面板，仔细核对您的 API 密钥和 Provider 端点。
  * *安全提示*：API 密钥经 Base64 处理后存储在 `EditorPrefs` 中。如果公开共享您的项目设置，请确保清除或不要泄露个人密钥。

---

## 3. 动态代码编译失败 (Compilation Errors)
**问题表现**：Agent 尝试动态生成代码，但 Unity 控制台疯狂报错，导致 Agent 循环卡死或重复尝试。
* **解决方案**：
  * 打开 Unity **Console** 控制台面板，查看具体的编译错误信息。
  * Python 后端会自动在编译失败时调用 `RollbackPendingGeneratedSource` 进行代码回滚。如果是人为修改代码导致了编译错误，需手动修复错误语法以让 Unity 能够正常重新编译。
  * 检查 `Assets/Editor/AITempCommands.cs` 或 `Assets/Scripts/AITemp/` 下生成的脚本，了解 Agent 写入的具体内容。

---

## 4. 敏感代码拦截警告 (Security Exception)
**问题表现**：Agent 尝试编写代码时，收到安全限制拦截警告，代码被拒绝编译。
* **解决方案**：
  * AI Bridge 拥有安全防护机制，会拦截具有潜在破坏性的代码碎片（例如删除系统文件、启动外部进程、扫描系统目录等）。
  * 如果 LLM 生成的代码中包含 `Process.Start`、`File.Delete` 或未授权的外部命名空间，会被 `CompileWatcher` 自动拦截。请在提示词中约束 AI，让其仅生成常规的 Unity API 操作代码。
