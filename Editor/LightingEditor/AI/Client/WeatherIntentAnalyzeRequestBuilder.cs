using System;
using System.Collections.Generic;
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
        private const float MinimumFogDistance = 0.01f;
        private const float MaximumStarEmission = 1000f;

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

            if (!TryGetPipelineName(target.PipelineKind, out string pipelineName))
            {
                return WeatherIntentAnalyzeRequestBuildResult.Invalid(
                    "PIPELINE_UNSUPPORTED",
                    "The AI assistant requires an active URP or HDRP render pipeline.");
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

            float normalizedTime = target.CapturedHour / 24f;
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

            WeatherPipelineCapabilities capabilities =
                WeatherPipelineCapabilities.ForKind(target.PipelineKind);
            float? starEmission = capabilities.SupportsStarEmission
                ? EvaluateOptionalClampedCurve(
                    preset.starEmissionCurve,
                    normalizedTime,
                    0f,
                    MaximumStarEmission)
                : null;
            float? fogDistance = capabilities.SupportsFog
                ? EvaluateOptionalCurve(preset.fogDistanceCurve, normalizedTime)
                : null;
            float? fogHeight = capabilities.SupportsFog
                ? EvaluateOptionalCurve(preset.fogHeightCurve, normalizedTime)
                : null;
            Color? fogColor = capabilities.SupportsFog
                ? EvaluateOptionalGradient(preset.fogColorGradient, normalizedTime)
                : null;
            float? exposure = capabilities.SupportsExposure
                ? EvaluateOptionalCurve(preset.exposureCompensationCurve, normalizedTime)
                : null;
            float? precipitation = capabilities.SupportsRain
                ? EvaluateOptionalCurve(preset.precipitationAmountCurve, normalizedTime)
                : null;
            float? rainSpeed = capabilities.SupportsRain
                ? EvaluateOptionalCurve(preset.rainySpeedCurve, normalizedTime)
                : null;
            float? rainDensity = capabilities.SupportsRain
                ? EvaluateOptionalCurve(preset.rainDensityCurve, normalizedTime)
                : null;
            float? rainWind = capabilities.SupportsRain
                ? EvaluateOptionalCurve(preset.rainWindZRotationCurve, normalizedTime)
                : null;

            if (!IsOptionalRange(starEmission, 0f, 1000f) ||
                !IsOptionalPositive(fogDistance) ||
                !IsOptionalFinite(fogHeight) ||
                !IsOptionalColor(fogColor) ||
                !IsOptionalFinite(exposure) ||
                !IsOptionalUnit(precipitation) ||
                !IsOptionalNonNegative(rainSpeed) ||
                !IsOptionalNonNegative(rainDensity) ||
                !IsOptionalRange(rainWind, -45f, 45f))
            {
                return WeatherIntentAnalyzeRequestBuildResult.Invalid(
                    "SNAPSHOT_INVALID",
                    "The captured weather snapshot contains a value outside the protocol range.");
            }

            List<string> supportedFields = BuildSupportedFields(capabilities, preset);
            string requestId = Guid.NewGuid().ToString();
            var root = new JObject
            {
                ["request_id"] = requestId,
                ["schema_version"] = DawnTodAiProtocol.SchemaVersion,
                ["pipeline"] = pipelineName,
                ["user_input"] = userInput,
                ["capabilities"] = new JObject
                {
                    ["supported_non_null_fields"] = new JArray(supportedFields)
                },
                ["snapshot"] = new JObject
                {
                    ["time_hour"] = target.CapturedHour,
                    ["sun"] = LightSnapshot(
                        sunAzimuth,
                        sunElevation,
                        sunIntensity,
                        sunColor),
                    ["moon"] = LightSnapshot(
                        moonAzimuth,
                        moonElevation,
                        moonIntensity,
                        moonColor),
                    ["sky"] = new JObject
                    {
                        ["star_emission"] = NumberOrNull(starEmission)
                    },
                    ["fog"] = new JObject
                    {
                        ["mean_free_path_m"] = NumberOrNull(fogDistance),
                        ["base_height_m"] = NumberOrNull(fogHeight),
                        ["color"] = ColorOrNull(fogColor)
                    },
                    ["exposure"] = new JObject
                    {
                        ["compensation_ev"] = NumberOrNull(exposure)
                    },
                    ["rain"] = new JObject
                    {
                        ["enabled"] = preset.rainyEnable,
                        ["precipitation_amount"] = NumberOrNull(precipitation),
                        ["fall_speed"] = NumberOrNull(rainSpeed),
                        ["density"] = NumberOrNull(rainDensity),
                        ["wind_z_rotation_deg"] = NumberOrNull(rainWind)
                    }
                }
            };

            return WeatherIntentAnalyzeRequestBuildResult.Valid(
                new WeatherIntentAnalyzeRequest(
                    requestId,
                    root.ToString(Formatting.None),
                    target));
        }

        private static List<string> BuildSupportedFields(
            WeatherPipelineCapabilities capabilities,
            DawnWeatherPreset preset)
        {
            var fields = new List<string>
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

            if (capabilities.SupportsStarEmission && preset.starEmissionCurve != null)
            {
                fields.Add("sky.star_emission");
            }

            if (capabilities.SupportsFog)
            {
                AddWhenPresent(fields, "fog.mean_free_path_m", preset.fogDistanceCurve);
                AddWhenPresent(fields, "fog.base_height_m", preset.fogHeightCurve);
                AddWhenPresent(fields, "fog.color", preset.fogColorGradient);
            }

            if (capabilities.SupportsExposure && preset.exposureCompensationCurve != null)
            {
                fields.Add("exposure.compensation_ev");
            }

            if (capabilities.SupportsRain)
            {
                fields.Add("rain.enabled");
                AddWhenPresent(fields, "rain.precipitation_amount", preset.precipitationAmountCurve);
                AddWhenPresent(fields, "rain.fall_speed", preset.rainySpeedCurve);
                AddWhenPresent(fields, "rain.density", preset.rainDensityCurve);
                AddWhenPresent(fields, "rain.wind_z_rotation_deg", preset.rainWindZRotationCurve);
            }

            return fields;
        }

        private static void AddWhenPresent(
            ICollection<string> fields,
            string path,
            object target)
        {
            if (target != null)
            {
                fields.Add(path);
            }
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
                ["color"] = ColorSnapshot(color)
            };
        }

        private static JObject ColorSnapshot(Color color)
        {
            return new JObject
            {
                ["r"] = color.r,
                ["g"] = color.g,
                ["b"] = color.b,
                ["a"] = color.a
            };
        }

        private static JToken NumberOrNull(float? value)
        {
            return value.HasValue
                ? new JValue(value.Value)
                : JValue.CreateNull();
        }

        private static JToken ColorOrNull(Color? value)
        {
            return value.HasValue
                ? ColorSnapshot(value.Value)
                : JValue.CreateNull();
        }

        private static float? EvaluateOptionalCurve(AnimationCurve curve, float time)
        {
            return curve != null ? curve.Evaluate(time) : (float?)null;
        }

        private static float? EvaluateOptionalClampedCurve(
            AnimationCurve curve,
            float time,
            float minimum,
            float maximum)
        {
            float? value = EvaluateOptionalCurve(curve, time);
            return value.HasValue && IsFinite(value.Value)
                ? Mathf.Clamp(value.Value, minimum, maximum)
                : value;
        }

        private static Color? EvaluateOptionalGradient(Gradient gradient, float time)
        {
            return gradient != null ? gradient.Evaluate(time) : (Color?)null;
        }

        private static bool IsValidLightSnapshot(
            float azimuth,
            float elevation,
            float intensity,
            Color color)
        {
            return IsFinite(azimuth) && azimuth >= 0f && azimuth < 360f &&
                   IsFinite(elevation) && elevation >= -90f && elevation <= 90f &&
                   IsFinite(intensity) && intensity >= 0f &&
                   IsUnit(color.r) && IsUnit(color.g) && IsUnit(color.b) && IsUnit(color.a);
        }

        private static bool IsOptionalFinite(float? value)
        {
            return !value.HasValue || IsFinite(value.Value);
        }

        private static bool IsOptionalNonNegative(float? value)
        {
            return !value.HasValue || IsFinite(value.Value) && value.Value >= 0f;
        }

        private static bool IsOptionalPositive(float? value)
        {
            return !value.HasValue ||
                   IsFinite(value.Value) && value.Value >= MinimumFogDistance;
        }

        private static bool IsOptionalUnit(float? value)
        {
            return !value.HasValue || IsUnit(value.Value);
        }

        private static bool IsOptionalRange(float? value, float minimum, float maximum)
        {
            return !value.HasValue ||
                   IsFinite(value.Value) && value.Value >= minimum && value.Value <= maximum;
        }

        private static bool IsOptionalColor(Color? value)
        {
            return !value.HasValue ||
                   IsUnit(value.Value.r) &&
                   IsUnit(value.Value.g) &&
                   IsUnit(value.Value.b) &&
                   IsUnit(value.Value.a);
        }

        private static bool IsUnit(float value)
        {
            return IsFinite(value) && value >= 0f && value <= 1f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool TryGetPipelineName(
            WeatherRenderPipelineKind pipelineKind,
            out string pipelineName)
        {
            switch (pipelineKind)
            {
                case WeatherRenderPipelineKind.Universal:
                    pipelineName = "URP";
                    return true;
                case WeatherRenderPipelineKind.HighDefinition:
                    pipelineName = "HDRP";
                    return true;
                default:
                    pipelineName = null;
                    return false;
            }
        }
    }
}
