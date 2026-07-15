using UnityEngine;
using UnityEditor;
using AIBridge.Agent;

namespace AIBridge.Agent
{
    /// <summary>
    /// Advanced sample demonstrating how to write custom commands and register them with AI Bridge.
    /// Methods marked with the [AgentCommand] attribute will automatically be indexed by the AgentCommandRegistry
    /// and made available to the LLM agent via the HTTP bridge.
    /// </summary>
    public static class CustomAgentCommandsDemo
    {
        [AgentCommand("Create a circle of primitive GameObjects in the scene", category: "Advanced Demo")]
        public static string CreateCircleOfObjects(string primitiveTypeName, int count, float radius)
        {
            PrimitiveType type;
            if (!System.Enum.TryParse(primitiveTypeName, true, out type))
            {
                return $"Error: '{primitiveTypeName}' is not a valid PrimitiveType. Please choose from Cube, Sphere, Cylinder, Capsule, Plane, Quad.";
            }

            GameObject container = new GameObject($"CircleOf{primitiveTypeName}s");
            container.transform.position = Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2 / count;
                Vector3 pos = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);

                GameObject go = GameObject.CreatePrimitive(type);
                go.name = $"{primitiveTypeName}_{i}";
                go.transform.position = pos;
                go.transform.parent = container.transform;
            }

            return $"Successfully created a circle of {count} {primitiveTypeName}s with a radius of {radius} units.";
        }

        [AgentCommand("Change the color of all GameObjects with a specific prefix in their name", category: "Advanced Demo")]
        public static string ColorObjectsWithPrefix(string prefix, string colorName)
        {
            Color color;
            if (!ColorUtility.TryParseHtmlString(colorName, out color))
            {
                // Fallback basic color checking
                switch (colorName.ToLower())
                {
                    case "red": color = Color.red; break;
                    case "green": color = Color.green; break;
                    case "blue": color = Color.blue; break;
                    case "yellow": color = Color.yellow; break;
                    case "white": color = Color.white; break;
                    default:
                        return $"Error: Cannot parse color '{colorName}'. Use HTML hex (e.g. #FF0000) or standard names (red, green, blue).";
                }
            }

            int count = 0;
            Renderer[] renderers = Object.FindObjectsOfType<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer.gameObject.name.StartsWith(prefix))
                {
                    renderer.sharedMaterial = new Material(renderer.sharedMaterial);
                    renderer.sharedMaterial.color = color;
                    count++;
                }
            }

            return $"Successfully changed the color of {count} GameObjects matching prefix '{prefix}' to {colorName}.";
        }
    }
}
