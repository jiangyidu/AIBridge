# API and Extension Guide

Register a public static method with `[AgentCommand]`:

```csharp
using AIBridge.Agent;
using UnityEditor;
using UnityEngine;

public static class MyEditorCommands
{
    [AgentCommand("Create an undoable object", category: "Custom")]
    public static string CreateObject(string objectName)
    {
        GameObject value = new GameObject(string.IsNullOrEmpty(objectName) ? "Generated" : objectName);
        Undo.RegisterCreatedObjectUndo(value, "Create object");
        return value.name;
    }
}
```

Commands should validate parameters, support Undo, avoid paths outside the project, and be safe to call repeatedly.

New fixed tools require a stable name in `BridgeProtocol`, a schema entry in `AgentTools.json`, a main-thread implementation in `AgentToolRouter`, and a structured JSON result.

All generated source must use `CompileTransactionManager.BeginGeneratedSource`. Persist intent before writing Assets, keep compile and execute separate, and never store credentials in durable state.
