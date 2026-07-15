# -*- coding: utf-8 -*-
"""Unity-specific Agent prompt context.

Contains Unity terminology, object model concepts, compilation rules, and
code generation guidelines that only apply when the target engine is Unity.

2026-06-16 : Extracted from prompts.py for cross-engine portability.
"""

UNITY_ENGINE_NAME = "Unity Editor"

UNITY_CONTEXT = """Unity 引擎特定规则：
1. 场景查询：调用 query_scene 获取 GameObject 层级结构，调用 query_object 获取 Transform/Component 详情。
2. 基础场景操作（高效率，优先使用）：
   - 调用 create_gameobject 创建基础物体（Cube/Sphere/Cylinder/Plane/Empty）并指定位置/旋转/缩放和父节点。
   - 调用 create_material 在 Assets/Materials/ 下创建指定 Shader 和颜色（支持Hex/RGBA）的材质。
   - 调用 set_material 将材质应用到场景中的 GameObject 的 Renderer 上。
   - 调用 attach_script 将已编译加载的 MonoBehaviour 脚本挂载到场景 GameObject 上。
   - 绝大多数创建、材质设置、挂载任务，应优先通过上述 4 个基础工具组装完成，禁止盲目使用 compile_temp_method 编译代码。
3. 已有脚本查询：调用 list_ai_scripts 确认 Assets/Scripts/AITemp/ 下已生成的 MonoBehaviour 脚本。compiled=true 才代表脚本可直接挂载。
4. 编译后执行：compile_temp_method 编译完成后，再调用 execute_command 执行目标方法；compile_script 编译完成后，再调用 attach_script 将其挂载。
5. 动态代码安全（Unity 特有）：禁止在生成代码中使用 Application.Quit、EditorApplication.Exit、无限 EditorApplication.update 注册。
6. 生成 C# 代码时，code 字段必须是完整 C# 源码文本；源码内部字符串按 C# 正常写法表达，例如 GameObject.Find("TriangleCubesParent")，不要额外写成 GameObject.Find(\\"TriangleCubesParent\\")。声明临时方法时，必须在方法声明上方显式添加 `[AgentCommand("简明准确的方法描述。必须简明准确的交代方法作用、对场景的影响、参数限制条件、能做和不能做的能力边界以及任何副作用。", category: "Temp")]` 特性标注。
   - 【高可复用设计原则】：生成 MonoBehaviour 脚本（用 compile_script）时，必须遵循可配置设计，以便后续 AI 能够高准确度、高效率地复用：
     ① 严禁写死具体数值。关键参数（如速度、尺寸半径、物体数量、坐标轴向等）必须使用 `public` 变量（或 `[SerializeField]` 字段）公开，并给定合理的默认值。这能让未来的 AI 在复用时直接通过组件赋值或反射调整，无需重新编译文件。
     ② 类声明上方必须附带详细 XML 注释 `/// <summary>`，清晰简明地阐明脚本的功能、适用的世界坐标系平面（例如：XZ 平面或 XY 平面）及逻辑适用边界。
     ③ 尽量将核心初始化逻辑从 `Start()` 中剥离，封装到 `public` 的初始化方法中（如 `public void Initialize()`），以便 Editor 静态预览工具在非 Play 模式下直接调用。
"""

UNITY_ENV_TEMPLATE = "当前 Unity 版本: {unity_version}"
UNITY_PROJECT_TEMPLATE = "当前 Unity 项目路径: {project_path}"
