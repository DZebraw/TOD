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
        public bool SupportsFog { get; }
        public bool SupportsExposure { get; }

        private WeatherPipelineCapabilities(
            WeatherRenderPipelineKind pipelineKind,
            bool supportsEnvironment)
        {
            PipelineKind = pipelineKind;
            SupportsCelestialLights = true;
            SupportsRain = true;
            SupportsPhysicalSky = supportsEnvironment;
            SupportsFog = supportsEnvironment;
            SupportsExposure = supportsEnvironment;
        }

        public static WeatherPipelineCapabilities Current
        {
            get
            {
#if USING_HDRP
                return new WeatherPipelineCapabilities(
                    WeatherRenderPipelineKind.HighDefinition,
                    true);
#elif USING_URP
                return new WeatherPipelineCapabilities(
                    WeatherRenderPipelineKind.Universal,
                    false);
#else
                return new WeatherPipelineCapabilities(
                    WeatherRenderPipelineKind.Unknown,
                    false);
#endif
            }
        }
    }
}
