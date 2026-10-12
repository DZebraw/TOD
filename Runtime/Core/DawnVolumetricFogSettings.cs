using System;
using UnityEngine;

namespace DawnTOD
{
    [Serializable]
    public struct DawnVolumetricFogSettings
    {
        public bool enabled;
        [Range(-0.9f, 0.9f)] public float scatteringDistribution;
        [ColorUsage(false)] public Color albedo;
        [ColorUsage(false, true)] public Color emissive;
        [Min(0f)] public float extinctionScale;
        [Tooltip("UE View Distance. Default 6000 cm = 60 m.")]
        [Min(0f)] public float viewDistance;
        [Min(0f)] public float startDistance;
        [Min(0f)] public float nearFadeInDistance;
        [Min(0f)] public float staticLightingScatteringIntensity;
        public bool overrideLightColorsWithFogInscatteringColors;
        [Tooltip("UE r.VolumetricFog.GridPixelSize, before scalability overrides.")]
        [Range(4, 64)] public int gridPixelSize;
        [Range(16, 256)] public int gridSizeZ;
        public bool temporalReprojection;
        [Range(0f, 0.99f)] public float historyWeight;

        // ExponentialHeightFogComponent.cpp and VolumetricFog.cpp (UE 5.6).
        public static DawnVolumetricFogSettings Default => new DawnVolumetricFogSettings
        {
            enabled = false,
            scatteringDistribution = 0.2f,
            albedo = Color.white,
            emissive = Color.black,
            extinctionScale = 1f,
            viewDistance = 60f,
            startDistance = 0f,
            nearFadeInDistance = 0f,
            staticLightingScatteringIntensity = 1f,
            overrideLightColorsWithFogInscatteringColors = false,
            gridPixelSize = 16,
            gridSizeZ = 64,
            temporalReprojection = true,
            historyWeight = 0.9f
        };

        public DawnVolumetricFogSettings Sanitized()
        {
            var value = this;
            value.scatteringDistribution = Mathf.Clamp(Finite(scatteringDistribution), -0.9f, 0.9f);
            value.albedo = new Color(Mathf.Clamp01(Finite(albedo.r)), Mathf.Clamp01(Finite(albedo.g)), Mathf.Clamp01(Finite(albedo.b)), 1f);
            value.emissive = Positive(emissive);
            value.extinctionScale = Positive(extinctionScale);
            value.viewDistance = Positive(viewDistance);
            value.startDistance = Positive(startDistance);
            value.nearFadeInDistance = Positive(nearFadeInDistance);
            value.staticLightingScatteringIntensity = Positive(staticLightingScatteringIntensity);
            value.gridPixelSize = Mathf.Clamp(gridPixelSize, 4, 64);
            value.gridSizeZ = Mathf.Clamp(gridSizeZ, 16, 256);
            value.historyWeight = Mathf.Clamp(Finite(historyWeight), 0f, 0.99f);
            return value;
        }

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        private static float Positive(float value) => Mathf.Max(0f, Finite(value));
        private static Color Positive(Color value) => new Color(Positive(value.r), Positive(value.g), Positive(value.b), 1f);
    }

    // Switching render models is discrete: fading extinction while excluding analytic fog
    // would clear the entire near range as soon as an enabled preset has nonzero weight.
    // Blend enabled contributors' properties and switch on at half the total weather weight.
    // Resolution and boolean choices come from the dominant enabled preset.
    internal struct DawnVolumetricFogBlend
    {
        private double weight, dominantWeight, g, extinction, distance, start, fade, staticIntensity, history;
        private Color albedo, emissive;
        private DawnVolumetricFogSettings dominant;

        public void Add(DawnVolumetricFogSettings input, double normalizedWeight)
        {
            if (!input.enabled || normalizedWeight <= 0d) return;
            var value = input.Sanitized();
            weight += normalizedWeight;
            g += value.scatteringDistribution * normalizedWeight;
            extinction += value.extinctionScale * normalizedWeight;
            distance += value.viewDistance * normalizedWeight;
            start += value.startDistance * normalizedWeight;
            fade += value.nearFadeInDistance * normalizedWeight;
            staticIntensity += value.staticLightingScatteringIntensity * normalizedWeight;
            history += value.historyWeight * normalizedWeight;
            albedo += value.albedo * (float)normalizedWeight;
            emissive += value.emissive * (float)normalizedWeight;
            if (normalizedWeight > dominantWeight)
            {
                dominantWeight = normalizedWeight;
                dominant = value;
            }
        }

        public DawnVolumetricFogSettings Resolve()
        {
            if (weight < 0.5d) return DawnVolumetricFogSettings.Default;
            var result = dominant;
            result.enabled = true;
            result.scatteringDistribution = (float)(g / weight);
            result.extinctionScale = (float)(extinction / weight);
            result.albedo = albedo / (float)weight;
            result.emissive = emissive / (float)weight;
            result.viewDistance = (float)(distance / weight);
            result.startDistance = (float)(start / weight);
            result.nearFadeInDistance = (float)(fade / weight);
            result.staticLightingScatteringIntensity = (float)(staticIntensity / weight);
            result.historyWeight = (float)(history / weight);
            return result.Sanitized();
        }
    }
}
