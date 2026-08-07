using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DawnTODEditor.AI
{
    public sealed class WeatherIntentParseResult
    {
        public bool IsValid => Patch != null;
        public WeatherIntentPatch Patch { get; }
        public string ErrorCode { get; }
        public string ErrorPath { get; }
        public string ErrorMessage { get; }

        private WeatherIntentParseResult(
            WeatherIntentPatch patch,
            string errorCode,
            string errorPath,
            string errorMessage)
        {
            Patch = patch;
            ErrorCode = errorCode;
            ErrorPath = errorPath;
            ErrorMessage = errorMessage;
        }

        internal static WeatherIntentParseResult Success(WeatherIntentPatch patch)
        {
            return new WeatherIntentParseResult(patch, null, null, null);
        }

        internal static WeatherIntentParseResult Failure(
            string errorCode,
            string errorPath,
            string errorMessage)
        {
            return new WeatherIntentParseResult(null, errorCode, errorPath, errorMessage);
        }
    }

    public static class WeatherIntentJsonParser
    {
        private static readonly string[] RootFields =
        {
            "schema_version", "time", "sun", "moon", "sky", "fog", "exposure", "rain"
        };

        private static readonly string[] TimeFields = { "mode", "hour" };
        private static readonly string[] LightFields = { "azimuth_deg", "elevation_deg", "intensity", "color" };
        private static readonly string[] ColorFields = { "r", "g", "b", "a" };
        private static readonly string[] SkyFields = { "star_emission" };
        private static readonly string[] FogFields = { "mean_free_path_m", "base_height_m", "color" };
        private static readonly string[] ExposureFields = { "compensation_ev" };
        private static readonly string[] RainFields =
        {
            "enabled", "precipitation_amount", "fall_speed", "density", "wind_z_rotation_deg"
        };

        public static WeatherIntentParseResult Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return WeatherIntentParseResult.Failure("EMPTY_JSON", "$", "Response JSON is empty.");
            }

            try
            {
                JToken token = JToken.Parse(
                    json,
                    new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Load,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Ignore
                    });

                if (ContainsComment(token))
                {
                    throw ValidationFailure.Invalid("MALFORMED_JSON", "$", "JSON comments are not allowed.");
                }

                JObject root = RequireObject(token, "$", "The response root must be an object.");
                ValidateShape(root, "$", RootFields);

                string schemaVersion = ReadString(root["schema_version"], "$.schema_version");
                if (!string.Equals(schemaVersion, WeatherIntentPatch.SupportedSchemaVersion, StringComparison.Ordinal))
                {
                    throw ValidationFailure.Invalid(
                        "UNSUPPORTED_SCHEMA_VERSION",
                        "$.schema_version",
                        $"Schema version '{schemaVersion}' is not supported.");
                }

                WeatherIntentTimePatch time = ParseTime(root["time"]);
                WeatherIntentLightPatch sun = ParseLight(root["sun"], "$.sun");
                WeatherIntentLightPatch moon = ParseLight(root["moon"], "$.moon");
                WeatherIntentSkyPatch sky = ParseSky(root["sky"]);
                WeatherIntentFogPatch fog = ParseFog(root["fog"]);
                WeatherIntentExposurePatch exposure = ParseExposure(root["exposure"]);
                WeatherIntentRainPatch rain = ParseRain(root["rain"]);

                return WeatherIntentParseResult.Success(
                    new WeatherIntentPatch(
                        schemaVersion,
                        time,
                        sun,
                        moon,
                        sky,
                        fog,
                        exposure,
                        rain));
            }
            catch (ValidationFailure exception)
            {
                return WeatherIntentParseResult.Failure(
                    exception.Code,
                    exception.JsonPath,
                    exception.Message);
            }
            catch (JsonException exception)
            {
                return WeatherIntentParseResult.Failure("MALFORMED_JSON", "$", exception.Message);
            }
        }

        private static WeatherIntentTimePatch ParseTime(JToken token)
        {
            const string path = "$.time";
            JObject time = RequireObject(token, path, "Time must be an object.");
            ValidateShape(time, path, TimeFields);

            string modeValue = ReadString(time["mode"], path + ".mode");
            JToken hourToken = time["hour"];

            if (string.Equals(modeValue, "current", StringComparison.Ordinal))
            {
                RequireNull(hourToken, path + ".hour", "Current time mode requires a null hour.", "INVALID_VALUE");
                return new WeatherIntentTimePatch(WeatherIntentTimeMode.Current, null);
            }

            if (string.Equals(modeValue, "explicit", StringComparison.Ordinal))
            {
                float hour = ReadNumber(hourToken, path + ".hour", 0d, true, 24d, false, false).Value;
                return new WeatherIntentTimePatch(WeatherIntentTimeMode.Explicit, hour);
            }

            throw ValidationFailure.Invalid(
                "INVALID_VALUE",
                path + ".mode",
                "Time mode must be either 'current' or 'explicit'.");
        }

        private static WeatherIntentLightPatch ParseLight(JToken token, string path)
        {
            JObject light = RequireObject(token, path, "Light patch must be an object.");
            ValidateShape(light, path, LightFields);

            float? azimuth = ReadNumber(
                light["azimuth_deg"], path + ".azimuth_deg", 0d, true, 360d, false, true);
            float? elevation = ReadNumber(
                light["elevation_deg"], path + ".elevation_deg", -90d, true, 90d, true, true);
            float? intensity = ReadNumber(
                light["intensity"], path + ".intensity", 0d, true, float.MaxValue, true, true);
            WeatherIntentColor color = ParseColor(light["color"], path + ".color");

            return new WeatherIntentLightPatch(azimuth, elevation, intensity, color);
        }

        private static WeatherIntentColor ParseColor(JToken token, string path)
        {
            if (token.Type == JTokenType.Null)
            {
                return null;
            }

            JObject color = RequireObject(token, path, "Color must be null or an RGBA object.");
            ValidateShape(color, path, ColorFields);

            float r = ReadNumber(color["r"], path + ".r", 0d, true, 1d, true, false).Value;
            float g = ReadNumber(color["g"], path + ".g", 0d, true, 1d, true, false).Value;
            float b = ReadNumber(color["b"], path + ".b", 0d, true, 1d, true, false).Value;
            float a = ReadNumber(color["a"], path + ".a", 0d, true, 1d, true, false).Value;
            return new WeatherIntentColor(r, g, b, a);
        }

        private static WeatherIntentSkyPatch ParseSky(JToken token)
        {
            const string path = "$.sky";
            JObject sky = RequireObject(token, path, "Sky patch must be an object.");
            ValidateShape(sky, path, SkyFields);
            return new WeatherIntentSkyPatch(
                ReadNumber(
                    sky["star_emission"],
                    path + ".star_emission",
                    0d,
                    true,
                    1000d,
                    true,
                    true));
        }

        private static WeatherIntentFogPatch ParseFog(JToken token)
        {
            const string path = "$.fog";
            JObject fog = RequireObject(token, path, "Fog patch must be an object.");
            ValidateShape(fog, path, FogFields);
            float? meanFreePath = ReadNumber(
                fog["mean_free_path_m"],
                path + ".mean_free_path_m",
                0.01d,
                true,
                float.MaxValue,
                true,
                true);
            float? baseHeight = ReadNumber(
                fog["base_height_m"],
                path + ".base_height_m",
                -float.MaxValue,
                true,
                float.MaxValue,
                true,
                true);
            WeatherIntentColor color = ParseColor(fog["color"], path + ".color");
            return new WeatherIntentFogPatch(meanFreePath, baseHeight, color);
        }

        private static WeatherIntentExposurePatch ParseExposure(JToken token)
        {
            const string path = "$.exposure";
            JObject exposure = RequireObject(token, path, "Exposure patch must be an object.");
            ValidateShape(exposure, path, ExposureFields);
            return new WeatherIntentExposurePatch(
                ReadNumber(
                    exposure["compensation_ev"],
                    path + ".compensation_ev",
                    -float.MaxValue,
                    true,
                    float.MaxValue,
                    true,
                    true));
        }

        private static WeatherIntentRainPatch ParseRain(JToken token)
        {
            const string path = "$.rain";
            JObject rain = RequireObject(token, path, "Rain patch must be an object.");
            ValidateShape(rain, path, RainFields);
            return new WeatherIntentRainPatch(
                ReadBoolean(rain["enabled"], path + ".enabled", true),
                ReadNumber(
                    rain["precipitation_amount"],
                    path + ".precipitation_amount",
                    0d,
                    true,
                    1d,
                    true,
                    true),
                ReadNumber(
                    rain["fall_speed"],
                    path + ".fall_speed",
                    0d,
                    true,
                    float.MaxValue,
                    true,
                    true),
                ReadNumber(
                    rain["density"],
                    path + ".density",
                    0d,
                    true,
                    float.MaxValue,
                    true,
                    true),
                ReadNumber(
                    rain["wind_z_rotation_deg"],
                    path + ".wind_z_rotation_deg",
                    -45d,
                    true,
                    45d,
                    true,
                    true));
        }

        private static void ValidateShape(JObject value, string path, IReadOnlyList<string> expectedFields)
        {
            var expected = new HashSet<string>(expectedFields, StringComparer.Ordinal);

            for (int i = 0; i < expectedFields.Count; i++)
            {
                string field = expectedFields[i];
                if (value.Property(field, StringComparison.Ordinal) == null)
                {
                    throw ValidationFailure.Invalid(
                        "MISSING_FIELD",
                        path + "." + field,
                        "Required field is missing.");
                }
            }

            foreach (JProperty property in value.Properties())
            {
                if (!expected.Contains(property.Name))
                {
                    throw ValidationFailure.Invalid(
                        "UNEXPECTED_FIELD",
                        path + "." + property.Name,
                        "Unexpected field is not allowed.");
                }
            }
        }

        private static JObject RequireObject(JToken token, string path, string message)
        {
            if (token == null || token.Type != JTokenType.Object)
            {
                throw ValidationFailure.Invalid("INVALID_TYPE", path, message);
            }

            return (JObject)token;
        }

        private static string ReadString(JToken token, string path)
        {
            if (token == null || token.Type != JTokenType.String)
            {
                throw ValidationFailure.Invalid("INVALID_TYPE", path, "Expected a string.");
            }

            return token.Value<string>();
        }

        private static float? ReadNumber(
            JToken token,
            string path,
            double minimum,
            bool minimumInclusive,
            double maximum,
            bool maximumInclusive,
            bool nullable)
        {
            if (token == null)
            {
                throw ValidationFailure.Invalid("MISSING_FIELD", path, "Required field is missing.");
            }

            if (token.Type == JTokenType.Null)
            {
                if (nullable)
                {
                    return null;
                }

                throw ValidationFailure.Invalid("INVALID_TYPE", path, "Expected a number.");
            }

            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                throw ValidationFailure.Invalid("INVALID_TYPE", path, "Expected a number or null.");
            }

            double value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw ValidationFailure.Invalid("INVALID_NUMBER", path, "Number must be finite.");
            }

            bool belowMinimum = minimumInclusive ? value < minimum : value <= minimum;
            bool aboveMaximum = maximumInclusive ? value > maximum : value >= maximum;
            if (belowMinimum || aboveMaximum)
            {
                throw ValidationFailure.Invalid("OUT_OF_RANGE", path, "Number is outside the supported range.");
            }

            float converted = (float)value;
            if (float.IsNaN(converted) || float.IsInfinity(converted))
            {
                throw ValidationFailure.Invalid("INVALID_NUMBER", path, "Number must fit in a finite Unity float.");
            }

            return converted;
        }

        private static bool? ReadBoolean(JToken token, string path, bool nullable)
        {
            if (token == null)
            {
                throw ValidationFailure.Invalid("MISSING_FIELD", path, "Required field is missing.");
            }

            if (token.Type == JTokenType.Null)
            {
                if (nullable)
                {
                    return null;
                }

                throw ValidationFailure.Invalid("INVALID_TYPE", path, "Expected a boolean.");
            }

            if (token.Type != JTokenType.Boolean)
            {
                throw ValidationFailure.Invalid("INVALID_TYPE", path, "Expected a boolean or null.");
            }

            return token.Value<bool>();
        }

        private static void RequireNull(JToken token, string path, string message, string code)
        {
            if (token == null || token.Type != JTokenType.Null)
            {
                throw ValidationFailure.Invalid(code, path, message);
            }
        }

        private static bool ContainsComment(JToken token)
        {
            if (token.Type == JTokenType.Comment)
            {
                return true;
            }

            foreach (JToken child in token.Children())
            {
                if (ContainsComment(child))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class ValidationFailure : Exception
        {
            public string Code { get; }
            public string JsonPath { get; }

            private ValidationFailure(string code, string jsonPath, string message)
                : base(message)
            {
                Code = code;
                JsonPath = jsonPath;
            }

            public static ValidationFailure Invalid(string code, string jsonPath, string message)
            {
                return new ValidationFailure(code, jsonPath, message);
            }
        }
    }
}
