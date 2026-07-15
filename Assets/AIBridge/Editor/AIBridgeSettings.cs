using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>
    /// ScriptableObject storing the configuration for AI Bridge (excluding sensitive API Keys).
    /// </summary>
    public class AIBridgeSettings : ScriptableObject
    {
        private const string SettingsPath = "Assets/AIBridgeSettings.asset";

        [SerializeField] private int mode = 0; // LLMMode enum value
        [SerializeField] private int provider = 0; // APIProvider enum value
        [SerializeField] private string baseUrl = "https://api.deepseek.com";
        [SerializeField] private string modelName = "deepseek-chat";
        [SerializeField] private string ollamaUrl = "http://localhost:11434";
        [SerializeField] private string ollamaModel = "llama3";
        [SerializeField] private string userSystemPrompt = "";
        [SerializeField] private int maxSteps = 30;
        [SerializeField] private bool showReasoning = true;
        [SerializeField] private bool allowGeneratedCodeExecution = false;

        public int Mode { get { return mode; } set { mode = value; Save(); } }
        public int Provider { get { return provider; } set { provider = value; Save(); } }
        public string BaseUrl { get { return baseUrl; } set { baseUrl = value; Save(); } }
        public string ModelName { get { return modelName; } set { modelName = value; Save(); } }
        public string OllamaUrl { get { return ollamaUrl; } set { ollamaUrl = value; Save(); } }
        public string OllamaModel { get { return ollamaModel; } set { ollamaModel = value; Save(); } }
        public string UserSystemPrompt { get { return userSystemPrompt; } set { userSystemPrompt = value; Save(); } }
        public int MaxSteps { get { return maxSteps; } set { maxSteps = value; Save(); } }
        public bool ShowReasoning { get { return showReasoning; } set { showReasoning = value; Save(); } }
        public bool AllowGeneratedCodeExecution { get { return allowGeneratedCodeExecution; } set { allowGeneratedCodeExecution = value; Save(); } }

        public void Apply(
            int nextMode,
            int nextProvider,
            string nextBaseUrl,
            string nextModelName,
            string nextOllamaUrl,
            string nextOllamaModel,
            string nextUserSystemPrompt,
            int nextMaxSteps,
            bool nextShowReasoning,
            bool nextAllowGeneratedCodeExecution)
        {
            bool changed = mode != nextMode || provider != nextProvider ||
                baseUrl != (nextBaseUrl ?? "") || modelName != (nextModelName ?? "") ||
                ollamaUrl != (nextOllamaUrl ?? "") || ollamaModel != (nextOllamaModel ?? "") ||
                userSystemPrompt != (nextUserSystemPrompt ?? "") || maxSteps != nextMaxSteps ||
                showReasoning != nextShowReasoning ||
                allowGeneratedCodeExecution != nextAllowGeneratedCodeExecution;
            if (!changed) return;

            mode = nextMode;
            provider = nextProvider;
            baseUrl = nextBaseUrl ?? "";
            modelName = nextModelName ?? "";
            ollamaUrl = nextOllamaUrl ?? "";
            ollamaModel = nextOllamaModel ?? "";
            userSystemPrompt = nextUserSystemPrompt ?? "";
            maxSteps = nextMaxSteps;
            showReasoning = nextShowReasoning;
            allowGeneratedCodeExecution = nextAllowGeneratedCodeExecution;
            Save();
        }

        public static AIBridgeSettings GetOrCreateSettings()
        {
            AIBridgeSettings settings = AssetDatabase.LoadAssetAtPath<AIBridgeSettings>(SettingsPath);
            if (settings == null)
            {
                settings = CreateInstance<AIBridgeSettings>();
                
                // If it existed in legacy EditorPrefs, try to migrate it
                MigrateFromPrefs(settings);

                string directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }

        private static void MigrateFromPrefs(AIBridgeSettings settings)
        {
            if (EditorPrefs.HasKey("AIAss_Mode")) settings.mode = EditorPrefs.GetInt("AIAss_Mode", 0);
            if (EditorPrefs.HasKey("AIAss_Provider")) settings.provider = EditorPrefs.GetInt("AIAss_Provider", 0);
            if (EditorPrefs.HasKey("AIAss_BaseUrl")) settings.baseUrl = EditorPrefs.GetString("AIAss_BaseUrl", settings.baseUrl);
            if (EditorPrefs.HasKey("AIAss_Model")) settings.modelName = EditorPrefs.GetString("AIAss_Model", settings.modelName);
            if (EditorPrefs.HasKey("AIAss_OllamaUrl")) settings.ollamaUrl = EditorPrefs.GetString("AIAss_OllamaUrl", settings.ollamaUrl);
            if (EditorPrefs.HasKey("AIAss_OllamaModel")) settings.ollamaModel = EditorPrefs.GetString("AIAss_OllamaModel", settings.ollamaModel);
            if (EditorPrefs.HasKey("AIAss_UserSystemPrompt")) settings.userSystemPrompt = EditorPrefs.GetString("AIAss_UserSystemPrompt", settings.userSystemPrompt);
            if (EditorPrefs.HasKey("AIAss_MaxSteps")) settings.maxSteps = EditorPrefs.GetInt("AIAss_MaxSteps", 30);
            if (EditorPrefs.HasKey("AIAss_ShowReasoning")) settings.showReasoning = EditorPrefs.GetBool("AIAss_ShowReasoning", true);
        }

        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
    }
}
