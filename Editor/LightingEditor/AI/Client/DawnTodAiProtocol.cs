using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DawnTODEditor.AI
{
    internal static class DawnTodAiProtocol
    {
        public const string Host = "127.0.0.1";
        public const int Port = 13296;
        public const string BaseUrl = "http://127.0.0.1:13296/";
        public const string SessionTokenHeader = "X-DawnTOD-Session-Token";
        public const string SessionTokenEnvironmentVariable = "DAWN_TOD_AI_SESSION_TOKEN";
        public const string ParentPidEnvironmentVariable = "DAWN_TOD_AI_PARENT_PID";
        public const string ConfigPathEnvironmentVariable = "DAWN_TOD_AI_CONFIG_PATH";
        public const string Mode = "deepseek";
        public const string ServiceVersion = "2.0.0";
        public const string SchemaVersion = "1.0";
        public const string DeepSeekBaseUrl = "https://api.deepseek.com";
        public const string DeepSeekModel = "deepseek-v4-flash";

        public static readonly string[] SupportedNonNullFields =
        {
            "time",
            "sun.azimuth_deg",
            "sun.elevation_deg",
            "sun.intensity",
            "sun.color",
            "moon.azimuth_deg",
            "moon.elevation_deg",
            "moon.intensity",
            "moon.color"
        };
    }

    internal sealed class DawnTodAiHttpResult
    {
        public bool HasResponse { get; }
        public HttpStatusCode StatusCode { get; }
        public string Body { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        private DawnTodAiHttpResult(
            bool hasResponse,
            HttpStatusCode statusCode,
            string body,
            string errorCode,
            string errorMessage)
        {
            HasResponse = hasResponse;
            StatusCode = statusCode;
            Body = body;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public static DawnTodAiHttpResult Response(HttpStatusCode statusCode, string body)
        {
            return new DawnTodAiHttpResult(true, statusCode, body ?? string.Empty, null, null);
        }

        public static DawnTodAiHttpResult Failure(string code, string message)
        {
            return new DawnTodAiHttpResult(false, 0, string.Empty, code, message);
        }
    }

    internal sealed class DawnTodAiHealthResult
    {
        public bool IsReady { get; }
        public bool IsRetryable { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public string SkillHash { get; }

        private DawnTodAiHealthResult(
            bool isReady,
            bool isRetryable,
            string errorCode,
            string errorMessage,
            string skillHash)
        {
            IsReady = isReady;
            IsRetryable = isRetryable;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            SkillHash = skillHash;
        }

        public static DawnTodAiHealthResult Ready(string skillHash)
        {
            return new DawnTodAiHealthResult(true, false, null, null, skillHash);
        }

        public static DawnTodAiHealthResult Failed(
            string code,
            string message,
            bool retryable = false)
        {
            return new DawnTodAiHealthResult(false, retryable, code, message, null);
        }
    }

    internal static class DawnTodAiProtocolValidator
    {
        private static readonly HashSet<string> StatusFields = new HashSet<string>
        {
            "status",
            "ready",
            "mode",
            "service_version",
            "schema_version",
            "skill_hash",
            "errors"
        };

        private static readonly HashSet<string> EnvelopeFields = new HashSet<string>
        {
            "request_id",
            "status",
            "mode",
            "data",
            "error"
        };

        public static DawnTodAiHealthResult ValidateHealth(
            DawnTodAiHttpResult result,
            string expectedSkillHash)
        {
            if (result == null || !result.HasResponse)
            {
                return DawnTodAiHealthResult.Failed(
                    result?.ErrorCode ?? "CONNECTION_FAILED",
                    result?.ErrorMessage ?? "The service did not return a response.",
                    true);
            }

            if (result.StatusCode == HttpStatusCode.Unauthorized)
            {
                return DawnTodAiHealthResult.Failed(
                    "AUTH_FAILED",
                    "The service rejected the session token.");
            }

            if (result.StatusCode != HttpStatusCode.OK)
            {
                return DawnTodAiHealthResult.Failed(
                    "HEALTH_HTTP_ERROR",
                    $"The health endpoint returned HTTP {(int)result.StatusCode}.",
                    (int)result.StatusCode >= 500);
            }

            if (!TryParseObject(result.Body, out JObject root, out string parseError))
            {
                return DawnTodAiHealthResult.Failed("HEALTH_INVALID", parseError);
            }

            if (!HasExactFields(root, StatusFields))
            {
                return DawnTodAiHealthResult.Failed(
                    "HEALTH_INVALID",
                    "The health response fields do not match the protocol.");
            }

            if (!TryReadString(root, "status", out string status) ||
                !TryReadBoolean(root, "ready", out bool ready) ||
                !TryReadString(root, "mode", out string mode) ||
                !TryReadString(root, "service_version", out string serviceVersion) ||
                !TryReadString(root, "schema_version", out string schemaVersion) ||
                !TryReadString(root, "skill_hash", out string skillHash) ||
                root["errors"]?.Type != JTokenType.Array)
            {
                return DawnTodAiHealthResult.Failed(
                    "HEALTH_INVALID",
                    "The health response contains invalid field types.");
            }

            if (!string.Equals(mode, DawnTodAiProtocol.Mode, StringComparison.Ordinal))
            {
                return DawnTodAiHealthResult.Failed(
                    "SERVICE_MODE_MISMATCH",
                    "The service mode does not match the client mode.");
            }

            if (!string.Equals(serviceVersion, DawnTodAiProtocol.ServiceVersion, StringComparison.Ordinal))
            {
                return DawnTodAiHealthResult.Failed(
                    "SERVICE_VERSION_MISMATCH",
                    "The service version does not match the client version.");
            }

            if (!string.Equals(schemaVersion, DawnTodAiProtocol.SchemaVersion, StringComparison.Ordinal))
            {
                return DawnTodAiHealthResult.Failed(
                    "SCHEMA_VERSION_MISMATCH",
                    "The service Schema version does not match the client version.");
            }

            if (!string.Equals(skillHash, expectedSkillHash, StringComparison.OrdinalIgnoreCase))
            {
                return DawnTodAiHealthResult.Failed(
                    "SKILL_HASH_MISMATCH",
                    "The service Skill hash does not match the package resource.");
            }

            if (!ready || !string.Equals(status, "ready", StringComparison.Ordinal))
            {
                string[] errors = ((JArray)root["errors"])
                    .Where(token => token.Type == JTokenType.String)
                    .Select(token => token.Value<string>())
                    .ToArray();
                string suffix = errors.Length > 0 ? " " + string.Join(", ", errors) : string.Empty;
                return DawnTodAiHealthResult.Failed(
                    "SERVICE_NOT_READY",
                    "The service resources are not ready." + suffix);
            }

            return DawnTodAiHealthResult.Ready(skillHash);
        }

        public static bool TryParseAnalyzeEnvelope(
            DawnTodAiHttpResult result,
            string expectedRequestId,
            out WeatherIntentPatch patch,
            out string rawJson,
            out string errorCode,
            out string errorMessage)
        {
            patch = null;
            rawJson = result?.Body ?? string.Empty;
            errorCode = null;
            errorMessage = null;

            if (result == null || !result.HasResponse)
            {
                errorCode = result?.ErrorCode ?? "CONNECTION_FAILED";
                errorMessage = result?.ErrorMessage ?? "The service did not return a response.";
                return false;
            }

            if (!TryParseObject(result.Body, out JObject root, out errorMessage))
            {
                errorCode = "ENVELOPE_INVALID";
                return false;
            }

            if (!HasExactFields(root, EnvelopeFields) ||
                !TryReadString(root, "request_id", out string requestId) ||
                !TryReadString(root, "status", out string status) ||
                !TryReadString(root, "mode", out string mode))
            {
                errorCode = "ENVELOPE_INVALID";
                errorMessage = "The response envelope fields do not match the protocol.";
                return false;
            }

            if (!string.Equals(requestId, expectedRequestId, StringComparison.Ordinal))
            {
                errorCode = "REQUEST_ID_MISMATCH";
                errorMessage = "The response request id does not match the active request.";
                return false;
            }

            if (!string.Equals(mode, DawnTodAiProtocol.Mode, StringComparison.Ordinal))
            {
                errorCode = "SERVICE_MODE_MISMATCH";
                errorMessage = "The response service mode does not match the client mode.";
                return false;
            }

            if (result.StatusCode != HttpStatusCode.OK ||
                !string.Equals(status, "ok", StringComparison.Ordinal))
            {
                ReadEnvelopeError(root, out errorCode, out errorMessage);
                if (string.IsNullOrEmpty(errorCode))
                {
                    errorCode = "ANALYZE_FAILED";
                    errorMessage = $"The analysis endpoint returned HTTP {(int)result.StatusCode}.";
                }

                return false;
            }

            if (root["data"]?.Type != JTokenType.Object ||
                root["error"]?.Type != JTokenType.Null)
            {
                errorCode = "ENVELOPE_INVALID";
                errorMessage = "A successful response must contain data and a null error.";
                return false;
            }

            string dataJson = root["data"].ToString(Formatting.None);
            WeatherIntentParseResult parseResult = WeatherIntentJsonParser.Parse(dataJson);
            if (!parseResult.IsValid)
            {
                errorCode = parseResult.ErrorCode;
                errorMessage = parseResult.ErrorMessage;
                return false;
            }

            patch = parseResult.Patch;
            return true;
        }

        private static void ReadEnvelopeError(
            JObject root,
            out string errorCode,
            out string errorMessage)
        {
            errorCode = null;
            errorMessage = null;
            if (root["error"]?.Type != JTokenType.Object)
            {
                return;
            }

            var error = (JObject)root["error"];
            TryReadString(error, "code", out errorCode);
            TryReadString(error, "message", out errorMessage);
        }

        private static bool TryParseObject(string json, out JObject value, out string error)
        {
            value = null;
            error = null;
            try
            {
                value = JObject.Parse(
                    json,
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                return true;
            }
            catch (JsonException)
            {
                error = "The response is not a valid JSON object.";
                return false;
            }
        }

        private static bool HasExactFields(JObject value, HashSet<string> fields)
        {
            return fields.SetEquals(value.Properties().Select(property => property.Name));
        }

        private static bool TryReadString(JObject value, string field, out string result)
        {
            JToken token = value[field];
            result = token?.Type == JTokenType.String ? token.Value<string>() : null;
            return result != null;
        }

        private static bool TryReadBoolean(JObject value, string field, out bool result)
        {
            JToken token = value[field];
            if (token?.Type == JTokenType.Boolean)
            {
                result = token.Value<bool>();
                return true;
            }

            result = false;
            return false;
        }
    }
}
