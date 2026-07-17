using UnityEngine;

namespace DawnTOD
{
    internal readonly struct WeatherScheduleWindow
    {
        public float StartHour { get; }
        public float EndHour { get; }
        public bool FullDay { get; }
        public float BlendInHours { get; }
        public float BlendOutHours { get; }
        public AnimationCurve Easing { get; }

        public WeatherScheduleWindow(
            float startHour,
            float endHour,
            bool fullDay,
            float blendInHours,
            float blendOutHours,
            AnimationCurve easing)
        {
            StartHour = startHour;
            EndHour = endHour;
            FullDay = fullDay;
            BlendInHours = blendInHours;
            BlendOutHours = blendOutHours;
            Easing = easing;
        }
    }

    internal static class WeatherScheduleWeightResolver
    {
        private const float HoursPerDay = 24f;
        private const float TimeEpsilon = 0.0001f;

        public static float NormalizeHour(float hour)
        {
            return IsFinite(hour) ? Mathf.Repeat(hour, HoursPerDay) : 0f;
        }

        public static float ForwardDistance(float fromHour, float toHour)
        {
            if (!IsFinite(fromHour) || !IsFinite(toHour))
            {
                return 0f;
            }

            float normalizedFrom = NormalizeHour(fromHour);
            float normalizedTo = NormalizeHour(toHour);
            return Mathf.Repeat(normalizedTo - normalizedFrom, HoursPerDay);
        }

        public static bool IsActive(WeatherScheduleWindow window, float currentHour)
        {
            return TryGetActiveTiming(window, currentHour, out _, out _);
        }

        public static float ResolveRawWeight(WeatherScheduleWindow window, float currentHour)
        {
            if (!TryGetActiveTiming(window, currentHour, out float duration, out float elapsed))
            {
                return 0f;
            }

            if (window.FullDay)
            {
                return 1f;
            }

            float blendIn = ClampBlendDuration(window.BlendInHours, duration);
            float blendOut = ClampBlendDuration(window.BlendOutHours, duration);
            float inWeight = blendIn > TimeEpsilon
                ? EvaluateEasing(window.Easing, elapsed / blendIn)
                : 1f;
            float remaining = duration - elapsed;
            float outWeight = blendOut > TimeEpsilon
                ? EvaluateEasing(window.Easing, remaining / blendOut)
                : 1f;

            return Mathf.Clamp01(Mathf.Min(inWeight, outWeight));
        }

        private static bool TryGetActiveTiming(
            WeatherScheduleWindow window,
            float currentHour,
            out float duration,
            out float elapsed)
        {
            duration = 0f;
            elapsed = 0f;
            if (!IsFinite(currentHour))
            {
                return false;
            }

            if (window.FullDay)
            {
                duration = HoursPerDay;
                elapsed = NormalizeHour(currentHour);
                return true;
            }

            if (!IsFinite(window.StartHour) || !IsFinite(window.EndHour))
            {
                return false;
            }

            duration = ForwardDistance(window.StartHour, window.EndHour);
            if (duration <= TimeEpsilon)
            {
                return false;
            }

            elapsed = ForwardDistance(window.StartHour, currentHour);
            return elapsed < duration;
        }

        private static float ClampBlendDuration(float blendDuration, float windowDuration)
        {
            if (!IsFinite(blendDuration))
            {
                return 0f;
            }

            return Mathf.Clamp(blendDuration, 0f, windowDuration);
        }

        private static float EvaluateEasing(AnimationCurve easing, float linearWeight)
        {
            float clampedLinearWeight = Mathf.Clamp01(linearWeight);
            if (easing == null || easing.length == 0)
            {
                return clampedLinearWeight;
            }

            float easedWeight = easing.Evaluate(clampedLinearWeight);
            return IsFinite(easedWeight) ? Mathf.Clamp01(easedWeight) : clampedLinearWeight;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
