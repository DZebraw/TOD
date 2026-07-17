using System;
using DawnTOD;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DawnTODEditor.AI
{
    internal sealed class WeatherIntentAnalyzeRequest
    {
        public string RequestId { get; }
        public string Json { get; }
        public WeatherIntentTargetSnapshot Target { get; }

        public WeatherIntentAnalyzeRequest(
            string requestId,
            string json,
            WeatherIntentTargetSnapshot target)
        {
            RequestId = requestId;
            Json = json;
            Target = target;
        }
    }

    internal sealed class WeatherIntentAnalyzeRequestBuildResult
    {
        public bool IsValid { get; }
        public WeatherIntentAnalyzeRequest Request { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        private WeatherIntentAnalyzeRequestBuildResult(
            bool isValid,
            WeatherIntentAnalyzeRequest request,
            string errorCode,
            string errorMessage)
        {
            IsValid = isValid;
            Request = request;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public static WeatherIntentAnalyzeRequestBuildResult Valid(
            WeatherIntentAnalyzeRequest request)
        {
            return new WeatherIntentAnalyzeRequestBuildResult(true, request, null, null);
        }

        public static WeatherIntentAnalyzeRequestBuildResult Invalid(string code, string message)
        {
            return new WeatherIntentAnalyzeRequestBuildResult(false, null, code, message);
        }
    }

    internal static class WeatherIntentAnalyzeRequestBuilder
    {
        public static WeatherIntentAnalyzeRequestBuildResult Build(
            string userInput,
            DawnWeatherController controller,
            float capturedHour)
        {
            if (string.IsNullOrWhiteSpace(userInput))
            {
                return WeatherIntentAnalyzeRequestBuildResult.Invalid(
                    "EMPTY_INPUT",
                    "Natural-language input cannot be empty.");
            }

            WeatherIntentTargetSnapshot target;
            try
            {
                target = WeatherIntentTargetSnapshot.Capture(controller, capturedHour);
            }
            catch (Exception exception)
            {
                return WeatherIntentAnalyzeRequestBuildResult.Invalid(
                    "TARGET_INVALID",
                    exception.Message);
            }

            DawnWeatherPreset preset = target.Preset;
            if (preset.sunAzimuthCurve == null ||
                preset.sunElevationCurve == null ||
                preset.sunIntensityCurve == null ||
                preset.sunColorGradient == null ||
                preset.moonAzimuthCurve == null ||
                preset.moonElevationCurve == null ||
                preset.moonIntensityCurve == null ||
                preset.moonColorGradient == null)
            {
                return WeatherIntentAnalyzeRequestBuildResult.Invalid(
                    "SNAPSHOT_TARGET_MISSING",
                    "The active preset is missing a required sun or moon curve/gradient.");
            }

            float normalizedTime = capturedHour / 24f;
            float sunAzimuth = Mathf.Repeat(preset.sunAzimuthCurve.Evaluate(normalizedTime), 360f);
            float sunElevation = preset.sunElevationCurve.Evaluate(normalizedTime);
            float sunIntensity = preset.sunIntensityCurve.Evaluate(normalizedTime);
            Color sunColor = preset.sunColorGradient.Evaluate(normalizedTime);
            float moonAzimuth = Mathf.Repeat(preset.moonAzimuthCurve.Evaluate(normalizedTime), 360f);
            float moonElevation = preset.moonElevationCurve.Evaluate(normalizedTime);
            float moonIntensity = preset.moonIntensityCurve.Evaluate(normalizedTime);
            Color moonColor = preset.moonColorGradient.Evaluate(normalizedTime);

            if (!IsValidLightSnapshot(sunAzimuth, sunElevation, sunIntensity, sunColor) ||
                !IsValidLightSnapshot(moonAzimuth, moonElevation, moonIntensity, moonColor))
            {
                return WeatherIntentAnalyzeRequestBuildResult.Invalid(
                    "SNAPSHOT_INVALID",
                    "The captured sun or moon snapshot is outside the protocol range.");
            }

            string requestId = Guid.NewGuid().ToString();
            var root = new JObject
            {
                ["request_id"] = requestId,
                ["schema_version"] = DawnTodAiProtocol.SchemaVersion,
                ["pipeline"] = "URP",
                ["user_input"] = userInput,
                ["capabilities"] = new JObject
                {
                    ["supported_non_null_fields"] = new JArray(
                        DawnTodAiProtocol.SupportedNonNullFields)
                },
                ["snapshot"] = new JObject
                {
                    ["time_hour"] = capturedHour,
                    ["sun"] = LightSnapshot(
                        sunAzimuth,
                        sunElevation,
                        sunIntensity,
                        sunColor),
                    ["moon"] = LightSnapshot(
                        moonAzimuth,
                        moonElevation,
                        moonIntensity,
                        moonColor)
                }
            };

            return WeatherIntentAnalyzeRequestBuildResult.Valid(
                new WeatherIntentAnalyzeRequest(
                    requestId,
                    root.ToString(Formatting.None),
                    target));
        }

        private static JObject LightSnapshot(
            float azimuth,
            float elevation,
            float intensity,
            Color color)
        {
            return new JObject
            {
                ["azimuth_deg"] = azimuth,
                ["elevation_deg"] = elevation,
                ["intensity"] = intensity,
                ["color"] = new JObject
                {
                    ["r"] = color.r,
                    ["g"] = color.g,
                    ["b"] = color.b,
                    ["a"] = color.a
                }
            };
        }

        private static bool IsValidLightSnapshot(
            float azimuth,
            float elevation,
            float intensity,
            Color color)
        {
            return IsFinite(azimuth) && azimuth >= 0f && azimuth < 360f &&
                   IsFinite(elevation) && elevation >= -90f && elevation <= 90f &&
                   IsFinite(intensity) && intensity >= 0f && intensity <= 8f &&
                   IsUnit(color.r) && IsUnit(color.g) && IsUnit(color.b) && IsUnit(color.a);
        }

        private static bool IsUnit(float value)
        {
            return IsFinite(value) && value >= 0f && value <= 1f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
