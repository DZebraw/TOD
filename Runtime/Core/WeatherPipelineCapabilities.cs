using System;
using UnityEngine.Rendering;

namespace DawnTOD
{
    public enum WeatherRenderPipelineKind
    {
        Unknown,
        Universal,
        HighDefinition
    }

    public readonly struct WeatherPipelineCapabilities
    {
        public WeatherRenderPipelineKind PipelineKind { get; }
        public bool SupportsCelestialLights { get; }
        public bool SupportsRain { get; }
        public bool SupportsPhysicalSky { get; }
        public bool SupportsStarEmission { get; }
        public bool SupportsFog { get; }
        public bool SupportsExposure { get; }

        private const string UniversalPipelineAssetTypeName =
            "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";
        private const string HighDefinitionPipelineAssetTypeName =
            "UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset";

        public WeatherPipelineCapabilities(
            WeatherRenderPipelineKind pipelineKind,
            bool supportsPhysicalSky,
            bool supportsStarEmission,
            bool supportsFog,
            bool supportsExposure)
        {
            PipelineKind = pipelineKind;
            SupportsCelestialLights = true;
            SupportsRain = true;
            SupportsPhysicalSky = supportsPhysicalSky;
            SupportsStarEmission = supportsStarEmission;
            SupportsFog = supportsFog;
            SupportsExposure = supportsExposure;
        }

        public static WeatherPipelineCapabilities Unknown =>
            new WeatherPipelineCapabilities(
                WeatherRenderPipelineKind.Unknown,
                false,
                false,
                false,
                false);

        public static WeatherPipelineCapabilities Universal =>
            new WeatherPipelineCapabilities(
                WeatherRenderPipelineKind.Universal,
                false,
                true,
                true,
                false);

        public static WeatherPipelineCapabilities HighDefinition =>
            new WeatherPipelineCapabilities(
                WeatherRenderPipelineKind.HighDefinition,
                true,
                true,
                true,
                true);

        public static WeatherPipelineCapabilities Current
        {
            get
            {
                RenderPipelineAsset pipelineAsset =
                    GraphicsSettings.currentRenderPipeline ??
                    GraphicsSettings.defaultRenderPipeline;
                return FromPipelineAsset(pipelineAsset);
            }
        }

        public static WeatherPipelineCapabilities ForKind(
            WeatherRenderPipelineKind pipelineKind)
        {
            switch (pipelineKind)
            {
                case WeatherRenderPipelineKind.Universal:
                    return Universal;
                case WeatherRenderPipelineKind.HighDefinition:
                    return HighDefinition;
                default:
                    return Unknown;
            }
        }

        public static WeatherPipelineCapabilities FromPipelineAsset(
            RenderPipelineAsset pipelineAsset)
        {
            for (Type pipelineType = pipelineAsset?.GetType();
                 pipelineType != null;
                 pipelineType = pipelineType.BaseType)
            {
                if (pipelineType.FullName == UniversalPipelineAssetTypeName)
                {
                    return Universal;
                }

                if (pipelineType.FullName ==
                    HighDefinitionPipelineAssetTypeName)
                {
                    return HighDefinition;
                }
            }

            return Unknown;
        }
    }
}
