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

    public sealed class WeatherIntentSkyPatch
    {
        public float? StarEmission { get; }
        public bool HasChanges => StarEmission.HasValue;

        internal WeatherIntentSkyPatch(float? starEmission)
        {
            StarEmission = starEmission;
        }
    }

    public sealed class WeatherIntentFogPatch
    {
        public float? MeanFreePathMeters { get; }
        public float? BaseHeightMeters { get; }
        public WeatherIntentColor Color { get; }

        public bool HasChanges => MeanFreePathMeters.HasValue ||
                                  BaseHeightMeters.HasValue ||
                                  Color != null;

        internal WeatherIntentFogPatch(
            float? meanFreePathMeters,
            float? baseHeightMeters,
            WeatherIntentColor color)
        {
            MeanFreePathMeters = meanFreePathMeters;
            BaseHeightMeters = baseHeightMeters;
            Color = color;
        }
    }

    public sealed class WeatherIntentExposurePatch
    {
        public float? CompensationEv { get; }
        public bool HasChanges => CompensationEv.HasValue;

        internal WeatherIntentExposurePatch(float? compensationEv)
        {
            CompensationEv = compensationEv;
        }
    }

    public sealed class WeatherIntentRainPatch
    {
        public bool? Enabled { get; }
        public float? PrecipitationAmount { get; }
        public float? FallSpeed { get; }
        public float? Density { get; }
        public float? WindZRotationDegrees { get; }

        public bool HasChanges => Enabled.HasValue ||
                                  PrecipitationAmount.HasValue ||
                                  FallSpeed.HasValue ||
                                  Density.HasValue ||
                                  WindZRotationDegrees.HasValue;

        internal WeatherIntentRainPatch(
            bool? enabled,
            float? precipitationAmount,
            float? fallSpeed,
            float? density,
            float? windZRotationDegrees)
        {
            Enabled = enabled;
            PrecipitationAmount = precipitationAmount;
            FallSpeed = fallSpeed;
            Density = density;
            WindZRotationDegrees = windZRotationDegrees;
        }
    }

    public sealed class WeatherIntentPatch
    {
        public const string SupportedSchemaVersion = "1.1";

        public string SchemaVersion { get; }
        public WeatherIntentTimePatch Time { get; }
        public WeatherIntentLightPatch Sun { get; }
        public WeatherIntentLightPatch Moon { get; }
        public WeatherIntentSkyPatch Sky { get; }
        public WeatherIntentFogPatch Fog { get; }
        public WeatherIntentExposurePatch Exposure { get; }
        public WeatherIntentRainPatch Rain { get; }

        public bool HasChanges => Time.Mode == WeatherIntentTimeMode.Explicit ||
                                  Sun.HasChanges ||
                                  Moon.HasChanges ||
                                  Sky.HasChanges ||
                                  Fog.HasChanges ||
                                  Exposure.HasChanges ||
                                  Rain.HasChanges;

        internal WeatherIntentPatch(
            string schemaVersion,
            WeatherIntentTimePatch time,
            WeatherIntentLightPatch sun,
            WeatherIntentLightPatch moon,
            WeatherIntentSkyPatch sky,
            WeatherIntentFogPatch fog,
            WeatherIntentExposurePatch exposure,
            WeatherIntentRainPatch rain)
        {
            SchemaVersion = schemaVersion;
            Time = time;
            Sun = sun;
            Moon = moon;
            Sky = sky;
            Fog = fog;
            Exposure = exposure;
            Rain = rain;
        }
    }
}
