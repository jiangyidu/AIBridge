using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>
    /// API Key 的 EditorPrefs 加密与旧格式迁移。带版本前缀的新格式解密失败时绝不把密文
    /// 当作明文返回，避免 Domain Reload 后把 Base64/AES 密文误发给模型服务。
    /// </summary>
    public static class AgentEncryptionUtility
    {
        private const string StoragePrefix = "aibridge:v1:";
        private static readonly byte[] Salt = new byte[]
        {
            0x41, 0x49, 0x42, 0x72, 0x69, 0x64, 0x67, 0x65,
            0x53, 0x61, 0x6C, 0x74, 0x32, 0x30, 0x32, 0x36
        };

        public static string Encrypt(string plainText)
        {
            plainText = NormalizeApiKey(plainText);
            if (string.IsNullOrEmpty(plainText)) return "";
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = DeriveKey();
                    aes.GenerateIV();
                    using (MemoryStream ms = new MemoryStream())
                    {
                        ms.Write(aes.IV, 0, aes.IV.Length);
                        using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                        {
                            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                            cs.Write(plainBytes, 0, plainBytes.Length);
                            cs.FlushFinalBlock();
                        }
                        return StoragePrefix + Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                // 加密失败时不再回退为明文持久化。
                Debug.LogError("[AIBridge] API Key encryption failed: " + ex.Message);
                return "";
            }
        }

        public static string Decrypt(string storedText)
        {
            string plainText;
            bool shouldRewrite;
            if (TryDecrypt(storedText, out plainText, out shouldRewrite))
                return NormalizeApiKey(plainText);
            if (!string.IsNullOrEmpty(storedText))
                Debug.LogWarning("[AIBridge] 已保存的 API Key 无法安全解密，请在配置中重新输入。");
            return "";
        }

        /// <summary>
        /// shouldRewrite 表示读取到了旧版无前缀密文、明文或 Base64 明文，调用方应立即
        /// 使用当前带版本前缀的格式重新保存。
        /// </summary>
        public static bool TryDecrypt(string storedText, out string plainText, out bool shouldRewrite)
        {
            plainText = "";
            shouldRewrite = false;
            if (string.IsNullOrEmpty(storedText)) return true;

            bool currentFormat = storedText.StartsWith(StoragePrefix, StringComparison.Ordinal);
            string payload = currentFormat ? storedText.Substring(StoragePrefix.Length) : storedText;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(payload);
            }
            catch
            {
                if (currentFormat) return false;
                plainText = storedText; // 旧版可能直接保存了原始 API Key。
                shouldRewrite = true;
                return IsPlausiblePlainText(plainText);
            }

            if (currentFormat)
                return TryDecryptCipher(bytes, out plainText);

            // 旧版 AES 格式没有版本前缀：IV(16) + 至少一个 AES block。
            if (bytes.Length >= 32 && (bytes.Length - 16) % 16 == 0)
            {
                if (TryDecryptCipher(bytes, out plainText))
                {
                    shouldRewrite = true;
                    return true;
                }

                // 仅在解码后确实是可打印文本时兼容早期“Base64(明文)”格式。
                if (TryDecodePrintableUtf8(bytes, out plainText))
                {
                    shouldRewrite = true;
                    return true;
                }

                // 形状像旧 AES 密文但无法解密：必须要求用户重新输入，不能发送密文。
                plainText = "";
                return false;
            }

            if (TryDecodePrintableUtf8(bytes, out plainText))
            {
                shouldRewrite = true;
                return true;
            }

            // 非旧 AES 形状的 Base64 字符串可能就是服务商签发的原始 Key，保留其文本。
            plainText = storedText;
            shouldRewrite = true;
            return IsPlausiblePlainText(plainText);
        }

        public static string NormalizeApiKey(string value)
        {
            value = (value ?? "").Trim();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(7).Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') ||
                 (value[0] == '\'' && value[value.Length - 1] == '\'')))
                value = value.Substring(1, value.Length - 2).Trim();
            return value;
        }

        private static bool TryDecryptCipher(byte[] bytes, out string plainText)
        {
            plainText = "";
            if (bytes == null || bytes.Length < 32 || (bytes.Length - 16) % 16 != 0) return false;
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = DeriveKey();
                    byte[] iv = new byte[16];
                    Array.Copy(bytes, 0, iv, 0, iv.Length);
                    aes.IV = iv;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            cs.Write(bytes, iv.Length, bytes.Length - iv.Length);
                            cs.FlushFinalBlock();
                        }
                        plainText = new UTF8Encoding(false, true).GetString(ms.ToArray());
                    }
                }
                return IsPlausiblePlainText(plainText);
            }
            catch
            {
                plainText = "";
                return false;
            }
        }

        private static bool TryDecodePrintableUtf8(byte[] bytes, out string text)
        {
            text = "";
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes ?? new byte[0]);
                return IsPlausiblePlainText(text);
            }
            catch
            {
                text = "";
                return false;
            }
        }

        private static bool IsPlausiblePlainText(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 4096) return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]) || char.IsSurrogate(value[i])) return false;
            }
            return true;
        }

        private static byte[] DeriveKey()
        {
            string keyString = SystemInfo.deviceUniqueIdentifier;
            if (string.IsNullOrEmpty(keyString) || keyString == "n/a")
                keyString = "AIBridgeFallbackEncryptionKeySalt";
            using (Rfc2898DeriveBytes deriveBytes = new Rfc2898DeriveBytes(keyString, Salt, 1000))
            {
                return deriveBytes.GetBytes(32);
            }
        }
    }
}
