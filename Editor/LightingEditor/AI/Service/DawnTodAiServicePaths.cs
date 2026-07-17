using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor.PackageManager;
using UnityEngine;

namespace DawnTODEditor.AI
{
    internal sealed class DawnTodAiServicePaths
    {
        public string PackageRoot { get; }
        public string ServiceDirectory { get; }
        public string ExecutablePath { get; }
        public string SchemaPath { get; }
        public string SkillPath { get; }
        public string PromptPath { get; }

        public DawnTodAiServicePaths(string packageRoot)
        {
            if (string.IsNullOrWhiteSpace(packageRoot))
            {
                throw new ArgumentException("A package root is required.", nameof(packageRoot));
            }

            PackageRoot = Path.GetFullPath(packageRoot);
            ServiceDirectory = Path.Combine(
                PackageRoot,
                "Editor",
                "LightingEditor",
                "AI",
                "Service");
            ExecutablePath = Path.Combine(
                ServiceDirectory,
                "Windows",
                "DawnTodAiService.exe");
            SchemaPath = Path.Combine(
                PackageRoot,
                "Editor",
                "LightingEditor",
                "AI",
                "Schemas",
                "weather-intent-v1.schema.json");
            SkillPath = Path.Combine(
                PackageRoot,
                "Editor",
                "LightingEditor",
                "AI",
                "Skills",
                "weather-intent",
                "SKILL.md");
            PromptPath = Path.Combine(
                PackageRoot,
                "Editor",
                "LightingEditor",
                "AI",
                "Prompts",
                "weather-intent-system.md");
        }

        public static bool TryResolveCurrentPackage(
            out DawnTodAiServicePaths paths,
            out string errorCode,
            out string errorMessage)
        {
            paths = null;
            errorCode = null;
            errorMessage = null;
            try
            {
                PackageInfo package = PackageInfo.FindForAssembly(Assembly.GetExecutingAssembly());
                if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
                {
                    errorCode = "PACKAGE_ROOT_NOT_FOUND";
                    errorMessage = "The DawnTOD package root could not be resolved.";
                    return false;
                }

                paths = new DawnTodAiServicePaths(package.resolvedPath);
                return true;
            }
            catch (Exception)
            {
                errorCode = "PACKAGE_ROOT_NOT_FOUND";
                errorMessage = "The DawnTOD package root could not be resolved.";
                return false;
            }
        }
    }

    internal sealed class DawnTodAiPreflightResult
    {
        public bool IsValid { get; }
        public string SkillHash { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        private DawnTodAiPreflightResult(
            bool isValid,
            string skillHash,
            string errorCode,
            string errorMessage)
        {
            IsValid = isValid;
            SkillHash = skillHash;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public static DawnTodAiPreflightResult Valid(string skillHash)
        {
            return new DawnTodAiPreflightResult(true, skillHash, null, null);
        }

        public static DawnTodAiPreflightResult Invalid(string code, string message)
        {
            return new DawnTodAiPreflightResult(false, null, code, message);
        }
    }

    internal static class DawnTodAiServicePreflight
    {
        public static DawnTodAiPreflightResult Validate(
            DawnTodAiServicePaths paths,
            RuntimePlatform platform)
        {
            if (platform != RuntimePlatform.WindowsEditor)
            {
                return DawnTodAiPreflightResult.Invalid(
                    "UNSUPPORTED_PLATFORM",
                    "The DawnTOD AI service is only supported in the Windows Unity Editor.");
            }

            if (paths == null)
            {
                return DawnTodAiPreflightResult.Invalid(
                    "PACKAGE_ROOT_NOT_FOUND",
                    "The DawnTOD package paths are unavailable.");
            }

            if (!ArePathsInsidePackage(paths))
            {
                return DawnTodAiPreflightResult.Invalid(
                    "PACKAGE_PATH_INVALID",
                    "A service resource resolves outside the DawnTOD package.");
            }

            DawnTodAiPreflightResult missing = RequireFile(paths.ExecutablePath, "EXE_MISSING");
            if (missing != null)
            {
                return missing;
            }

            missing = RequireFile(paths.SchemaPath, "SCHEMA_MISSING");
            if (missing != null)
            {
                return missing;
            }

            missing = RequireFile(paths.SkillPath, "SKILL_MISSING");
            if (missing != null)
            {
                return missing;
            }

            missing = RequireFile(paths.PromptPath, "PROMPT_MISSING");
            if (missing != null)
            {
                return missing;
            }

            try
            {
                using (SHA256 sha256 = SHA256.Create())
                using (FileStream stream = File.OpenRead(paths.SkillPath))
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    return DawnTodAiPreflightResult.Valid(ToLowerHex(hash));
                }
            }
            catch (Exception)
            {
                return DawnTodAiPreflightResult.Invalid(
                    "SKILL_HASH_FAILED",
                    "The package Skill hash could not be calculated.");
            }
        }

        private static DawnTodAiPreflightResult RequireFile(string path, string code)
        {
            return File.Exists(path)
                ? null
                : DawnTodAiPreflightResult.Invalid(code, $"Required service file is missing: {Path.GetFileName(path)}");
        }

        private static bool ArePathsInsidePackage(DawnTodAiServicePaths paths)
        {
            string root = Path.GetFullPath(paths.PackageRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return IsInside(root, paths.ServiceDirectory) &&
                   IsInside(root, paths.ExecutablePath) &&
                   IsInside(root, paths.SchemaPath) &&
                   IsInside(root, paths.SkillPath) &&
                   IsInside(root, paths.PromptPath);
        }

        private static bool IsInside(string rootWithSeparator, string candidate)
        {
            string fullPath = Path.GetFullPath(candidate);
            return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToLowerHex(byte[] value)
        {
            return BitConverter.ToString(value).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
