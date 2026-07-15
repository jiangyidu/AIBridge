# Advanced Agent Sample

This sample demonstrates how to write and expose custom C# commands to the AI Agent using the `[AgentCommand]` attribute, and how the Agent can generate and compile MonoBehaviour scripts dynamically.

## Included Files

* **[CustomAgentCommandsDemo.cs](file:///f:/otherProject/AIBridge2026617/Assets/AIBridge/Samples/AdvancedAgent/Editor/CustomAgentCommandsDemo.cs)**: Exposes two custom commands to the AI:
  * `CreateCircleOfObjects`: Generates a circular layout of primitive 3D shapes.
  * `ColorObjectsWithPrefix`: Searches and colors all renderers whose game objects match a given name prefix.
* **[AdvancedDemoScene.unity](file:///f:/otherProject/AIBridge2026617/Assets/AIBridge/Samples/AdvancedAgent/AdvancedDemoScene.unity)**: A pre-configured test environment.

## How to Test

1. Open the [AdvancedDemoScene.unity](file:///f:/otherProject/AIBridge2026617/Assets/AIBridge/Samples/AdvancedAgent/AdvancedDemoScene.unity) scene.
2. Open the AI Assistant window (`AIBridge -> AI Assistant`).
3. Send a natural language prompt to the AI, instructing it to use your custom command. For example:
   > "Please create a circle of 8 spheres with a radius of 5 units."
4. Observe the AI identifying and invoking the `CreateCircleOfObjects` method inside the [CustomAgentCommandsDemo.cs](file:///f:/otherProject/AIBridge2026617/Assets/AIBridge/Samples/AdvancedAgent/Editor/CustomAgentCommandsDemo.cs) class.
5. Next, try:
   > "Change the color of all sphere objects we just created to blue."
6. The AI will call `ColorObjectsWithPrefix` using `Sphere` as the prefix and `blue` as the color parameter.
