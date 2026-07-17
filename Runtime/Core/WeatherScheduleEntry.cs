using System;
using UnityEngine;

namespace DawnTOD
{
    [Serializable]
    public class WeatherScheduleEntry
    {
        public DawnWeatherController controller;
        public bool enabled = true;
        public bool fullDay;

        [Range(0f, 24f)]
        public float startHour;

        [Range(0f, 24f)]
        public float endHour = 24f;

        [Min(0f)]
        public float blendInHours;

        [Min(0f)]
        public float blendOutHours;

        public AnimationCurve easing;

        public float DurationHours => fullDay
            ? 24f
            : WeatherScheduleWeightResolver.ForwardDistance(startHour, endHour);

        public bool CrossesMidnight =>
            !fullDay &&
            DurationHours > 0.0001f &&
            WeatherScheduleWeightResolver.NormalizeHour(startHour) >=
            WeatherScheduleWeightResolver.NormalizeHour(endHour);

        public bool IsActiveAt(float currentHour)
        {
            return enabled && WeatherScheduleWeightResolver.IsActive(
                CreateScheduleWindow(),
                currentHour);
        }

        public float GetRawWeightAt(float currentHour)
        {
            return enabled
                ? WeatherScheduleWeightResolver.ResolveRawWeight(
                    CreateScheduleWindow(),
                    currentHour)
                : 0f;
        }

        internal WeatherScheduleWindow CreateScheduleWindow()
        {
            return new WeatherScheduleWindow(
                startHour,
                endHour,
                fullDay,
                blendInHours,
                blendOutHours,
                easing);
        }

        internal WeatherScheduleWindow CreateLegacyScheduleWindow()
        {
            bool legacyFullDay = Mathf.Approximately(startHour, 0f) &&
                                 Mathf.Approximately(endHour, 24f);
            return new WeatherScheduleWindow(
                startHour,
                endHour,
                legacyFullDay,
                0f,
                0f,
                null);
        }

        internal void Sanitize()
        {
            startHour = Mathf.Clamp(startHour, 0f, 24f);
            endHour = Mathf.Clamp(endHour, 0f, 24f);
            float duration = DurationHours;
            blendInHours = Mathf.Clamp(blendInHours, 0f, duration);
            blendOutHours = Mathf.Clamp(blendOutHours, 0f, duration);
            if (fullDay)
            {
                blendInHours = 0f;
                blendOutHours = 0f;
            }
        }
    }
}
