# AI Bridge

`AI Bridge` (UPM package: `com.LegendMars.aibridge`) is a powerful, lightweight, and extensible Unity Editor AI Agent bridge framework. It connects local LLMs (like Ollama) and cloud LLMs (like DeepSeek, Gemini, Claude, and others) to execute C# commands and run intelligent ReAct agents directly inside the Unity Editor.

Designed with an engine-agnostic core, the framework is extensible to other CAD/3D application environments (e.g., AutoCAD, Tekla, Unreal Engine) in the future.

---

## 🏗 System Architecture

AI Bridge splits the responsibilities between the **Unity Editor (C#)** host and the **Python Agent Service**. They communicate bidirectionally using a lightweight HTTP protocol on a dynamically allocated port.

### Architectural Workflow

```mermaid
graph TD
    subgraph Unity Editor (C# Side)
        Window[AIAssistantWindow UI]
        BridgeClient[PythonAgentClient]
        Server[AgentBridge HTTP Server]
        Router[AgentToolRouter]
        Registry[AgentCommandRegistry]
        Compiler[CompileWatcher / UnityEngineBridge]
    end

    subgraph Backend Service (Python Side)
        Service[run_agent_service.py]
        Runner[AgentRunner Session]
        Engine[UnityClient / EngineClient]
    end

    subgraph LLM Providers
        LLM[DeepSeek / Gemini / Claude / Ollama]
    end

    %% User Interaction
    Window -- 1. Starts Session / Sends Chat --> Service
    Service -- 2. Spawns --> Runner
    
    %% Agent Loop
    Runner -- 3. Request LLM Decision --> LLM
    LLM -- 4. Tool Call / Text --> Runner
    
    %% Engine Execution
    Runner -- 5. Execute Tool Request --> Engine
    Engine -- 6. HTTP POST /agent/tool --> Server
    Server -- 7. Route & Execute --> Router
    Router -- 8. Read Scene / Run Command --> Registry
    Router -- 9. Compile Code / Attach Scripts --> Compiler
    
    %% Response Cycle
    Server -- 10. HTTP Response (Tool Result) --> Engine
    Engine -- 11. Feed Back to Context --> Runner
    
    %% UI Polling
    Window -- 12. Poll Session Events --> Service
    Service -- 13. UI Events / Stream History --> Window
```

---

## 🌟 Key Features

1. **Engine-Agnostic Core (`EngineClient`)**
   - The Python backend separates common protocol logic (HTTP communication, connection retries, event logging, dynamic compilation polling) from engine-specific details. Subclassing `EngineClient` allows porting the framework to AutoCAD, Tekla, Unreal Engine, and more.

2. **Bidirectional HTTP IPC & Hashed Port Discovery**
   - Unity hosts a local HTTP server (`AgentBridge`). The server port is dynamically computed using an MD5 hash of the normalized project path to prevent port conflicts when running multiple Unity projects simultaneously.
   - The active port is saved in `AgentController/agent_port.txt` for the Python backend to automatically detect.

3. **Dynamic Code Compilation & Execution**
   - **Temporary Methods**: The Agent can generate C# static methods at runtime and inject them into a partial class (`AITempCommands.cs`).
   - **MonoBehaviours**: The Agent can write entire scripts to the `Assets/Scripts/AITemp/` directory and compile them synchronously.
   - **Compilation Polling**: The backend polls the compilation state via `check_compile_status` and handles error reporting gracefully with `get_compile_errors`.

4. **Rich Editor Chat Interface (`AIAssistantWindow`)**
   - A fully featured Unity Editor window supporting remote LLM APIs and Ollama.
   - Displays reasoning content (`reasoning_content`) for model architectures (e.g. DeepSeek R1).
   - Features manual execution panel to search and run any C# method decorated with `[AgentCommand]`.

5. **Integrated Setup Wizard (`AIBridgeSetupWizard`)**
   - Detects the Python installation path, checks for pip, and installs the required `requests` package automatically.

6. **Dual Mode Execution**
   - **Interactive Mode**: Directly runs through the Unity Editor GUI window.
   - **Batchmode CLI Mode**: Runs headless automation via `unity_agent_controller.py` using Unity's `-batchmode` CLI.

---

## 🚀 Getting Started

### Prerequisites
- **Unity**: Version 2018.4.36f1 or higher.
- **Python**: Version 3.8 or higher.

### Step 1: Install & Set Up Python Environment
1. Open the project in Unity.
2. In the Unity Editor top menu, click **`AIBridge -> Environment Setup Wizard`**.
3. Follow the wizard steps to automatically detect Python and install the required `requests` library.

### Step 2: Open AI Assistant Window
1. In the Unity top menu, click **`AIBridge -> AI Assistant`**.
2. Expand the **配置 (Configuration)** panel.
3. Configure your API endpoint:
   - **Remote API**: Select a provider (DeepSeek, Gemini, Claude, Tongyi, Kimi, Zhipu GLM, etc.) and paste your **API Key**.
   - **Ollama**: Specify the local Ollama URL (e.g., `http://localhost:11434`) and model name.
4. Close or keep the Configuration panel open. Type your query in the input area and hit **发送 (Send)**!

---

## 🛠 Developer Guide

### Exposing C# Commands to AI
You can easily expose your custom C# editor tasks to the AI Agent. Simply write a public static method and mark it with the `[AgentCommand]` attribute:

```csharp
using UnityEngine;
using AIBridge.Agent;

namespace AIBridge.Agent
{
    public static class CustomEditorTasks
    {
        [AgentCommand("Generate a procedural grid of game objects", category: "Custom")]
        public static string GenerateGrid(string prefabName, int rows, int cols, float spacing)
        {
            // Load prefab, instantiate in loop
            // Return progress message to the LLM agent
            return $"Successfully generated a {rows}x{cols} grid of '{prefabName}'.";
        }
    }
}
```

The system automatically scans all types on reload, making the new command available in the command registry (`AgentCommandRegistry`), which is then served to the Python agent via `/agent/commands`.

### Python CLI Control
To run commands directly from the command line (either via HTTP IPC when Unity is running, or automatically launching Unity in batchmode if it is closed):

```bash
# List all registered editor commands
python AgentController/unity_agent_controller.py list

# Run a specific command
python AgentController/unity_agent_controller.py run --class "AntigravityTasks" --method "CreateCustomCube" --args "MyProceduralCube"
```

---

## 📁 Project Structure

```text
AIBridge/
├── Assets/
│   ├── Editor/
│   │   ├── AntigravityTasks.cs       # Custom user tasks container
│   │   └── AITempCommands.cs         # AI temporary commands partial class
│   └── AIBridge/                     # Core UPM package folder
│       ├── package.json              # Package manifest
│       ├── LICENSE.md                # License details
│       ├── Editor/                   # Unity Editor scripts
│       │   ├── AIAssistantWindow.cs  # Main Editor GUI
│       │   ├── AgentBridge.cs        # Hashed port HTTP listener
│       │   ├── AgentToolRouter.cs    # Tool Router
│       │   ├── SceneObserver.cs      # Scene scanner
│       │   └── ...                   
│       ├── Runtime/                  # Runtime utilities
│       │   ├── AgentCommandAttribute.cs
│       │   └── ...
│       └── Samples/                 # Setup and basic usage samples
└── AgentController/                  # Python backend
    ├── run_agent_service.py          # Launcher script for HTTP service
    ├── unity_agent_controller.py     # CLI execution entry point
    └── python_agent/                 # Core Python source code
        ├── agent_core.py             # ReAct loop orchestration
        ├── engine_client.py          # Abstract engine communications
        ├── unity_client.py           # Unity specific hooks
        ├── service.py                # Session controller API
        └── providers.py              # LLM client abstractions
```

---

## 📄 License
This project is licensed under the MIT License. See the [LICENSE.md](file:///f:/otherProject/AIBridge2026617/Assets/AIBridge/LICENSE.md) file for details.
