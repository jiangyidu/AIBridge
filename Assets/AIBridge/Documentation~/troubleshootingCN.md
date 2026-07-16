# 故障排查

## Unity 编译后 Agent 没有继续

1. 查看 Console 是否存在插件本身或项目已有的编译错误。
2. 查看 `Library/AIBridge/agent-state.json` 的 `phase` 和 `status`。
3. 查看 `Library/AIBridge/compile-state.json` 的 `phase`、`detail`、`originalErrors`。
4. 若阶段为 `FailedConflict`，说明生成文件在事务期间被其他操作修改；请人工比较文件和 `Library/AIBridge/backups`，不要强制覆盖。
5. 若 Unity 仍显示正在编译，先解决编辑器的编译卡死。系统不会在编译过程中修改源码回滚。

若 Console 出现 `Can't assign value '0' (type System.Int32) to type System.Int64`，说明旧版本在 Domain Reload 后无法反序列化 Agent 状态。更新插件后，状态机会兼容读取这些小整数并继续处理已完成的编译事务，无需删除 `Library/AIBridge`。

若状态文件确实损坏，AI Assistant 会显示“状态恢复失败”，保留原文件并每 5 秒低频重试。此时不要启动新会话或删除活动事务；先备份 `Library/AIBridge`，再检查 `agent-state.json` 与 `.bak` 的内容。

## 生成脚本编译失败

事务会自动保存错误、恢复原文件并再次编译。Agent 只有在恢复编译完成后才收到最终失败结果。不要在恢复过程中手动删除 `compile-state.json`。

如果阶段为 `FailedRollback`，说明原工程本身仍有错误、备份不可读或恢复编译超时。先修复 Console 错误，再人工确认生成文件和备份。

## LLM 请求在脚本重载时中断

这是预期现象。网络对象不能跨 AppDomain 保存，系统会按同一逻辑决策最多重试 3 次。达到上限后会停止会话，避免无限请求。

## HTTP 401 / 403：API 认证失败

这不是可恢复的网络错误，Agent 会立即停止且不会重复请求。展开 AI Assistant 的“配置”，确认服务商和接口地址匹配，然后删除旧值并重新粘贴该服务商签发的有效 API Key。不要填写 API 地址、`Bearer ` 前缀、引号或其他服务商的 Key。

API Key 会在会话启动前以带版本标识的 AES 格式写入 EditorPrefs，确保 Domain Reload 后仍能恢复；无法安全解密的旧密文会被阻止发送并要求重新输入。Key 不会写入 `Library/AIBridge` 的 Agent 状态文件。

HTTP 402、无效参数或无效模型同样不会盲目重试；超时、429 和 5xx 才会进行有限退避重试。

## 工具返回 Uncertain

普通工具在执行提交边界遇到重载，系统无法证明它是否已经产生副作用，因此不会自动重放。让 Agent 重新调用 `query_scene`、`query_object` 或 `find_assets` 检查现状。

## 生成代码被安全检查拒绝

优先改用固定工具。确实需要代码时，移除进程、网络、原生调用、反射加载、文件系统、初始化回调、静态构造函数和编辑器永久事件。安全检查不能通过配置绕过。

## 无法自动执行生成命令

这是默认安全策略。编译成功不代表代码已经执行。若已审查源码，可在 AI Assistant 配置中显式开启“执行生成代码”，或从命令面板手动执行。使用后建议重新关闭开关。

## 清理状态

仅在 Agent 已停止且没有编译/回滚时，才可备份后删除 `Library/AIBridge`。不要在活动编译事务期间清理该目录。
