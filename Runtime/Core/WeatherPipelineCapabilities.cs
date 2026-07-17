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

        private WeatherPipelineCapabilities(
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

        public static WeatherPipelineCapabilities Current
        {
            get
            {
#if USING_HDRP
                return new WeatherPipelineCapabilities(
                    WeatherRenderPipelineKind.HighDefinition,
                    true,
                    true,
                    true,
                    true);
#elif USING_URP
                return new WeatherPipelineCapabilities(
                    WeatherRenderPipelineKind.Universal,
                    false,
                    true,
                    true,
                    false);
#else
                return new WeatherPipelineCapabilities(
                    WeatherRenderPipelineKind.Unknown,
                    false,
                    false,
                    false,
                    false);
#endif
            }
        }
    }
}
