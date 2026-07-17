using System.Collections.Generic;
using DawnTOD;
using UnityEngine;

namespace DawnTODEditor
{
    internal sealed class WeatherScheduleDiagnosticReport
    {
        public IReadOnlyList<WeatherScheduleIssue> Issues { get; }
        public bool HasErrors { get; }
        public bool HasWarnings { get; }

        public WeatherScheduleDiagnosticReport(List<WeatherScheduleIssue> issues)
        {
            Issues = issues.AsReadOnly();
            for (int index = 0; index < issues.Count; index++)
            {
                HasErrors |= issues[index].Severity == WeatherScheduleIssueSeverity.Error;
                HasWarnings |= issues[index].Severity == WeatherScheduleIssueSeverity.Warning;
            }
        }
    }

    internal static class WeatherScheduleDiagnostics
    {
        public static WeatherScheduleDiagnosticReport Analyze(DawnTODSystem system)
        {
            var issues = new List<WeatherScheduleIssue>();
            if (system == null)
            {
                return new WeatherScheduleDiagnosticReport(issues);
            }

            if (system.NeedsScheduleMigration)
            {
                issues.Add(Issue(
                    "MIGRATION_REQUIRED",
                    WeatherScheduleIssueSeverity.Error,
                    -1,
                    "Legacy schedule data must be reviewed and migrated."));
            }

            List<DawnTODSystem.WeatherControllerTimeRange> entries =
                system.controllerTimeRanges;
            if (entries == null)
            {
                return new WeatherScheduleDiagnosticReport(issues);
            }

            var seenControllers = new HashSet<DawnWeatherController>();
            for (int index = 0; index < entries.Count; index++)
            {
                DawnTODSystem.WeatherControllerTimeRange entry = entries[index];
                if (entry == null || entry.controller == null)
                {
                    issues.Add(Issue(
                        "MISSING_CONTROLLER",
                        WeatherScheduleIssueSeverity.Error,
                        index,
                        "Schedule entry has no Controller."));
                    continue;
                }

                if (!seenControllers.Add(entry.controller))
                {
                    issues.Add(Issue(
                        "DUPLICATE_CONTROLLER",
                        WeatherScheduleIssueSeverity.Warning,
                        index,
                        "Controller is referenced by more than one schedule entry."));
                }

                if (!entry.controller.gameObject.activeInHierarchy ||
                    !entry.controller.enabled)
                {
                    issues.Add(Issue(
                        "CONTROLLER_DISABLED",
                        WeatherScheduleIssueSeverity.Warning,
                        index,
                        "Controller is inactive or disabled and contributes no weight."));
                }

                if (entry.controller.ActivePreset == null)
                {
                    issues.Add(Issue(
                        "MISSING_PRESET",
                        WeatherScheduleIssueSeverity.Error,
                        index,
                        "Controller has no Active Preset."));
                }

                if (!entry.fullDay &&
                    Mathf.Approximately(
                        WeatherScheduleWeightResolver.NormalizeHour(entry.startHour),
                        WeatherScheduleWeightResolver.NormalizeHour(entry.endHour)))
                {
                    issues.Add(Issue(
                        "ZERO_DURATION",
                        WeatherScheduleIssueSeverity.Error,
                        index,
                        "Start equals End while Full Day is disabled."));
                }

                float duration = entry.DurationHours;
                if (entry.blendInHours > duration + 0.0001f ||
                    entry.blendOutHours > duration + 0.0001f)
                {
                    issues.Add(Issue(
                        "BLEND_EXCEEDS_DURATION",
                        WeatherScheduleIssueSeverity.Warning,
                        index,
                        "Blend In/Out exceeds the schedule duration."));
                }
            }

            AnalyzeCoverage(system, entries, issues);
            return new WeatherScheduleDiagnosticReport(issues);
        }

        private static void AnalyzeCoverage(
            DawnTODSystem system,
            List<DawnTODSystem.WeatherControllerTimeRange> entries,
            List<WeatherScheduleIssue> issues)
        {
            const int Samples = 96;
            bool gapReported = false;
            bool tripleReported = false;
            for (int sample = 0; sample < Samples; sample++)
            {
                float hour = sample * (24f / Samples);
                int activeCount = 0;
                for (int index = 0; index < entries.Count; index++)
                {
                    DawnTODSystem.WeatherControllerTimeRange entry = entries[index];
                    if (entry == null || !entry.enabled ||
                        entry.controller == null ||
                        !entry.controller.isActiveAndEnabled ||
                        entry.controller.ActivePreset == null)
                    {
                        continue;
                    }

                    if (entry.GetRawWeightAt(hour) > 0.000001f)
                    {
                        activeCount++;
                    }
                }

                if (!gapReported && activeCount == 0 && system.FallbackPreset == null)
                {
                    issues.Add(Issue(
                        "UNCOVERED_TIME",
                        WeatherScheduleIssueSeverity.Error,
                        -1,
                        $"No weather or fallback covers {FormatHour(hour)}."));
                    gapReported = true;
                }

                if (!tripleReported && activeCount >= 3)
                {
                    issues.Add(Issue(
                        "TRIPLE_OVERLAP",
                        WeatherScheduleIssueSeverity.Warning,
                        -1,
                        $"Three or more schedules overlap near {FormatHour(hour)}."));
                    tripleReported = true;
                }
            }
        }

        private static WeatherScheduleIssue Issue(
            string code,
            WeatherScheduleIssueSeverity severity,
            int entryIndex,
            string message)
        {
            return new WeatherScheduleIssue(code, severity, entryIndex, message);
        }

        private static string FormatHour(float hour)
        {
            int totalMinutes = Mathf.RoundToInt(hour * 60f);
            return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
        }
    }
}
