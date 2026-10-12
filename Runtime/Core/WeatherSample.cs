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
        public float FogDensity { get; }
        public float FogMaxOpacity { get; }
        public float FogStartDistance { get; }
        public float FogEndDistance { get; }
        public float FogCutoffDistance { get; }
        public float FogHeightFalloff { get; }
        public float SecondFogDensity { get; }
        public float SecondFogHeightFalloff { get; }
        public float SecondFogHeightOffset { get; }
        public Color FogColor { get; }
        public Color FogSkyAtmosphereAmbientContributionColorScale { get; }
        public float FogHeightFogContribution { get; }
        public Color FogDirectionalInscatteringColor { get; }
        public float FogDirectionalInscatteringExponent { get; }
        public float FogDirectionalInscatteringStartDistance { get; }
        public DawnVolumetricFogSettings VolumetricFog { get; }
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
            float fogDensity,
            Color fogColor,
            float exposureCompensation,
            float precipitationAmount,
            float rainSpeed,
            float rainDensity,
            float rainWindZRotation,
            float fogHeightFalloff = 0.2f,
            float fogMaxOpacity = 1f,
            float fogStartDistance = 0f,
            float fogEndDistance = 0f,
            float fogCutoffDistance = 0f,
            float secondFogDensity = 0f,
            float secondFogHeightFalloff = 0.2f,
            float secondFogHeightOffset = 0f,
            Color? fogSkyAtmosphereAmbientContributionColorScale = null,
            float fogHeightFogContribution = 1f,
            Color? fogDirectionalInscatteringColor = null,
            float fogDirectionalInscatteringExponent = 4f,
            float fogDirectionalInscatteringStartDistance = 100f,
            DawnVolumetricFogSettings? volumetricFog = null)
        {
            SunDirection = sunDirection;
            SunIntensity = sunIntensity;
            SunColor = sunColor;
            MoonDirection = moonDirection;
            MoonIntensity = moonIntensity;
            MoonColor = moonColor;
            StarEmission = starEmission;
            FogDensity = fogDensity;
            FogMaxOpacity = fogMaxOpacity;
            FogStartDistance = fogStartDistance;
            FogEndDistance = fogEndDistance;
            FogCutoffDistance = fogCutoffDistance;
            FogHeightFalloff = fogHeightFalloff;
            SecondFogDensity = secondFogDensity;
            SecondFogHeightFalloff = secondFogHeightFalloff;
            SecondFogHeightOffset = secondFogHeightOffset;
            FogColor = fogColor;
            FogSkyAtmosphereAmbientContributionColorScale = fogSkyAtmosphereAmbientContributionColorScale ?? Color.white;
            FogHeightFogContribution = fogHeightFogContribution;
            FogDirectionalInscatteringColor = fogDirectionalInscatteringColor ?? Color.black;
            FogDirectionalInscatteringExponent = fogDirectionalInscatteringExponent;
            FogDirectionalInscatteringStartDistance = fogDirectionalInscatteringStartDistance;
            VolumetricFog = (volumetricFog ?? DawnVolumetricFogSettings.Default).Sanitized();
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
                Mathf.Clamp(preset.fogDensity, 0f, 10f),
                preset.fogColor,
                preset.exposureCompensationCurve.Evaluate(time),
                preset.rainyEnable
                    ? Mathf.Clamp01(preset.precipitationAmountCurve?.Evaluate(time) ?? 1f)
                    : 0f,
                preset.rainySpeedCurve?.Evaluate(time) ?? DefaultRainSpeed,
                preset.rainDensityCurve?.Evaluate(time) ?? DefaultRainDensity,
                preset.rainWindZRotationCurve?.Evaluate(time) ?? DefaultRainWindZRotation,
                Mathf.Clamp(preset.fogHeightFalloff, 0f, 2f),
                Mathf.Clamp01(preset.fogMaxOpacity),
                Mathf.Max(0f, preset.fogStartDistance),
                Mathf.Max(0f, preset.fogEndDistance),
                Mathf.Max(0f, preset.fogCutoffDistance),
                preset.secondFogEnabled ? Mathf.Clamp(preset.secondFogDensity, 0f, 10f) : 0f,
                Mathf.Clamp(preset.secondFogHeightFalloff, 0f, 2f),
                preset.secondFogHeightOffset,
                preset.fogSkyAtmosphereAmbientContributionColorScale,
                preset.fogAtmosphereLighting ? Mathf.Max(0f, preset.fogHeightFogContribution) : 0f,
                preset.fogDirectionalInscatteringColor,
                Mathf.Clamp(preset.fogDirectionalInscatteringExponent, 0.000001f, 1000f),
                Mathf.Max(0f, preset.fogDirectionalInscatteringStartDistance),
                preset.volumetricFog);
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
                   preset.exposureCompensationCurve != null;
        }
    }
}
