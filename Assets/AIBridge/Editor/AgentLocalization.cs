using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using LitJson;

namespace AIBridge.Agent
{
    /// <summary>
    /// AIBridge 中英文本地化管理器。
    /// 从 Assets/AIBridge/Editor/Localization.json 动态加载翻译文本。
    /// </summary>
    public static class AgentLocalization
    {
        private static string _currentLanguage = "zh";
        private static JsonData _translations;

        public static string CurrentLanguage
        {
            get 
            {
                return _currentLanguage; 
            }
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    EditorPrefs.SetString("AIBridge_Language", _currentLanguage);
                }
            }
        }

        static AgentLocalization()
        {
            LoadLanguage();
            LoadTranslations();
        }

        public static void LoadLanguage()
        {
            string systemLang = "en";
            if (Application.systemLanguage == SystemLanguage.Chinese || 
                Application.systemLanguage == SystemLanguage.ChineseSimplified || 
                Application.systemLanguage == SystemLanguage.ChineseTraditional)
            {
                systemLang = "zh";
            }
            _currentLanguage = EditorPrefs.GetString("AIBridge_Language", systemLang);
        }

        public static void LoadTranslations()
        {
            try
            {
                string path = GetLocalizationPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    _translations = JsonMapper.ToObject(json);
                }
                else
                {
                    Debug.LogWarning("[AgentLocalization] Localization file not found: " + path);
                    _translations = new JsonData();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[AgentLocalization] Failed to load translations: " + e.Message);
                _translations = new JsonData();
            }
        }

        private static string GetLocalizationPath()
        {
            // 优先通过 AssetDatabase 查找 Localization.json
            string[] guids = AssetDatabase.FindAssets("Localization");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("Localization.json"))
                {
                    return Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
                }
            }
            // 退路：拼接默认路径
            return Path.Combine(Application.dataPath, "AIBridge", "Editor", "Localization.json");
        }

        /// <summary>
        /// 获取翻译文本。若 key 不存在，返回 key 本身或默认值。
        /// </summary>
        public static string Get(string key, string defaultValue = "")
        {
            if (_translations == null)
            {
                LoadTranslations();
            }
            
            if (_translations != null && _translations.Keys.Contains(key))
            {
                JsonData langData = _translations[key];
                if (langData.Keys.Contains(_currentLanguage))
                {
                    return langData[_currentLanguage].ToString();
                }
            }
            return !string.IsNullOrEmpty(defaultValue) ? defaultValue : key;
        }

        /// <summary>
        /// 获取格式化后的翻译文本。
        /// </summary>
        public static string GetFormat(string key, params object[] args)
        {
            string raw = Get(key);
            try
            {
                return string.Format(raw, args);
            }
            catch
            {
                return raw;
            }
        }
    }
}
