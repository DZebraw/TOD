using System;
using System.Collections.Generic;
using UnityEngine;

namespace DawnTOD
{
    internal readonly struct WeatherSampleContribution
    {
        public int SourceIndex { get; }
        public WeatherSample Sample { get; }
        public float Weight { get; }

        public WeatherSampleContribution(
            int sourceIndex,
            WeatherSample sample,
            float weight)
        {
            SourceIndex = sourceIndex;
            Sample = sample;
            Weight = weight;
        }
    }

    internal readonly struct WeatherBlendResult
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
        public int DominantSourceIndex { get; }
        public bool HasPrecipitation =>
            PrecipitationAmount > WeatherBlender.PrecipitationEpsilon;

        public WeatherBlendResult(
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
            float rainWindZRotation,
            int dominantSourceIndex)
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
            DominantSourceIndex = dominantSourceIndex;
        }
    }

    internal static class WeatherBlender
    {
        internal const float MinimumFogDistance = 0.01f;
        internal const float PrecipitationEpsilon = 0.000001f;

        private const double DirectionMagnitudeEpsilonSquared = 0.000000000001d;
        private const double PositiveWeightEpsilon = 0.000001d;
        private const double DegreesToRadians = Math.PI / 180d;
        private const double RadiansToDegrees = 180d / Math.PI;

        public static bool TryBlend(
            IReadOnlyList<WeatherSampleContribution> contributions,
            out WeatherBlendResult result)
        {
            result = default;
            if (contributions == null)
            {
                return false;
            }

            double totalWeight = 0d;
            float dominantWeight = 0f;
            int dominantSourceIndex = 0;
            bool hasDominant = false;

            for (int index = 0; index < contributions.Count; index++)
            {
                WeatherSampleContribution contribution = contributions[index];
                if (!IsPositiveFinite(contribution.Weight))
                {
                    continue;
                }

                totalWeight += contribution.Weight;
                if (!hasDominant || contribution.Weight > dominantWeight)
                {
                    dominantWeight = contribution.Weight;
                    dominantSourceIndex = contribution.SourceIndex;
                    hasDominant = true;
                }
            }

            if (!hasDominant || totalWeight <= PositiveWeightEpsilon)
            {
                return false;
            }

            double sunDirectionX = 0d;
            double sunDirectionY = 0d;
            double sunDirectionZ = 0d;
            double moonDirectionX = 0d;
            double moonDirectionY = 0d;
            double moonDirectionZ = 0d;
            Vector3 dominantSunDirection = Vector3.forward;
            Vector3 dominantMoonDirection = Vector3.forward;
            float dominantSunDirectionWeight = 0f;
            float dominantMoonDirectionWeight = 0f;
            bool hasDominantSunDirection = false;
            bool hasDominantMoonDirection = false;

            double sunIntensity = 0d;
            double sunColorR = 0d;
            double sunColorG = 0d;
            double sunColorB = 0d;
            double moonIntensity = 0d;
            double moonColorR = 0d;
            double moonColorG = 0d;
            double moonColorB = 0d;
            double starEmission = 0d;
            double fogExtinction = 0d;
            double fogHeight = 0d;
            double fogColorR = 0d;
            double fogColorG = 0d;
            double fogColorB = 0d;
            double exposureCompensation = 0d;
            double precipitationAmount = 0d;

            double rainContributionWeight = 0d;
            double rainSpeed = 0d;
            double rainBaseDensity = 0d;
            double rainWindX = 0d;
            double rainWindY = 0d;
            double dominantRainWeight = 0d;
            double dominantRainAngle = 0d;
            bool hasDominantRain = false;

            for (int index = 0; index < contributions.Count; index++)
            {
                WeatherSampleContribution contribution = contributions[index];
                if (!IsPositiveFinite(contribution.Weight))
                {
                    continue;
                }

                double normalizedWeight = contribution.Weight / totalWeight;
                WeatherSample sample = contribution.Sample;

                if (TryNormalizeDirection(sample.SunDirection, out Vector3 sunDirection))
                {
                    sunDirectionX += sunDirection.x * normalizedWeight;
                    sunDirectionY += sunDirection.y * normalizedWeight;
                    sunDirectionZ += sunDirection.z * normalizedWeight;
                    if (!hasDominantSunDirection ||
                        contribution.Weight > dominantSunDirectionWeight)
                    {
                        dominantSunDirection = sunDirection;
                        dominantSunDirectionWeight = contribution.Weight;
                        hasDominantSunDirection = true;
                    }
                }

                if (TryNormalizeDirection(sample.MoonDirection, out Vector3 moonDirection))
                {
                    moonDirectionX += moonDirection.x * normalizedWeight;
                    moonDirectionY += moonDirection.y * normalizedWeight;
                    moonDirectionZ += moonDirection.z * normalizedWeight;
                    if (!hasDominantMoonDirection ||
                        contribution.Weight > dominantMoonDirectionWeight)
                    {
                        dominantMoonDirection = moonDirection;
                        dominantMoonDirectionWeight = contribution.Weight;
                        hasDominantMoonDirection = true;
                    }
                }

                sunIntensity += SanitizeNonNegative(sample.SunIntensity) * normalizedWeight;
                AccumulateColor(
                    sample.SunColor,
                    normalizedWeight,
                    ref sunColorR,
                    ref sunColorG,
                    ref sunColorB);
                moonIntensity += SanitizeNonNegative(sample.MoonIntensity) * normalizedWeight;
                AccumulateColor(
                    sample.MoonColor,
                    normalizedWeight,
                    ref moonColorR,
                    ref moonColorG,
                    ref moonColorB);
                starEmission += SanitizeNonNegative(sample.StarEmission) * normalizedWeight;

                double fogDistance = SanitizeFogDistance(sample.FogDistance);
                fogExtinction += normalizedWeight / fogDistance;
                fogHeight += SanitizeFinite(sample.FogHeight) * normalizedWeight;
                AccumulateColor(
                    sample.FogColor,
                    normalizedWeight,
                    ref fogColorR,
                    ref fogColorG,
                    ref fogColorB);
                exposureCompensation +=
                    SanitizeFinite(sample.ExposureCompensation) * normalizedWeight;

                double samplePrecipitation = SanitizeUnit(sample.PrecipitationAmount);
                precipitationAmount += samplePrecipitation * normalizedWeight;
                double sampleRainWeight = normalizedWeight * samplePrecipitation;
                if (sampleRainWeight <= 0d)
                {
                    continue;
                }

                rainContributionWeight += sampleRainWeight;
                rainSpeed += SanitizeNonNegative(sample.RainSpeed) * sampleRainWeight;
                rainBaseDensity +=
                    SanitizeNonNegative(sample.RainDensity) * sampleRainWeight;

                double rainAngle = SanitizeFinite(sample.RainWindZRotation);
                double rainAngleRadians = rainAngle * DegreesToRadians;
                rainWindX += Math.Cos(rainAngleRadians) * sampleRainWeight;
                rainWindY += Math.Sin(rainAngleRadians) * sampleRainWeight;
                if (!hasDominantRain || sampleRainWeight > dominantRainWeight)
                {
                    dominantRainWeight = sampleRainWeight;
                    dominantRainAngle = rainAngle;
                    hasDominantRain = true;
                }
            }

            Vector3 blendedSunDirection = ResolveDirection(
                sunDirectionX,
                sunDirectionY,
                sunDirectionZ,
                dominantSunDirection,
                hasDominantSunDirection);
            Vector3 blendedMoonDirection = ResolveDirection(
                moonDirectionX,
                moonDirectionY,
                moonDirectionZ,
                dominantMoonDirection,
                hasDominantMoonDirection);

            double blendedFogDistance = fogExtinction > 0d
                ? 1d / fogExtinction
                : MinimumFogDistance;
            double clampedPrecipitation = Clamp01(precipitationAmount);

            double blendedRainSpeed = 0d;
            double outputRainDensity = 0d;
            double blendedRainWind = 0d;
            if (rainContributionWeight > PositiveWeightEpsilon)
            {
                blendedRainSpeed = rainSpeed / rainContributionWeight;
                double blendedRainDensity = rainBaseDensity / rainContributionWeight;
                outputRainDensity = blendedRainDensity * clampedPrecipitation;

                double rainDirectionMagnitudeSquared =
                    rainWindX * rainWindX + rainWindY * rainWindY;
                blendedRainWind = rainDirectionMagnitudeSquared >
                    DirectionMagnitudeEpsilonSquared
                    ? Math.Atan2(rainWindY, rainWindX) * RadiansToDegrees
                    : dominantRainAngle;
            }

            result = new WeatherBlendResult(
                blendedSunDirection,
                ToFiniteNonNegativeFloat(sunIntensity),
                CreateColor(sunColorR, sunColorG, sunColorB),
                blendedMoonDirection,
                ToFiniteNonNegativeFloat(moonIntensity),
                CreateColor(moonColorR, moonColorG, moonColorB),
                ToFiniteNonNegativeFloat(starEmission),
                Math.Max(MinimumFogDistance, ToFiniteNonNegativeFloat(blendedFogDistance)),
                ToFiniteFloat(fogHeight),
                CreateColor(fogColorR, fogColorG, fogColorB),
                ToFiniteFloat(exposureCompensation),
                (float)clampedPrecipitation,
                ToFiniteNonNegativeFloat(blendedRainSpeed),
                ToFiniteNonNegativeFloat(outputRainDensity),
                NormalizeAngle(ToFiniteFloat(blendedRainWind)),
                dominantSourceIndex);
            return true;
        }

        private static bool IsPositiveFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   value > PositiveWeightEpsilon;
        }

        private static bool TryNormalizeDirection(Vector3 direction, out Vector3 normalized)
        {
            normalized = Vector3.forward;
            if (!IsFinite(direction.x) ||
                !IsFinite(direction.y) ||
                !IsFinite(direction.z))
            {
                return false;
            }

            double x = direction.x;
            double y = direction.y;
            double z = direction.z;
            double magnitudeSquared = x * x + y * y + z * z;
            if (magnitudeSquared <= DirectionMagnitudeEpsilonSquared ||
                double.IsNaN(magnitudeSquared) ||
                double.IsInfinity(magnitudeSquared))
            {
                return false;
            }

            double inverseMagnitude = 1d / Math.Sqrt(magnitudeSquared);
            normalized = new Vector3(
                (float)(x * inverseMagnitude),
                (float)(y * inverseMagnitude),
                (float)(z * inverseMagnitude));
            return true;
        }

        private static Vector3 ResolveDirection(
            double x,
            double y,
            double z,
            Vector3 dominantDirection,
            bool hasDominantDirection)
        {
            double magnitudeSquared = x * x + y * y + z * z;
            if (magnitudeSquared > DirectionMagnitudeEpsilonSquared &&
                !double.IsNaN(magnitudeSquared) &&
                !double.IsInfinity(magnitudeSquared))
            {
                double inverseMagnitude = 1d / Math.Sqrt(magnitudeSquared);
                return new Vector3(
                    (float)(x * inverseMagnitude),
                    (float)(y * inverseMagnitude),
                    (float)(z * inverseMagnitude));
            }

            return hasDominantDirection ? dominantDirection : Vector3.forward;
        }

        private static void AccumulateColor(
            Color color,
            double weight,
            ref double red,
            ref double green,
            ref double blue)
        {
            red += SanitizeNonNegative(color.r) * weight;
            green += SanitizeNonNegative(color.g) * weight;
            blue += SanitizeNonNegative(color.b) * weight;
        }

        private static Color CreateColor(double red, double green, double blue)
        {
            return new Color(
                ToFiniteNonNegativeFloat(red),
                ToFiniteNonNegativeFloat(green),
                ToFiniteNonNegativeFloat(blue),
                1f);
        }

        private static double SanitizeFogDistance(float value)
        {
            return IsFinite(value) && value >= MinimumFogDistance
                ? value
                : MinimumFogDistance;
        }

        private static double SanitizeNonNegative(float value)
        {
            return IsFinite(value) && value > 0f ? value : 0d;
        }

        private static double SanitizeFinite(float value)
        {
            return IsFinite(value) ? value : 0d;
        }

        private static double SanitizeUnit(float value)
        {
            return IsFinite(value) ? Clamp01(value) : 0d;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static double Clamp01(double value)
        {
            if (value <= 0d)
            {
                return 0d;
            }

            return value >= 1d ? 1d : value;
        }

        private static float ToFiniteNonNegativeFloat(double value)
        {
            if (double.IsNaN(value) || value <= 0d)
            {
                return 0f;
            }

            return value >= float.MaxValue ? float.MaxValue : (float)value;
        }

        private static float ToFiniteFloat(double value)
        {
            if (double.IsNaN(value))
            {
                return 0f;
            }

            if (value >= float.MaxValue)
            {
                return float.MaxValue;
            }

            return value <= -float.MaxValue ? -float.MaxValue : (float)value;
        }

        private static float NormalizeAngle(float degrees)
        {
            if (!IsFinite(degrees))
            {
                return 0f;
            }

            double normalized = degrees % 360d;
            if (normalized > 180d)
            {
                normalized -= 360d;
            }
            else if (normalized <= -180d)
            {
                normalized += 360d;
            }

            return (float)normalized;
        }
    }
}
