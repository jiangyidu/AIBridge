# AI Bridge API & Command Guide

Exposing custom Unity editor tasks to the AI Agent is done declaratively using the `[AgentCommand]` attribute.

## 1. Exposing Custom C# Methods
To register a custom command:
1. Create a `public static` method in an Editor script (or any runtime script).
2. Annotate the method with `[AgentCommand("Description of the task", category: "CategoryName")]`.
3. The method should ideally return a `string` containing feedback/log info for the AI.

### Code Example
```csharp
using UnityEngine;
using UnityEditor;
using AIBridge.Agent;

namespace AIBridge.Agent
{
    public static class LevelDesignCommands
    {
        [AgentCommand("Add random lights to selected GameObjects", category: "Level Design")]
        public static string AddLightsToSelection(float intensity, string colorHex)
        {
            var selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0)
            {
                return "Failed: No GameObjects are currently selected in the scene.";
            }

            Color lightColor = Color.white;
            ColorUtility.TryParseHtmlString(colorHex, out lightColor);

            int count = 0;
            foreach (var go in selectedObjects)
            {
                Light light = go.AddComponent<Light>();
                light.intensity = intensity;
                light.color = lightColor;
                count++;
            }

            return $"Success: Added Light component to {count} selected GameObjects.";
        }
    }
}
```

---

## 2. Argument Parsing & Type Conversion Rules
The C# HTTP bridge (`AgentBridge.cs`) parses arguments received from the Python Agent and matches them to method parameters automatically:
* **Primitive Types**: Supports automatic parsing for `int`, `float`, `double`, `bool`, `string`, and `enum` values (case-insensitive).
* **UnityEngine Types**:
  * `Vector2`, `Vector3`, `Vector4`, `Color`, `Quaternion`, `Bounds` (parsed from string format or JSON array).
  * `GameObject`: Automatically finds matching GameObjects in the scene using `GameObject.Find()`.
* **Fallback**: Supports JSON strings which can be deserialized using `JsonMapper` (LitJson).
