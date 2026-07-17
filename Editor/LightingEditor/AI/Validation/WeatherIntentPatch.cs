namespace DawnTODEditor.AI
{
    public enum WeatherIntentTimeMode
    {
        Current,
        Explicit
    }

    public sealed class WeatherIntentColor
    {
        public float R { get; }
        public float G { get; }
        public float B { get; }
        public float A { get; }

        internal WeatherIntentColor(float r, float g, float b, float a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    public sealed class WeatherIntentTimePatch
    {
        public WeatherIntentTimeMode Mode { get; }
        public float? Hour { get; }

        internal WeatherIntentTimePatch(WeatherIntentTimeMode mode, float? hour)
        {
            Mode = mode;
            Hour = hour;
        }
    }

    public sealed class WeatherIntentLightPatch
    {
        public float? AzimuthDegrees { get; }
        public float? ElevationDegrees { get; }
        public float? Intensity { get; }
        public WeatherIntentColor Color { get; }

        public bool HasChanges => AzimuthDegrees.HasValue ||
                                  ElevationDegrees.HasValue ||
                                  Intensity.HasValue ||
                                  Color != null;

        internal WeatherIntentLightPatch(
            float? azimuthDegrees,
            float? elevationDegrees,
            float? intensity,
            WeatherIntentColor color)
        {
            AzimuthDegrees = azimuthDegrees;
            ElevationDegrees = elevationDegrees;
            Intensity = intensity;
            Color = color;
        }
    }

    public sealed class WeatherIntentPatch
    {
        public const string SupportedSchemaVersion = "1.0";

        public string SchemaVersion { get; }
        public WeatherIntentTimePatch Time { get; }
        public WeatherIntentLightPatch Sun { get; }
        public WeatherIntentLightPatch Moon { get; }

        public bool HasChanges => Time.Mode == WeatherIntentTimeMode.Explicit ||
                                  Sun.HasChanges ||
                                  Moon.HasChanges;

        internal WeatherIntentPatch(
            string schemaVersion,
            WeatherIntentTimePatch time,
            WeatherIntentLightPatch sun,
            WeatherIntentLightPatch moon)
        {
            SchemaVersion = schemaVersion;
            Time = time;
            Sun = sun;
            Moon = moon;
        }
    }
}
