using System.Collections.Generic;
using DawnTOD;
using UnityEditor;
using UnityEngine;

namespace DawnTODEditor
{
    internal enum WeatherScheduleIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    internal readonly struct WeatherScheduleIssue
    {
        public string Code { get; }
        public WeatherScheduleIssueSeverity Severity { get; }
        public int EntryIndex { get; }
        public string Message { get; }

        public WeatherScheduleIssue(
            string code,
            WeatherScheduleIssueSeverity severity,
            int entryIndex,
            string message)
        {
            Code = code;
            Severity = severity;
            EntryIndex = entryIndex;
            Message = message;
        }
    }

    internal sealed class WeatherScheduleMigrationPreview
    {
        public int EntryCount { get; }
        public IReadOnlyList<WeatherScheduleIssue> Issues { get; }
        public bool HasAmbiguities { get; }

        public WeatherScheduleMigrationPreview(
            int entryCount,
            List<WeatherScheduleIssue> issues)
        {
            EntryCount = entryCount;
            Issues = issues.AsReadOnly();
            for (int index = 0; index < issues.Count; index++)
            {
                if (issues[index].Severity == WeatherScheduleIssueSeverity.Warning ||
                    issues[index].Severity == WeatherScheduleIssueSeverity.Error)
                {
                    HasAmbiguities = true;
                    break;
                }
            }
        }
    }

    internal static class WeatherScheduleMigrationUtility
    {
        public const string UndoName = "Migrate TOD Weather Schedule";

        public static WeatherScheduleMigrationPreview Analyze(DawnTODSystem system)
        {
            var issues = new List<WeatherScheduleIssue>();
            if (system == null || system.controllerTimeRanges == null)
            {
                return new WeatherScheduleMigrationPreview(0, issues);
            }

            List<DawnTODSystem.WeatherControllerTimeRange> entries =
                system.controllerTimeRanges;
            int fullDayCount = 0;
            for (int index = 0; index < entries.Count; index++)
            {
                DawnTODSystem.WeatherControllerTimeRange entry = entries[index];
                if (entry == null || entry.controller == null)
                {
                    issues.Add(new WeatherScheduleIssue(
                        "MISSING_CONTROLLER",
                        WeatherScheduleIssueSeverity.Error,
                        index,
                        "Legacy entry has no Controller."));
                    continue;
                }

                bool legacyFullDay = Mathf.Approximately(entry.startHour, 0f) &&
                                     Mathf.Approximately(entry.endHour, 24f);
                if (legacyFullDay)
                {
                    fullDayCount++;
                }

                if (entry.startHour > entry.endHour)
                {
                    issues.Add(new WeatherScheduleIssue(
                        "CROSS_MIDNIGHT",
                        WeatherScheduleIssueSeverity.Warning,
                        index,
                        "Cross-midnight legacy entry requires visual review."));
                }

                if (entry.LegacyPreset != null &&
                    entry.LegacyPreset != entry.controller.ActivePreset)
                {
                    issues.Add(new WeatherScheduleIssue(
                        "PRESET_CONFLICT",
                        WeatherScheduleIssueSeverity.Warning,
                        index,
                        "Legacy Preset differs from Controller.ActivePreset; migration uses ActivePreset."));
                }

                for (int previous = 0; previous < index; previous++)
                {
                    DawnTODSystem.WeatherControllerTimeRange other = entries[previous];
                    if (other != null &&
                        Mathf.Approximately(other.startHour, entry.startHour))
                    {
                        issues.Add(new WeatherScheduleIssue(
                            "SAME_START",
                            WeatherScheduleIssueSeverity.Warning,
                            index,
                            "Multiple legacy entries share the same start time."));
                        break;
                    }
                }
            }

            if (fullDayCount > 1)
            {
                issues.Add(new WeatherScheduleIssue(
                    "MULTIPLE_FULL_DAY",
                    WeatherScheduleIssueSeverity.Warning,
                    -1,
                    "Multiple full-day legacy entries are ambiguous."));
            }

            if (HasTripleOverlap(entries))
            {
                issues.Add(new WeatherScheduleIssue(
                    "TRIPLE_OVERLAP",
                    WeatherScheduleIssueSeverity.Warning,
                    -1,
                    "Three or more legacy entries overlap."));
            }

            return new WeatherScheduleMigrationPreview(entries.Count, issues);
        }

        public static bool MigrateWithUndo(DawnTODSystem system)
        {
            if (system == null || !system.NeedsScheduleMigration)
            {
                return false;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            Undo.RegisterCompleteObjectUndo(system, UndoName);
            system.MigrateLegacyScheduleData();
            EditorUtility.SetDirty(system);
            Undo.CollapseUndoOperations(group);
            return true;
        }

        private static bool HasTripleOverlap(
            List<DawnTODSystem.WeatherControllerTimeRange> entries)
        {
            const int Samples = 96;
            for (int sample = 0; sample < Samples; sample++)
            {
                float hour = sample * (24f / Samples);
                int activeCount = 0;
                for (int index = 0; index < entries.Count; index++)
                {
                    DawnTODSystem.WeatherControllerTimeRange entry = entries[index];
                    if (entry == null)
                    {
                        continue;
                    }

                    WeatherScheduleWindow window = entry.CreateLegacyScheduleWindow();
                    if (WeatherScheduleWeightResolver.IsActive(window, hour) &&
                        ++activeCount >= 3)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
