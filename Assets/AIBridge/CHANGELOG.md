# Changelog

All notable changes to the **AI Bridge** project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2026-06-18

This is the initial release of the **AI Bridge** framework, establishing a solid foundational bridge between local/remote LLM Agents and the Unity Editor.

### Added
- **Core Architecture & Communications**:
  - Implemented Unity HTTP server (`AgentBridge`) using `HttpListener` running in a background thread with safe lifecycle callbacks (`InitializeOnLoad`).
  - Added deterministic hashed port calculation based on the project path MD5 to prevent port collision when running multiple projects.
  - Implemented `AgentController/agent_port.txt` file generation to allow local Python tools to discover active Unity instances automatically.
- **Python Agent Orchestration (`python_agent`)**:
  - Created a local Python-side HTTP service (`service.py`) to manage asynchronous agent sessions, stream step-by-step events, and maintain persistent JSON history logs (`ai_chat_history.json`).
  - Added `AgentRunner` (`agent_core.py`) with a full ReAct loop supporting parallel tool call parsing, execution, and cancel tokens.
  - Added an engine-agnostic `EngineClient` base class to encapsulate core HTTP routing, timeout configurations, and retries.
  - Added `UnityClient` extending `EngineClient` with Unity-specific paths, URL resolutions, and script compilation steps.
- **Dynamic C# Compilation**:
  - Integrated `compile_temp_method` tool allowing models to inject custom C# static methods decorated with `[AgentCommand]` into `AITempCommands.cs` on the fly.
  - Integrated `compile_script` tool allowing models to write entire custom `MonoBehaviour` scripts to the asset folder and dynamically compile them.
  - Added a compiler polling workflow utilizing the `check_compile_status` endpoint and diagnostic helper `get_compile_errors`.
- **Unity Editor UI & Wizards**:
  - Added `AIAssistantWindow.cs`, a custom Editor window featuring a responsive chat pane, model provider dropdowns (Ollama, DeepSeek, Claude, Gemini, etc.), custom system prompts, and a manual command execution pane.
  - Added support for displaying reasoning tokens (`reasoning_content`) for modern reasoning models.
  - Added `AIBridgeSetupWizard.cs` environment assistant to check for Python environments and dependencies, providing one-click pip installation for the `requests` package.
- **CLI Utilities**:
  - Added `unity_agent_controller.py` providing command-line arguments to list, wait for compilation, and run Unity commands.
  - Added offline fallback routing using Unity `-batchmode` CLI when the editor is closed.
