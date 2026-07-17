using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DawnTODEditor.AI
{
    internal interface IDawnTodAiDataProtector
    {
        byte[] Protect(byte[] plaintext);
        byte[] Unprotect(byte[] ciphertext);
    }

    internal sealed class DawnTodAiCurrentUserDataProtector : IDawnTodAiDataProtector
    {
        private const int CryptProtectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            int flags,
            out DataBlob dataOut);

        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            int flags,
            out DataBlob dataOut);

        [DllImport("Kernel32.dll", SetLastError = false)]
        private static extern IntPtr LocalFree(IntPtr memory);

        public byte[] Protect(byte[] plaintext)
        {
            return Transform(plaintext, true);
        }

        public byte[] Unprotect(byte[] ciphertext)
        {
            return Transform(ciphertext, false);
        }

        private static byte[] Transform(byte[] source, bool protect)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                throw new CryptographicException(
                    "Windows DPAPI is unavailable on this platform.");
            }

            var input = new DataBlob();
            var output = new DataBlob();
            try
            {
                input.Size = source.Length;
                input.Data = Marshal.AllocHGlobal(Math.Max(source.Length, 1));
                if (source.Length > 0)
                {
                    Marshal.Copy(source, 0, input.Data, source.Length);
                }

                bool succeeded = protect
                    ? CryptProtectData(
                        ref input,
                        null,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out output)
                    : CryptUnprotectData(
                        ref input,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out output);
                if (!succeeded || output.Data == IntPtr.Zero || output.Size <= 0)
                {
                    throw new CryptographicException(
                        "Windows DPAPI could not complete the requested operation.");
                }

                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                ZeroMemory(input.Data, input.Size);
                if (input.Data != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(input.Data);
                }

                ZeroMemory(output.Data, output.Size);
                if (output.Data != IntPtr.Zero)
                {
                    LocalFree(output.Data);
                }
            }
        }

        private static void ZeroMemory(IntPtr memory, int size)
        {
            if (memory == IntPtr.Zero || size <= 0)
            {
                return;
            }

            for (int index = 0; index < size; index++)
            {
                Marshal.WriteByte(memory, index, 0);
            }
        }
    }

    internal sealed class DawnTodAiApiKeyStatus
    {
        public bool IsConfigured { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        private DawnTodAiApiKeyStatus(
            bool isConfigured,
            string errorCode,
            string errorMessage)
        {
            IsConfigured = isConfigured;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public static DawnTodAiApiKeyStatus Configured()
        {
            return new DawnTodAiApiKeyStatus(true, null, null);
        }

        public static DawnTodAiApiKeyStatus NotConfigured(string code, string message)
        {
            return new DawnTodAiApiKeyStatus(false, code, message);
        }
    }

    internal sealed class DawnTodAiApiKeyOperationResult
    {
        public bool IsSuccess { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        private DawnTodAiApiKeyOperationResult(
            bool isSuccess,
            string errorCode,
            string errorMessage)
        {
            IsSuccess = isSuccess;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public static DawnTodAiApiKeyOperationResult Success()
        {
            return new DawnTodAiApiKeyOperationResult(true, null, null);
        }

        public static DawnTodAiApiKeyOperationResult Failed(string code, string message)
        {
            return new DawnTodAiApiKeyOperationResult(false, code, message);
        }
    }

    internal sealed class DawnTodAiApiKeyStore
    {
        private const int ConfigVersion = 1;
        private const string ProtectionName = "dpapi-current-user";

        private static readonly HashSet<string> RootFields =
            new HashSet<string> { "version", "api_key" };
        private static readonly HashSet<string> ApiKeyFields =
            new HashSet<string> { "protection", "ciphertext" };

        private readonly string _configPath;
        private readonly IDawnTodAiDataProtector _protector;

        public string ConfigPath => _configPath;

        public DawnTodAiApiKeyStore(
            string configPath = null,
            IDawnTodAiDataProtector protector = null)
        {
            _configPath = Path.GetFullPath(
                configPath ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DawnTODAI",
                    "config.json"));
            _protector = protector ?? new DawnTodAiCurrentUserDataProtector();
        }

        public DawnTodAiApiKeyStatus GetStatus()
        {
            if (!File.Exists(_configPath))
            {
                return DawnTodAiApiKeyStatus.NotConfigured(
                    "API_KEY_MISSING",
                    "No DeepSeek API key is configured for the current Windows user.");
            }

            byte[] ciphertext = null;
            byte[] plaintext = null;
            try
            {
                JObject root = JObject.Parse(
                    File.ReadAllText(_configPath, Encoding.UTF8),
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                if (!RootFields.SetEquals(root.Properties().Select(property => property.Name)) ||
                    root["version"]?.Type != JTokenType.Integer ||
                    root["version"].Value<int>() != ConfigVersion ||
                    root["api_key"]?.Type != JTokenType.Object)
                {
                    throw new InvalidDataException();
                }

                var keyRecord = (JObject)root["api_key"];
                if (!ApiKeyFields.SetEquals(
                        keyRecord.Properties().Select(property => property.Name)) ||
                    keyRecord["protection"]?.Type != JTokenType.String ||
                    keyRecord["protection"].Value<string>() != ProtectionName ||
                    keyRecord["ciphertext"]?.Type != JTokenType.String)
                {
                    throw new InvalidDataException();
                }

                ciphertext = Convert.FromBase64String(
                    keyRecord["ciphertext"].Value<string>() ?? string.Empty);
                if (ciphertext.Length == 0)
                {
                    throw new InvalidDataException();
                }

                plaintext = _protector.Unprotect(ciphertext);
                string apiKey = new UTF8Encoding(false, true)
                    .GetString(plaintext)
                    .Trim();
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidDataException();
                }

                return DawnTodAiApiKeyStatus.Configured();
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is JsonException ||
                exception is FormatException ||
                exception is CryptographicException ||
                exception is DecoderFallbackException ||
                exception is InvalidDataException)
            {
                return DawnTodAiApiKeyStatus.NotConfigured(
                    "API_KEY_CONFIG_INVALID",
                    "The encrypted DeepSeek API key configuration is invalid or unreadable.");
            }
            finally
            {
                Clear(ciphertext);
                Clear(plaintext);
            }
        }

        public DawnTodAiApiKeyOperationResult Save(string apiKey)
        {
            string normalized = apiKey?.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                return DawnTodAiApiKeyOperationResult.Failed(
                    "API_KEY_EMPTY",
                    "Enter a non-empty DeepSeek API key.");
            }

            byte[] plaintext = null;
            byte[] ciphertext = null;
            string temporaryPath = null;
            try
            {
                plaintext = new UTF8Encoding(false, true).GetBytes(normalized);
                ciphertext = _protector.Protect(plaintext);
                if (ciphertext == null || ciphertext.Length == 0)
                {
                    throw new CryptographicException();
                }

                var root = new JObject
                {
                    ["version"] = ConfigVersion,
                    ["api_key"] = new JObject
                    {
                        ["protection"] = ProtectionName,
                        ["ciphertext"] = Convert.ToBase64String(ciphertext)
                    }
                };
                string directory = Path.GetDirectoryName(_configPath);
                if (string.IsNullOrEmpty(directory))
                {
                    throw new IOException();
                }

                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(
                    directory,
                    Path.GetFileName(_configPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(
                    temporaryPath,
                    root.ToString(Formatting.Indented),
                    new UTF8Encoding(false));
                if (File.Exists(_configPath))
                {
                    File.Replace(temporaryPath, _configPath, null);
                }
                else
                {
                    File.Move(temporaryPath, _configPath);
                }

                temporaryPath = null;
                return DawnTodAiApiKeyOperationResult.Success();
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is CryptographicException ||
                exception is EncoderFallbackException)
            {
                return DawnTodAiApiKeyOperationResult.Failed(
                    "API_KEY_SAVE_FAILED",
                    "The DeepSeek API key could not be encrypted and saved.");
            }
            finally
            {
                Clear(plaintext);
                Clear(ciphertext);
                if (!string.IsNullOrEmpty(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (Exception)
                    {
                        // A failed cleanup must not expose the key or replace the primary error.
                    }
                }
            }
        }

        public DawnTodAiApiKeyOperationResult Clear()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    File.Delete(_configPath);
                }

                return DawnTodAiApiKeyOperationResult.Success();
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException)
            {
                return DawnTodAiApiKeyOperationResult.Failed(
                    "API_KEY_CLEAR_FAILED",
                    "The encrypted DeepSeek API key configuration could not be removed.");
            }
        }

        private static void Clear(byte[] value)
        {
            if (value != null)
            {
                Array.Clear(value, 0, value.Length);
            }
        }
    }
}
