using System;
using System.Collections.Generic;

namespace DawnTOD
{
    internal enum WeatherWeightResolutionMode
    {
        Scheduled,
        Fallback
    }

    internal readonly struct WeatherWeightContribution
    {
        public const int FallbackSourceIndex = -1;

        public int SourceIndex { get; }
        public float RawWeight { get; }
        public float NormalizedWeight { get; }
        public bool IsFallback => SourceIndex == FallbackSourceIndex;

        public WeatherWeightContribution(
            int sourceIndex,
            float rawWeight,
            float normalizedWeight)
        {
            SourceIndex = sourceIndex;
            RawWeight = rawWeight;
            NormalizedWeight = normalizedWeight;
        }

        public WeatherWeightContribution WithNormalizedWeight(float normalizedWeight)
        {
            return new WeatherWeightContribution(SourceIndex, RawWeight, normalizedWeight);
        }

        public static WeatherWeightContribution CreateFallback()
        {
            return new WeatherWeightContribution(FallbackSourceIndex, 1f, 1f);
        }
    }

    internal static class WeatherContributionResolver
    {
        private const float PositiveWeightEpsilon = 0.000001f;

        public static WeatherWeightResolutionMode Resolve(
            IReadOnlyList<WeatherScheduleWindow> windows,
            float currentHour,
            List<WeatherWeightContribution> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            output.Clear();
            if (windows == null)
            {
                return AddFallback(output);
            }

            double totalRawWeight = 0d;
            for (int sourceIndex = 0; sourceIndex < windows.Count; sourceIndex++)
            {
                float rawWeight = WeatherScheduleWeightResolver.ResolveRawWeight(
                    windows[sourceIndex],
                    currentHour);
                if (!IsPositiveFinite(rawWeight))
                {
                    continue;
                }

                output.Add(new WeatherWeightContribution(sourceIndex, rawWeight, 0f));
                totalRawWeight += rawWeight;
            }

            if (output.Count == 0 || totalRawWeight <= PositiveWeightEpsilon)
            {
                return AddFallback(output);
            }

            double accumulatedNormalizedWeight = 0d;
            int lastIndex = output.Count - 1;
            for (int contributionIndex = 0; contributionIndex < output.Count; contributionIndex++)
            {
                WeatherWeightContribution contribution = output[contributionIndex];
                double normalizedWeight = contributionIndex == lastIndex
                    ? 1d - accumulatedNormalizedWeight
                    : contribution.RawWeight / totalRawWeight;
                float clampedWeight = Clamp01((float)normalizedWeight);
                output[contributionIndex] = contribution.WithNormalizedWeight(clampedWeight);
                accumulatedNormalizedWeight += clampedWeight;
            }

            return WeatherWeightResolutionMode.Scheduled;
        }

        private static WeatherWeightResolutionMode AddFallback(
            List<WeatherWeightContribution> output)
        {
            output.Clear();
            output.Add(WeatherWeightContribution.CreateFallback());
            return WeatherWeightResolutionMode.Fallback;
        }

        private static bool IsPositiveFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   value > PositiveWeightEpsilon;
        }

        private static float Clamp01(float value)
        {
            if (value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }
    }
}
