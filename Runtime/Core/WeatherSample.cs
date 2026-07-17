using UnityEngine;

namespace DawnTOD
{
    internal readonly struct WeatherSample
    {
        public Vector3 SunDirection { get; }
        public float SunIntensity { get; }
        public Color SunColor { get; }
        public Vector3 MoonDirection { get; }
        public float MoonIntensity { get; }
        public Color MoonColor { get; }
        public float StarEmission { get; }
        public float FogDistance { get; }
        public float FogHeight { get; }
        public Color FogColor { get; }
        public float ExposureCompensation { get; }
        public float PrecipitationAmount { get; }
        public float RainSpeed { get; }
        public float RainDensity { get; }
        public float RainWindZRotation { get; }

        public WeatherSample(
            Vector3 sunDirection,
            float sunIntensity,
            Color sunColor,
            Vector3 moonDirection,
            float moonIntensity,
            Color moonColor,
            float starEmission,
            float fogDistance,
            float fogHeight,
            Color fogColor,
            float exposureCompensation,
            float precipitationAmount,
            float rainSpeed,
            float rainDensity,
            float rainWindZRotation)
        {
            SunDirection = sunDirection;
            SunIntensity = sunIntensity;
            SunColor = sunColor;
            MoonDirection = moonDirection;
            MoonIntensity = moonIntensity;
            MoonColor = moonColor;
            StarEmission = starEmission;
            FogDistance = fogDistance;
            FogHeight = fogHeight;
            FogColor = fogColor;
            ExposureCompensation = exposureCompensation;
            PrecipitationAmount = precipitationAmount;
            RainSpeed = rainSpeed;
            RainDensity = rainDensity;
            RainWindZRotation = rainWindZRotation;
        }
    }

    internal static class WeatherPresetSampler
    {
        private const float DefaultRainSpeed = 1f;
        private const float DefaultRainDensity = 1f;
        private const float DefaultRainWindZRotation = 0f;

        public static bool TrySample(
            DawnWeatherPreset preset,
            float normalizedTime,
            out WeatherSample sample)
        {
            sample = default;
            if (!HasRequiredData(preset))
            {
                return false;
            }

            float time = Mathf.Clamp01(normalizedTime);
            Quaternion sunRotation = preset.SampleSunRotation(time);
            Quaternion moonRotation = preset.SampleMoonRotation(time);

            sample = new WeatherSample(
                sunRotation * Vector3.forward,
                preset.sunIntensityCurve.Evaluate(time),
                preset.sunColorGradient.Evaluate(time),
                moonRotation * Vector3.forward,
                preset.moonIntensityCurve.Evaluate(time),
                preset.moonColorGradient.Evaluate(time),
                preset.starEmissionCurve.Evaluate(time),
                preset.fogDistanceCurve.Evaluate(time),
                preset.fogHeightCurve.Evaluate(time),
                preset.fogColorGradient.Evaluate(time),
                preset.exposureCompensationCurve.Evaluate(time),
                preset.rainyEnable
                    ? Mathf.Clamp01(preset.precipitationAmountCurve?.Evaluate(time) ?? 1f)
                    : 0f,
                preset.rainySpeedCurve?.Evaluate(time) ?? DefaultRainSpeed,
                preset.rainDensityCurve?.Evaluate(time) ?? DefaultRainDensity,
                preset.rainWindZRotationCurve?.Evaluate(time) ?? DefaultRainWindZRotation);
            return true;
        }

        private static bool HasRequiredData(DawnWeatherPreset preset)
        {
            return preset != null &&
                   preset.sunAzimuthCurve != null &&
                   preset.sunElevationCurve != null &&
                   preset.sunIntensityCurve != null &&
                   preset.sunColorGradient != null &&
                   preset.moonAzimuthCurve != null &&
                   preset.moonElevationCurve != null &&
                   preset.moonIntensityCurve != null &&
                   preset.moonColorGradient != null &&
                   preset.starEmissionCurve != null &&
                   preset.fogDistanceCurve != null &&
                   preset.fogHeightCurve != null &&
                   preset.fogColorGradient != null &&
                   preset.exposureCompensationCurve != null;
        }
    }
}
