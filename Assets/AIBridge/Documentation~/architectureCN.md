# 纯 C# 架构与 Domain Reload 恢复

## 1. 为什么旧式内存主循环会失效

Unity 导入或修改 `.cs` 后会编译程序集并重建 AppDomain。静态字段、编辑器窗口实例、`Task`、线程回调和正在执行的 `UnityWebRequest` 对象都会丢失。因此“一个异步方法从请求 LLM 一直 await 到任务结束”的生命周期无法覆盖代码生成流程。

AI Bridge 2.0 把 Agent 改为磁盘上的有限状态机：

```text
RequestingLlm -> WaitingLlm -> ExecutingTools
                                  |
                                  +-> ExecutingPreparedTool
                                  |
                                  +-> StartingCompile -> WaitingCompile
                                                             |
                                     新脚本域恢复并验证 <-----+
```

每次 `EditorApplication.update` 只读取当前阶段并推进一步。关键意图在产生副作用前写入 `Library/AIBridge`，`[InitializeOnLoad]` 在新脚本域重新注册回调并恢复状态。

## 2. LLM 请求恢复

网络请求对象不能跨重载保存，但请求意图、消息、逻辑决策 ID 和尝试次数可以保存。若在 `WaitingLlm` 恢复时没有活动请求对象，系统对同一决策做指数退避的有限重试，最多 3 次。重试不增加 Agent 逻辑步数。

这仍可能让模型端处理两次同一请求，因此工具执行必须独立做幂等与提交保护，不能假设 LLM 请求“恰好一次”。

## 3. 普通工具的提交保护

调用普通工具前先保存 `pendingExecution=Prepared`。如果重载发生在工具结果持久化之前，宿主无法可靠判断副作用是否已经发生。此时不会自动重放，而是提交 `Uncertain` 结果，让模型先通过查询工具检查场景或资产，再决定补偿动作。

这是“最多一次自动执行”策略：宁可漏做后探查，也不盲目重复有副作用操作。

## 4. 生成源码事务

编译工具带稳定 `operationId`，允许在真正写文件前安全续跑。流程如下：

1. 校验目标路径必须位于 `Assets`，校验源码危险片段、标识符、目标类型与括号。
2. 读取原文件哈希；若存在原文件，把备份写入 `Library/AIBridge/backups`。
3. 先保存 `Prepared` 事务，再使用同目录安全替换写入无 BOM UTF-8 源码。
4. 保存 `AwaitingCompilation` 后调用 `AssetDatabase.Refresh`。
5. `CompileWatcher` 在 `compilationStarted` 清空本周期错误，并在所有 `assemblyCompilationFinished` 中累计错误。
6. `compilationFinished` 只负责保存结果，不立即执行生成代码。
7. 编译成功后等待脚本域标识变化，并验证目标类型/方法已加载。
8. 编译失败则保存原始错误，确认生成文件未被第三方修改后恢复备份或删除新文件。
9. 等待恢复编译和第二次 Domain Reload，再向 Agent 返回“失败但已恢复”。

任何哈希冲突都会停止自动覆盖，避免把用户在编译期间的修改抹掉。

## 5. 取消、Play Mode 与超时

- 用户取消会终止 Agent 调度和网络请求，但已经开始的编译事务仍会完成验证或安全回滚。
- 进入 Play Mode 时 Agent 暂停推进，退出后从持久化阶段继续。
- LLM 重试、Agent 步数和编译等待都有上限。Unity 正在编译时不会同时写入回滚源码。

## 6. 安全模型

固定工具优先，生成代码最后使用。生成源码禁止外部进程、网络、原生调用、反射加载、原始文件系统、编辑器初始化钩子和持久事件注册。生成代码自动执行默认关闭。

静态扫描无法证明任意 C# 安全，因此本插件不把它描述为沙箱。高风险项目应在隔离分支使用，并审查用户自定义命令。
