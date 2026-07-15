# AI Bridge System Architecture

AI Bridge splits responsibilities between the **Unity Editor (C# Host)** and the **Python Agent Service**. Bidirectional IPC is handled via a lightweight local HTTP protocol.

```
┌──────────────────────────────────────┐       HTTP Requests (LLM decisions)       ┌────────────────────────┐
│          Unity Editor (C#)           │ <──────────────────────────────────────── │  Python Agent Service  │
│                                      │                                           │                        │
│   ┌──────────────────────────────┐   │           HTTP Session Polling            │   ┌────────────────┐   │
│   │     AIAssistantWindow UI     │ ──┼─────────────────────────────────────────> │   │   service.py   │   │
│   └──────────────┬───────────────┘   │                                           │   └────────┬───────┘   │
│                  │ (HTTP client)     │                                           │            │           │
│   ┌──────────────▼───────────────┐   │                                           │   ┌────────▼───────┐   │
│   │     AgentBridge Server       │   │                                           │   │   agent_core.py│   │
│   │        (HttpListener)        │   │                                           │   │ (ReAct Loop)   │   │
│   └──────────────┬───────────────┘   │                                           │   └────────┬───────┘   │
│                  │ (routing)         │                                           │            │           │
│   ┌──────────────▼───────────────┐   │                                           │   ┌────────▼───────┐   │
│   │       AgentToolRouter        │   │                                           │   │  UnityClient   │   │
│   └──────────────┬───────────────┘   │                                           │   └────────────────┘   │
│                  │                   │                                           └────────────────────────┘
│    ┌─────────────┼─────────────┐     │
│    ▼             ▼             ▼     │
│ SceneQuery  CompileWatcher  Registry │
└──────────────────────────────────────┘
```

---

## 1. Bidirectional HTTP IPC
The C# Editor hosts a lightweight HTTP listener (`AgentBridge.cs`) running in a background thread.
* **Port Allocation**: Computed dynamically using an MD5 hash of the normalized active project path. This guarantees that multiple open Unity projects have unique ports (ranging from 8000 to 9999) and do not collide.
* **Port Discovery**: On startup, Unity saves the assigned port to `AgentController/agent_port.txt`. The Python backend automatically reads this file to locate the active editor session.

---

## 2. Python Agent Orchestration
* **`service.py`**: Manages active sessions, streams agent events/text to the C# UI via HTTP response polling, and saves conversation logs to `ai_chat_history.json`.
* **`agent_core.py` (ReAct Loop)**: Manages prompt formatting, LLM requests (supporting DeepSeek, Gemini, Claude, Ollama), and tool call parsing.
* **`engine_client.py` & `unity_client.py`**: Encapsulates engine-specific calls, HTTP request timeouts, and compile state checks.

---

## 3. Dynamic Compilation Workflow
1. **Tool Invocation**: The Agent decides to write code (`compile_temp_method` or `compile_script`).
2. **File Generation**:
   * For temporary actions, it writes a static method into `AITempCommands.cs` using partial classes.
   * For runtime scripts, it creates full `MonoBehaviour` classes in `Assets/Scripts/AITemp/`.
3. **Compilation Polling**: The Python side uses `check_compile_status` in Unity. If the editor compiles successfully, the tool execution completes.
4. **Error Recovery**: If compilation fails, the agent queries compiler diagnostic logs via `get_compile_errors` and rolls back the generated source to prevent corruption.
