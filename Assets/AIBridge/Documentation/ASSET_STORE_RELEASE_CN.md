# AI Bridge 2.0 Asset Store 上架与发布清单

> 审阅基线：`2026-07-16`。官方 Submission Guidelines 页面标记的最近更新日期为 `2026-05-20`。规则可能变化，正式送审当天必须再次核对官方页面。本文件是项目发布工程文档，不是 Unity 的审核保证或法律意见。

## 1. 官方依据

- [Unity Asset Store Submission Guidelines](https://assetstore.unity.com/publishing/submission-guidelines)
- [Asset package Asset Store publishing workflow](https://docs.unity3d.com/Manual/asset-store-workflow.html)
- [Validate and upload assets to your package](https://docs.unity3d.com/Manual/AssetStoreUpload.html)
- [Submit an asset package for approval](https://docs.unity3d.com/Manual/AssetStoreSubmit.html)
- [Start publishing on the Asset Store](https://assetstore.unity.com/publishing/publish-and-sell-assets)

规则优先级高于本文。若条款编号、最低 Unity 版本、上传工具或 UPM 开放范围发生变化，以送审时的官方页面为准。

## 2. 当前发布结论

**当前结论：No-Go，不能仅凭现有功能可运行就直接送审。**

纯 C# 重构已经消除了旧版 Python、pip、本地代理、监听端口和外部可执行依赖，这是关键进展；但当前仍有数据披露/同意、安全边界、导入副作用、菜单路径、第三方来源确认、商店文案和干净工程验证等 P0 项未关闭。

本次文档整理时，`dotnet build AIBridge.Editor.csproj --no-restore` 已通过，结果为 `0 Warning / 0 Error`；当前 Unity Editor.log 末尾也显示脚本编译和程序集重载完成，未出现新的程序集错误。这只能证明当前开发工程的局部构建状态，不能代替最终 `.unitypackage` 的干净工程导入、Console、Missing Script 和 Validator 证据。

建议冻结发行形态为：

- 主发行：经典 `.unitypackage`；
- 商品根目录：只上传 `Assets/AIBridge`；
- 制包与送审 Unity：至少 `2022.3`，当前开发工程为 `2023.1.17f1c1`；
- UPM：当前不作为默认付费发行承诺。官方 UPM Asset Store 流程仍属于受限/早期访问路径时，不应把 `package.json` 等同于已经具备 UPM 上架资格；
- 对外表述：可以描述为“插件本体纯 C#、无需额外可执行依赖”，但必须同时披露远程模型或 Ollama 服务、API Key、数据发送、费用和代码生成边界。

## 3. 规则映射与当前证据

状态说明：

- `通过`：代码静态证据明确，但送审前仍要在最终包复验；
- `部分`：已有控制，但披露、测试或边界不完整；
- `未通过`：当前存在直接差距；
- `待验证`：必须在干净工程或 Publisher 工具中得到证据。

| 官方条款 | 当前状态 | 当前项目证据 | 关闭条件 |
|---|---|---|---|
| 1.1.a 专业质量 | 部分 | 具有 asmdef、示例、双语 README、状态恢复和回滚设计 | 完成干净工程、文档、演示、错误处理和发布材料验收 |
| 1.1.b setup 后无包引发的错误/警告 | 待验证 | 当前本地 `dotnet build` 为 0 Warning/0 Error，Editor.log 末尾无新增程序集错误；但工作区不是干净导入环境 | 最终包在所有声明版本 `0 Error / 0 Warning / 0 Missing Script`；允许例外必须完整披露 |
| 1.1.c 依赖透明 | 部分 | 无 Python/可执行文件；依赖 Unity 内置能力和 LitJson 源码；功能依赖用户选择的模型服务 | 商店顶部与文档披露服务商账户、API、Ollama、费用、网络和第三方条款 |
| 1.1.g 不安全内容 | 部分/高风险 | 默认禁止自动执行生成代码，有危险片段检查和失败回滚；仍支持生成 C# 与用户命令 | 完成威胁模型、首次使用警告/确认、能力最小化、越权与绕过测试；不得称为沙箱 |
| 1.1.j AI 仅访问必要数据 | 未通过 | 系统 Prompt自动包含项目绝对路径；工具可返回场景、资产路径、日志和源码 | 增加首次发送前的数据清单与明确同意；评估去除/脱敏绝对路径；允许用户控制高敏工具 |
| 1.1.k 外部模型训练须有明确同意 | 未通过 | 插件不训练模型，但当前 UI 没有针对所选服务商数据使用/训练政策的明确同意流程 | 用户在首次连接前选择服务商并确认其条款；文档不能替第三方作“绝不训练”保证 |
| 1.1.l AI 功能不得明显拖慢或干扰 Editor | 待验证 | 网络非阻塞；Agent/编译事务由 Editor update 推进；日志缓冲和状态写盘持续运行 | 空闲、长会话、重载、编译和大日志场景性能测试；无 update 泄漏、刷盘或刷日志 |
| 1.2.a Third-Party Notices | 部分 | 包含 LitJson 源码，已补候选 `Third-Party Notices.txt` | 逐文件确认上游版本/来源/修改，保留完整适用许可文本，并在商店描述声明 |
| 1.2.b 许可兼容 | 待验证 | LitJson 源文件声明作者放弃版权，公开包标为 Unlicense/Public Domain | 发布者完成法律/来源核验；包内不得遗漏上游 COPYING/许可义务 |
| 1.3.a 新提交使用 Unity 2022.3+ | 通过/待复验 | 当前工程 `2023.1.17f1c1` | 用 2022.3 LTS 或更高受支持版本制作和上传最终包，并记录版本 |
| 1.5.a 不含可执行文件及外部可执行依赖 | 通过 | 当前包内静态扫描无 Python、pip、exe、代理或本地监听实现 | 对最终上传目录重复扫描；Ollama 明确是用户选择的外部模型服务，不捆绑下载 |
| 1.5.b API Key 存储方式透明且不进入构建 | 部分 | Key 以版本化 AES 密文存于 EditorPrefs，不写 Agent 状态/Settings asset | 在文档和商店描述准确说明；验证 Player 构建、场景、预制体和导出包中没有 Key |
| 1.5.c API 条款与额外费用置顶披露 | 未通过 | 代码支持多个付费 API，但仓库没有可直接复制到商店顶部的最终披露 | 使用本文第 8 节模板，逐个声称支持的服务商核对条款和费用链接 |
| 1.5.e 分析收集需 opt-in | 通过/待复验 | 当前代码未发现遥测或分析上报 | 最终包静态扫描与网络抓包确认；若未来增加，必须默认关闭且可随时退出 |
| 1.6 AI 生成内容披露 | 待确认 | 产品功能与部分开发/素材可能使用 AI | Publisher Portal 的 AI description 按实际情况如实填写，不使用暗示纯人工的词 |
| 2.1.a 单一根目录 | 通过/待复验 | 商品源文件集中于 `Assets/AIBridge`；导入阶段不再写根外模板，运行产物只在用户明确操作后创建 | 上传只选根目录；干净工程复验导入前后文件清单，并区分商品内容与用户产物 |
| 2.1.c 无重复/无用文件 | 待验证 | 同时存在 `Documentation` 与 `Documentation~` 两套说明，且开发工程有测试生成物 | 冻结 classic/UPM 发行形态；最终包只保留对该形态必要且不重复的内容 |
| 2.1.e 路径短于 150 字符 | 通过/待复验 | 当前 `Assets/AIBridge` 最长已扫描路径远低于 150 | 对最终上传清单重新计算，包括 `.meta` |
| 2.3 综合离线文档 | 部分 | 已新增正常 Unity 目录下的操作、架构和发布文档 | 确认 Publisher 上传后的 `.unitypackage` 实际包含文档；补齐/同步面向商店用户的英文版 |
| 2.5.a 用户命名空间 | 通过 | 业务代码位于 `AIBridge.Agent` / `AIBridge.Core`，LitJson 使用其上游命名空间 | 最终静态扫描无落入全局或 Unity 官方命名空间的用户代码 |
| 2.5.d 源码可读可改 | 通过 | C# 源码可读，未混淆 | 保持一致风格、公开 API 注释与示例 |
| 2.5.g 不反射 Unity Editor 内部 API | 部分 | 对 `[AgentCommand]` 的反射面向用户代码；Import Worker 探测通过反射调用公开 `AssetDatabase` API以兼容旧版 | 确认没有发现/调用 Unity internal API；在最新支持版本测试公开 API路径 |
| 2.5.i 最新支持版本无 obsolete warning | 待验证 | Unity 2018.4 兼容路径使用已过时编译事件并局部禁用 Warning | 在“最新声明支持版本”构建最终包，无 CS0618 或其他包引发 Warning；必要时用版本条件分支 |
| 2.5.1.a Editor 菜单位置 | 通过/待复验 | 当前入口为 `Tools/AIBridge/AI Assistant` | 在最终上传版本和截图中复验菜单路径 |
| 2.5.1.d InitializeOnLoad 有功能目的且不外跳 | 通过/待复验 | 初始化只用于恢复状态、编译观察和命令扫描，无自动外跳或根外文件创建 | 干净工程验证导入后无额外用户文件、网络请求或包引发日志 |
| 3.1.a 描述覆盖依赖、功能和限制 | 未通过 | 当前 README 有部分内容，但尚无最终商店版顶部披露和限制清单 | 完成第 8 节商店文案，确保与实际支持列表、版本和数据流完全一致 |

## 4. P0：送审阻断项

以下项目未全部关闭时，结论保持 No-Go。

### P0-1 发行边界冻结

- [ ] 确认本次是经典 `.unitypackage`，不是 UPM 提交。
- [ ] 最终上传选择且只选择 `Assets/AIBridge`。
- [ ] `Documentation~`、`package.json` 的去留与 classic 发行目的保持一致，避免重复或误导。
- [ ] 开发工程生成的 `Assets/Materials`、`Assets/Editor`、`Assets/Scripts/AITemp`、`Assets/AIBridgeSettings.asset` 不进入包。
- [ ] 包内无 `.exe`、`.dll` 外部运行时、Python、pip、压缩包、另一个 `.unitypackage` 或下载器。

验收：输出最终文件清单、总大小、最长路径和依赖清单；审阅者从包内即可获得所有声称随包提供的内容。

### P0-2 菜单与导入副作用整改

- [x] 菜单移到 `Tools/AIBridge/AI Assistant`。
- [x] 导入包时不在用户项目根目录静默创建 `Assets/Editor/*.cs`。
- [x] 生成目录只在用户明确触发对应功能后创建。
- [ ] 创建失败、只读工程、版本控制锁定和重复导入不产生 Warning/Error。
- [ ] 配置资产的路径与生命周期明确，不和商品源文件混淆。

验收：空白项目导入后，未打开 AI Bridge 前没有额外用户文件、弹窗、外跳、网络请求或包引发的 Console 消息。

### P0-3 数据流、同意与外部服务

- [ ] 首次远程请求前显示：服务商、目标 URL、将发送的数据类别、可能费用、服务商条款/隐私链接。
- [ ] 用户明确确认后才发送；更换服务商或自定义 URL 后重新确认。
- [ ] 评估删除系统 Prompt 中不必要的项目绝对路径，或至少脱敏为项目名。
- [ ] 对源码、Console 日志等高敏工具增加明确控制或逐次确认。
- [ ] 明确说明插件不自建训练流程，但外部服务如何处理数据由其条款决定。
- [ ] 不默认勾选遥测；当前没有遥测时也在文档明确说明。

验收：全新用户不可能在没有看到披露并确认的情况下把项目数据发送给远程服务。

### P0-4 生成代码与命令安全

- [ ] “允许自动执行生成代码”默认关闭，升级和重装后仍关闭。
- [ ] UI 把“编译成功”和“执行成功”分开显示。
- [ ] 首次开启自动执行时给出不可忽略的风险确认。
- [ ] 固定工具和内置命令有明确最小权限、路径限制、Undo 与参数校验。
- [ ] 对危险片段扫描做绕过测试，并在文档中明确“不是沙箱”。
- [ ] 失败回滚、哈希冲突、超时和取消流程均有自动化或可复现测试记录。
- [ ] 审核演示不要求审核人员输入真实付费 Key；准备本地 Mock 或可审查的无密钥流程。

验收：默认配置下模型不能自动执行新生成的 C#；任何高风险操作都需要明确用户动作。

### P0-5 凭据处理

- [ ] 商店描述和文档准确写明 Key 存在本机 EditorPrefs 的版本化 AES 密文中。
- [ ] 不把该机制称为系统钥匙串或绝对安全存储。
- [ ] Key 不出现在 `Assets`、`ProjectSettings`、场景、预制体、日志、状态 JSON、导出包或 Player 构建。
- [ ] 旧密文解密失败时阻止发送并要求重输。
- [ ] 自定义 URL 场景明确提示 Key 将发送到该 URL。

验收：用测试 Key 做全仓库、Library、日志、构建产物和 `.unitypackage` 扫描，均无明文命中。

### P0-6 第三方许可与权利

- [ ] 确认当前 LitJson 源码的准确上游版本、来源 commit/包版本和全部本地修改。
- [ ] `Third-Party Notices.txt` 内容与实际文件一致。
- [ ] 包内保留上游要求的完整许可文本或 COPYING 内容。
- [ ] 商店描述包含第三方声明。
- [ ] 样例场景、图标、字体、截图、生成素材和营销素材均有可分发权利证明。
- [ ] Publisher Portal 的 AI description 与实际 AI 辅助情况一致。

验收：形成“文件/来源/许可/修改/证明”清单，由发布者签字确认。

### P0-7 干净工程质量门

- [ ] Unity 2022.3 LTS 干净工程导入与核心流程通过。
- [ ] 当前主支持版本干净工程导入与核心流程通过。
- [ ] 最新声称支持版本无 deprecated/obsolete Warning。
- [ ] Console：`0 Error / 0 Warning / 0 Missing Script`。
- [ ] Asset Store Validator 无未解决项。
- [ ] 新导出的 `.unitypackage` 再导入另一个全新工程通过。
- [ ] 关闭 Domain Reload / Fast Enter Play Mode 的要求按送审版本规则验证；如果不声明支持 Unity 6.6，则不要误写已支持。

验收：每个版本保留 Editor.log、Validator 报告、测试记录、包哈希与截图。

## 5. P1：提交资料与专业化

- [ ] 正常 `Documentation` 目录中包含离线安装、配置、数据流、API 成本、架构、扩展和排障文档。
- [ ] 中文与英文 README、架构、限制和商店文案相互一致。
- [ ] Sample 场景没有 Missing Script，能演示固定工具与自定义命令，但不需要真实 Key 才能理解产品。
- [ ] Changelog、版本号、文件头和 Publisher Portal 版本一致。
- [ ] `package.json` 名称、Unity 版本和发行定位一致；如果只发 classic 包，避免把它描述为已获准的 UPM 商品。
- [ ] 检查 `AIBridge.Runtime` 是否确有必要进入 Player；移除不需要的 LitJson Runtime 引用或清晰说明构建影响。
- [ ] 提供支持邮箱、维护网站、隐私/数据说明、服务条款和更新政策。
- [ ] 商店截图真实反映当前 UI，不展示已经删除的 Python Setup Wizard 或端口服务。

## 6. 干净工程测试矩阵

| 测试 | 预期结果 | 证据 |
|---|---|---|
| 只导入最终包 | 无网络、无外跳、无根外文件、0 Error/Warning | Editor.log、Assets 前后快照 |
| 打开 AI Assistant | 窗口可用，配置默认安全 | 截图、Console |
| 环境检查 | 工具定义、状态目录、命令宿主均通过 | 窗口结果 |
| 无 Key 远程发送 | 明确阻止，不发请求 | 网络日志、UI |
| 首次远程发送 | 先披露并取得同意 | 录屏/截图 |
| 远程 401/403 | 立即停止，不重试 | 历史和请求计数 |
| 远程 429/5xx | 有限退避，最多安全尝试次数 | 历史和请求计数 |
| Ollama 未运行 | 可理解错误，不刷日志 | Console、UI |
| 固定场景工具 | 操作成功、支持 Undo | 场景前后快照 |
| 普通工具提交边界重载 | 返回 `Uncertain`，无重复对象/资源 | 状态文件、场景 |
| 正确脚本生成 | 编译、重载、目标验证、继续会话 | compile-state、Console |
| 错误脚本生成 | 保存原始错误、恢复、恢复编译 | 备份和状态 |
| 编译中人工改文件 | `FailedConflict`，不覆盖人工修改 | 文件哈希、状态 |
| 自动执行关闭 | 编译可成功，执行被阻止 | 工具结果 |
| 取消会话 | 网络/调度停止，活动回滚继续完成 | 状态与日志 |
| 删除/损坏状态主文件 | 尝试 `.bak`，不高频刷屏 | Console、恢复结果 |
| Player 构建 | 无 Key/历史；Runtime 影响符合声明 | 构建报告、内容扫描 |
| 重新导出再导入 | 与源工程表现一致 | 包哈希、第二工程记录 |

## 7. 发布执行流程

### 阶段 A：版本冻结

- [ ] 冻结版本号、Unity 版本、支持平台、支持的服务商和功能边界。
- [ ] 关闭所有 P0/P1 项并生成 Release Candidate。
- [ ] 清理测试生成物，只保留 `Assets/AIBridge` 商品根目录。
- [ ] 记录 Git commit、包文件哈希、文件清单和测试矩阵。

### 阶段 B：Publisher Portal 草稿

- [ ] 建立或更新 Publisher Profile。
- [ ] 创建 package draft。
- [ ] 填写分类、版本、价格、支持版本、渲染管线和依赖。
- [ ] 填写描述、关键词、技术细节、AI description、第三方声明和支持链接。
- [ ] 把外部 API、额外费用、数据流和 Key 存储披露放在描述顶部。

### 阶段 C：Validator 与上传

官方 classic asset package 流程为：

1. 在包含最终商品根目录的 Unity 工程中安装 Asset Store Publishing Tools；
2. 打开对应的 Validator/Package Upload 工具；
3. 选择 Publisher Portal 中的 draft；
4. 只选择一个根目录 `Assets/AIBridge`；
5. 运行 Validator/Scan；
6. 关闭所有错误，审查每个 Warning；
7. 上传包；
8. 把上传后生成的 `.unitypackage` 导入全新 Unity 项目再次测试。

Unity 不同时期的菜单名称可能是 `Asset Store Tools > Package Upload`，或 Publishing Tools 下的 `Window > Tools > Asset Store > Validator/Uploader`。以送审时安装的官方工具界面为准，不要凭旧截图操作。

### 阶段 D：提交审核

- [ ] 在 Portal 预览商店页面和 Public link。
- [ ] 填完 Package Detail、Metadata & Artwork、依赖和兼容性。
- [ ] 声明拥有全部内容权利。
- [ ] 给审核员提供最短可复现步骤、无需真实密钥的验证方式和已知限制。
- [ ] 决定是否 Auto publish。
- [ ] 提交后保存版本、日期、审核备注和包哈希。

## 8. 商店描述顶部披露模板

正式发布前必须按实际服务商和法律审查结果替换占位符，不能原样提交。

```text
External AI service and cost disclosure

AI Bridge is a Unity Editor tool. The plugin itself is implemented in C# and does not bundle Python, a proxy process, a listening server, or an executable dependency.

To use AI features, customers must either:
1) provide credentials for a supported third-party model API, which may require a separate account and may charge usage fees under that provider's terms; or
2) run their own Ollama service and model.

Data sent to the selected service can include the user's prompts, Unity version, project identifier/path information, conversation history, and tool results such as scene object/component names, asset paths, Console/compile errors, or source code explicitly read for the task. AI Bridge does not operate its own proxy or analytics service. Data retention or model-training practices are governed by the selected provider's terms and the user's agreement with that provider.

API keys are stored locally in Unity EditorPrefs as versioned AES-encrypted text so sessions can resume after Domain Reload. Keys are not stored in AI Bridge project settings, scenes, prefabs, chat/state JSON, or Player builds. This storage is not an operating-system credential vault.

AI Bridge can generate C# source files. Generated code is statically screened and compiled through a rollback transaction, but this is not a security sandbox. Automatic execution of generated code is disabled by default and should only be enabled after source review and with version control in place.

See the included documentation for setup, supported providers, data flow, limitations, safety controls, and troubleshooting.
```

## 9. 审核员备注模板

```text
Package root: Assets/AIBridge
Unity version used to upload: <exact version>
Minimum tested version: <exact version>
Latest tested version: <exact version>

Quick verification:
1. Import the package into a clean project.
2. Open Tools/AIBridge/AI Assistant.
3. Run Environment Check.
4. Use the included no-secret test/mock flow to inspect scene tools and safety defaults.
5. Confirm generated-code execution is disabled by default.

External services:
- No executable, Python runtime, proxy, listener, installer, telemetry, or publisher-hosted relay is bundled.
- Real model requests require the reviewer's/user's selected API service or local Ollama.

Data and credentials:
- See Documentation/PROJECT_OPERATIONS_CN.md and Documentation/ARCHITECTURE_AND_FLOWS_CN.md.
- API keys are not included in the package.

Known limitations/workarounds:
<list only confirmed items, or write None>
```

## 10. 最终 Go/No-Go 记录

发布负责人在送审前填写：

| 门槛 | 结果 | 证据位置 | 负责人/日期 |
|---|---|---|---|
| 发行边界和上传清单冻结 |  |  |  |
| 数据披露与用户同意完成 |  |  |  |
| 生成代码安全评审完成 |  |  |  |
| API Key 泄漏扫描为零 |  |  |  |
| 第三方许可与权利确认 |  |  |  |
| 2022.3 LTS 干净工程通过 |  |  |  |
| 最新支持版本通过 |  |  |  |
| 0 Error / 0 Warning / 0 Missing Script |  |  |  |
| Validator 全部关闭 |  |  |  |
| 上传后 `.unitypackage` 重导入通过 |  |  |  |
| 商店描述/AI description/第三方声明完成 |  |  |  |
| 审核员无密钥验证流程可执行 |  |  |  |

只有全部标为通过且证据可追溯时，结论才能从 No-Go 改为 Go。
