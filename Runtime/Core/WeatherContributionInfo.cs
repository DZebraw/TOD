namespace DawnTOD
{
    public readonly struct WeatherContributionInfo
    {
        public int ScheduleIndex { get; }
        public DawnWeatherController Controller { get; }
        public DawnWeatherPreset Preset { get; }
        public float RawWeight { get; }
        public float NormalizedWeight { get; }
        public bool IsFallback { get; }

        public WeatherContributionInfo(
            int scheduleIndex,
            DawnWeatherController controller,
            DawnWeatherPreset preset,
            float rawWeight,
            float normalizedWeight,
            bool isFallback)
        {
            ScheduleIndex = scheduleIndex;
            Controller = controller;
            Preset = preset;
            RawWeight = rawWeight;
            NormalizedWeight = normalizedWeight;
            IsFallback = isFallback;
        }
    }
}
