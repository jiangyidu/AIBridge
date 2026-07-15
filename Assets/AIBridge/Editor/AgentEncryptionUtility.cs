using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>
    /// Utility class for encrypting and decrypting API keys stored in EditorPrefs.
    /// Uses AES-256 with a key derived from the machine's unique identifier (SystemInfo.deviceUniqueIdentifier)
    /// to prevent keys from being extracted if preference files are copied to another machine.
    /// </summary>
    public static class AgentEncryptionUtility
    {
        private static readonly byte[] Salt = new byte[]
        {
            0x41, 0x49, 0x42, 0x72, 0x69, 0x64, 0x67, 0x65,
            0x53, 0x61, 0x6C, 0x74, 0x32, 0x30, 0x32, 0x36
        };

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            try
            {
                string keyStr = SystemInfo.deviceUniqueIdentifier;
                if (string.IsNullOrEmpty(keyStr) || keyStr == "n/a")
                {
                    keyStr = "AIBridgeFallbackEncryptionKeySalt";
                }

                byte[] keyBytes;
                using (var deriveBytes = new Rfc2898DeriveBytes(keyStr, Salt, 1000))
                {
                    keyBytes = deriveBytes.GetBytes(32); // AES-256
                }

                using (Aes aes = Aes.Create())
                {
                    aes.Key = keyBytes;
                    aes.GenerateIV();
                    byte[] iv = aes.IV;

                    using (MemoryStream ms = new MemoryStream())
                    {
                        ms.Write(iv, 0, iv.Length);
                        using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                        {
                            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                            cs.Write(plainBytes, 0, plainBytes.Length);
                            cs.FlushFinalBlock();
                        }
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[AIBridge] API Key encryption failed: " + ex.Message);
                return plainText; // Fallback to plain text if exception occurs
            }
        }

        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";
            try
            {
                byte[] cipherBytes;
                try
                {
                    cipherBytes = Convert.FromBase64String(cipherText);
                }
                catch
                {
                    return cipherText; // Not a base64 string, probably raw key
                }

                if (cipherBytes.Length < 16)
                {
                    // Too short to contain IV, probably just a legacy base64 encoded raw key
                    try
                    {
                        return Encoding.UTF8.GetString(cipherBytes);
                    }
                    catch
                    {
                        return cipherText;
                    }
                }

                string keyStr = SystemInfo.deviceUniqueIdentifier;
                if (string.IsNullOrEmpty(keyStr) || keyStr == "n/a")
                {
                    keyStr = "AIBridgeFallbackEncryptionKeySalt";
                }

                byte[] keyBytes;
                using (var deriveBytes = new Rfc2898DeriveBytes(keyStr, Salt, 1000))
                {
                    keyBytes = deriveBytes.GetBytes(32);
                }

                using (Aes aes = Aes.Create())
                {
                    aes.Key = keyBytes;
                    byte[] iv = new byte[16];
                    Array.Copy(cipherBytes, 0, iv, 0, iv.Length);
                    aes.IV = iv;

                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            cs.Write(cipherBytes, iv.Length, cipherBytes.Length - iv.Length);
                            cs.FlushFinalBlock();
                        }
                        return Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
            }
            catch
            {
                // Decryption failed: fallback to legacy Base64 decoding or raw key
                try
                {
                    byte[] bytes = Convert.FromBase64String(cipherText);
                    return Encoding.UTF8.GetString(bytes);
                }
                catch
                {
                    return cipherText;
                }
            }
        }
    }
}
