using System;
using System.Collections.Generic;
using System.Globalization;
using DawnTOD;
using UnityEditor;
using UnityEngine;
#if USING_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace DawnTODEditor
{
    [CustomEditor(typeof(DawnTODSystem))]
    public class DawnTODSystemEditor : Editor
    {
        private const float HoursPerDay = 24f;
        private const float TimelineRowHeight = 20f;

        private readonly List<WeatherContributionInfo> contributions =
            new List<WeatherContributionInfo>();

        private SerializedProperty schedulesProp;
        private SerializedProperty fallbackPresetProp;
        private SerializedProperty timeOfDayProp;
        private SerializedProperty sunriseTimeProp;
        private SerializedProperty sunsetTimeProp;
        private SerializedProperty autoAdvanceTimeProp;
        private SerializedProperty dayLengthInSecondsProp;
        private SerializedProperty timeScaleProp;
        private SerializedProperty sunLightProp;
        private SerializedProperty moonLightProp;
        private SerializedProperty rainParticleSystemProp;
        private SerializedProperty shadowEnableIntensityProp;
        private SerializedProperty shadowHysteresisProp;
#if USING_HDRP
        private SerializedProperty hdrpVolumeProp;
#endif

        private bool showDiagnostics = true;
        private bool showDebugWeights = true;
        private bool showTimeSettings = true;
        private bool showOutputSettings = true;

        private void OnEnable()
        {
            schedulesProp = serializedObject.FindProperty("controllerTimeRanges");
            fallbackPresetProp = serializedObject.FindProperty("fallbackPreset");
            timeOfDayProp = serializedObject.FindProperty("timeOfDay");
            sunriseTimeProp = serializedObject.FindProperty("sunriseTime");
            sunsetTimeProp = serializedObject.FindProperty("sunsetTime");
            autoAdvanceTimeProp = serializedObject.FindProperty("autoAdvanceTime");
            dayLengthInSecondsProp = serializedObject.FindProperty("dayLengthInSeconds");
            timeScaleProp = serializedObject.FindProperty("timeScale");
            sunLightProp = serializedObject.FindProperty("sunLight");
            moonLightProp = serializedObject.FindProperty("moonLight");
            rainParticleSystemProp = serializedObject.FindProperty("rainParticleSystem");
            shadowEnableIntensityProp = serializedObject.FindProperty("shadowEnableIntensity");
            shadowHysteresisProp = serializedObject.FindProperty("shadowHysteresis");
#if USING_HDRP
            hdrpVolumeProp = serializedObject.FindProperty("hdrpVolume");
#endif
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var system = (DawnTODSystem)target;
            WeatherScheduleDiagnosticReport report =
                WeatherScheduleDiagnostics.Analyze(system);

            DrawMigrationPanel(system);
            DrawSchedule(system, report);
            EditorGUILayout.Space(8f);
            DrawTimeSettings(system);
            EditorGUILayout.Space(8f);
            DrawOutputSettings(system);
            DrawPipelineCapability(system);

            ApplyModifiedPropertiesAndRefresh(serializedObject, system);
        }

        internal static bool ApplyModifiedPropertiesAndRefresh(
            SerializedObject serializedObject,
            DawnTODSystem todSystem)
        {
            if (!serializedObject.ApplyModifiedProperties())
            {
                return false;
            }

            todSystem.RefreshWeatherBlendingSystem();
            EditorUtility.SetDirty(todSystem);
            SceneView.RepaintAll();
            return true;
        }

        internal static string FormatHour(float hour, bool allowEndOfDay = false)
        {
            if (allowEndOfDay && Mathf.Approximately(hour, 24f))
            {
                return "24:00";
            }

            int totalMinutes = Mathf.RoundToInt(
                Mathf.Repeat(hour, HoursPerDay) * 60f);
            totalMinutes %= 24 * 60;
            return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
        }

        internal static bool TryParseHour(
            string text,
            bool allowEndOfDay,
            out float hour)
        {
            hour = 0f;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] parts = text.Trim().Split(':');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) ||
                minutes < 0 || minutes > 59)
            {
                return false;
            }

            if (allowEndOfDay && hours == 24 && minutes == 0)
            {
                hour = 24f;
                return true;
            }

            if (hours < 0 || hours > 23)
            {
                return false;
            }

            hour = hours + minutes / 60f;
            return true;
        }

        internal static void SetTimeFromTimeline(
            DawnTODSystem system,
            Rect timelineRect,
            float mouseX)
        {
            if (system == null || timelineRect.width <= 0f)
            {
                return;
            }

            float normalized = Mathf.InverseLerp(
                timelineRect.x,
                timelineRect.xMax,
                Mathf.Clamp(mouseX, timelineRect.x, timelineRect.xMax));
            Undo.RecordObject(system, "Set TOD Time From Weather Timeline");
            system.SetTime(normalized * HoursPerDay);
            EditorUtility.SetDirty(system);
            SceneView.RepaintAll();
        }

        internal static int RescanControllersWithUndo(DawnTODSystem system)
        {
            if (system == null)
            {
                return 0;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Rescan TOD Controllers");
            Undo.RegisterCompleteObjectUndo(system, "Rescan TOD Controllers");
            int count = system.RescanControllers(true);
            EditorUtility.SetDirty(system);
            Undo.CollapseUndoOperations(group);
            return count;
        }

        private void DrawMigrationPanel(DawnTODSystem system)
        {
            if (!system.NeedsScheduleMigration)
            {
                return;
            }

            WeatherScheduleMigrationPreview preview =
                WeatherScheduleMigrationUtility.Analyze(system);
            EditorGUILayout.HelpBox(
                $"Legacy weather schedule detected ({preview.EntryCount} entries). " +
                "Review the warnings, then migrate with one Undo step.",
                preview.HasAmbiguities ? MessageType.Warning : MessageType.Info);
            for (int index = 0; index < preview.Issues.Count; index++)
            {
                WeatherScheduleIssue issue = preview.Issues[index];
                EditorGUILayout.LabelField(
                    $"• [{issue.Code}] {issue.Message}",
                    EditorStyles.wordWrappedMiniLabel);
            }

            if (GUILayout.Button("Review & Migrate Legacy Schedule"))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Migrate Weather Schedule",
                    "Migration uses each Controller's current ActivePreset. " +
                    "Ambiguous overlaps should be reviewed after migration.",
                    "Migrate",
                    "Cancel");
                if (confirmed && WeatherScheduleMigrationUtility.MigrateWithUndo(system))
                {
                    serializedObject.Update();
                }
            }

            EditorGUILayout.Space(6f);
        }

        private void DrawSchedule(
            DawnTODSystem system,
            WeatherScheduleDiagnosticReport report)
        {
            EditorGUILayout.LabelField("Weather Schedule", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(fallbackPresetProp, new GUIContent("Fallback Preset"));
            DrawTimeline(system);
            DrawCurrentContributions(system);
            DrawScheduleToolbar(system);

            if (schedulesProp == null)
            {
                EditorGUILayout.HelpBox("Schedule property is unavailable.", MessageType.Error);
                return;
            }

            for (int index = 0; index < schedulesProp.arraySize; index++)
            {
                if (DrawScheduleEntry(index, report))
                {
                    break;
                }
            }

            if (GUILayout.Button("Add Schedule Entry"))
            {
                AddScheduleEntry();
            }

            showDiagnostics = EditorGUILayout.Foldout(
                showDiagnostics,
                $"Diagnostics ({report.Issues.Count})",
                true);
            if (showDiagnostics)
            {
                DrawDiagnostics(report);
            }
        }

        private void DrawTimeline(DawnTODSystem system)
        {
            int rowCount = Mathf.Max(1, system.controllerTimeRanges?.Count ?? 0);
            float height = 30f + rowCount * TimelineRowHeight;
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            EditorGUI.DrawRect(rect, new Color(0.11f, 0.12f, 0.14f));

            Rect content = new Rect(rect.x, rect.y + 20f, rect.width, rect.height - 20f);
            for (int hour = 0; hour <= 24; hour += 3)
            {
                float x = Mathf.Lerp(rect.x, rect.xMax, hour / HoursPerDay);
                EditorGUI.DrawRect(
                    new Rect(x, rect.y + 16f, 1f, rect.height - 16f),
                    new Color(1f, 1f, 1f, hour % 6 == 0 ? 0.2f : 0.08f));
                GUI.Label(
                    new Rect(x - 12f, rect.y, 28f, 16f),
                    hour.ToString("00"),
                    EditorStyles.centeredGreyMiniLabel);
            }

            if (system.controllerTimeRanges != null)
            {
                for (int index = 0; index < system.controllerTimeRanges.Count; index++)
                {
                    WeatherScheduleEntry entry = system.controllerTimeRanges[index];
                    if (entry == null) continue;
                    Rect row = new Rect(
                        content.x,
                        content.y + index * TimelineRowHeight + 2f,
                        content.width,
                        TimelineRowHeight - 4f);
                    DrawTimelineEntry(row, entry, GetEntryColor(index, entry));
                }
            }

            float currentX = Mathf.Lerp(
                rect.x,
                rect.xMax,
                Mathf.Repeat(system.TimeOfDay, HoursPerDay) / HoursPerDay);
            EditorGUI.DrawRect(
                new Rect(currentX - 1f, rect.y + 16f, 2f, rect.height - 16f),
                Color.cyan);
            GUI.Label(
                new Rect(
                    Mathf.Clamp(currentX - 28f, rect.x, rect.xMax - 56f),
                    rect.yMax - 18f,
                    56f,
                    16f),
                FormatHour(system.TimeOfDay),
                EditorStyles.miniBoldLabel);

            Event current = Event.current;
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                rect.Contains(current.mousePosition))
            {
                SetTimeFromTimeline(system, rect, current.mousePosition.x);
                serializedObject.Update();
                current.Use();
                Repaint();
            }
        }

        private static void DrawTimelineEntry(
            Rect row,
            WeatherScheduleEntry entry,
            Color color)
        {
            if (!entry.enabled)
            {
                color = Color.gray;
            }

            if (entry.fullDay)
            {
                EditorGUI.DrawRect(row, color);
                return;
            }

            float start = Mathf.Repeat(entry.startHour, HoursPerDay) / HoursPerDay;
            float end = Mathf.Repeat(entry.endHour, HoursPerDay) / HoursPerDay;
            if (entry.CrossesMidnight)
            {
                DrawTimelineSegment(row, start, 1f, color);
                DrawTimelineSegment(row, 0f, end, color);
            }
            else if (end > start)
            {
                DrawTimelineSegment(row, start, end, color);
            }

            DrawBlendOverlay(row, entry, true);
            DrawBlendOverlay(row, entry, false);
        }

        private static void DrawTimelineSegment(
            Rect row,
            float normalizedStart,
            float normalizedEnd,
            Color color)
        {
            Rect segment = new Rect(
                row.x + row.width * normalizedStart,
                row.y,
                row.width * (normalizedEnd - normalizedStart),
                row.height);
            EditorGUI.DrawRect(segment, color);
        }

        private static void DrawBlendOverlay(
            Rect row,
            WeatherScheduleEntry entry,
            bool blendIn)
        {
            float hours = blendIn ? entry.blendInHours : entry.blendOutHours;
            if (hours <= 0.0001f || entry.fullDay)
            {
                return;
            }

            float anchor = blendIn ? entry.startHour : entry.endHour - hours;
            float start = Mathf.Repeat(anchor, HoursPerDay) / HoursPerDay;
            float width = Mathf.Min(hours / HoursPerDay, 1f - start);
            EditorGUI.DrawRect(
                new Rect(
                    row.x + row.width * start,
                    row.y,
                    row.width * width,
                    row.height),
                new Color(1f, 1f, 1f, 0.18f));
        }

        private void DrawCurrentContributions(DawnTODSystem system)
        {
            system.GetCurrentContributions(contributions);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Current", GUILayout.Width(50f));
            if (contributions.Count == 0)
            {
                EditorGUILayout.LabelField("No evaluated contribution");
            }
            else
            {
                for (int index = 0; index < contributions.Count; index++)
                {
                    WeatherContributionInfo info = contributions[index];
                    string name = info.IsFallback
                        ? "Fallback"
                        : info.Controller != null
                            ? info.Controller.name
                            : $"Entry {info.ScheduleIndex}";
                    EditorGUILayout.LabelField(
                        $"{name} {info.NormalizedWeight:P0}",
                        GUILayout.MinWidth(90f));
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawScheduleToolbar(DawnTODSystem system)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan Controllers"))
            {
                RescanControllersWithUndo(system);
                serializedObject.Update();
            }

            if (GUILayout.Button("Sort by Start"))
            {
                Undo.RegisterCompleteObjectUndo(system, "Sort TOD Weather Schedule");
                system.controllerTimeRanges.Sort(CompareScheduleEntries);
                system.RefreshWeatherBlendingSystem();
                EditorUtility.SetDirty(system);
                serializedObject.Update();
            }

            if (GUILayout.Button("Validate"))
            {
                showDiagnostics = true;
                Repaint();
            }

            if (GUILayout.Button("Refresh Preview"))
            {
                system.RefreshWeatherBlendingSystem();
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
        }

        private bool DrawScheduleEntry(
            int index,
            WeatherScheduleDiagnosticReport report)
        {
            SerializedProperty entry = schedulesProp.GetArrayElementAtIndex(index);
            SerializedProperty enabled = entry.FindPropertyRelative("enabled");
            SerializedProperty controller = entry.FindPropertyRelative("controller");
            SerializedProperty fullDay = entry.FindPropertyRelative("fullDay");
            SerializedProperty start = entry.FindPropertyRelative("startHour");
            SerializedProperty end = entry.FindPropertyRelative("endHour");
            SerializedProperty blendIn = entry.FindPropertyRelative("blendInHours");
            SerializedProperty blendOut = entry.FindPropertyRelative("blendOutHours");
            SerializedProperty easing = entry.FindPropertyRelative("easing");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(enabled, GUIContent.none, GUILayout.Width(18f));
            EditorGUILayout.PropertyField(controller, new GUIContent($"Entry {index + 1}"));
            if (GUILayout.Button("−", GUILayout.Width(24f)))
            {
                schedulesProp.DeleteArrayElementAtIndex(index);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return true;
            }
            EditorGUILayout.EndHorizontal();

            var controllerValue = controller.objectReferenceValue as DawnWeatherController;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    "Active Preset",
                    controllerValue != null ? controllerValue.ActivePreset : null,
                    typeof(DawnWeatherPreset),
                    false);
            }

            EditorGUILayout.PropertyField(fullDay, new GUIContent("Full Day"));
            if (!fullDay.boolValue)
            {
                EditorGUILayout.BeginHorizontal();
                DrawHourField("Start", start, false);
                DrawHourField("End", end, true);
                EditorGUILayout.EndHorizontal();
                float duration = WeatherScheduleWeightResolver.ForwardDistance(
                    start.floatValue,
                    end.floatValue);
                string crossing = start.floatValue > end.floatValue
                    ? " • crosses midnight"
                    : string.Empty;
                EditorGUILayout.LabelField(
                    $"Duration {duration:0.##} h{crossing}",
                    EditorStyles.miniLabel);
            }

            using (new EditorGUI.DisabledScope(fullDay.boolValue))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(blendIn, new GUIContent("Blend In"));
                EditorGUILayout.PropertyField(blendOut, new GUIContent("Blend Out"));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(easing, new GUIContent("Easing"));
            }

            showDebugWeights = EditorGUILayout.Foldout(
                showDebugWeights,
                "Live Weight",
                true);
            if (showDebugWeights)
            {
                WeatherContributionInfo? current = FindContribution(index);
                EditorGUILayout.LabelField(
                    current.HasValue
                        ? $"Raw {current.Value.RawWeight:0.###}  •  Normalized {current.Value.NormalizedWeight:P1}"
                        : "Raw 0  •  Normalized 0%",
                    EditorStyles.miniLabel);
            }

            for (int issueIndex = 0; issueIndex < report.Issues.Count; issueIndex++)
            {
                WeatherScheduleIssue issue = report.Issues[issueIndex];
                if (issue.EntryIndex == index)
                {
                    EditorGUILayout.HelpBox(
                        $"[{issue.Code}] {issue.Message}",
                        ToMessageType(issue.Severity));
                }
            }

            EditorGUILayout.EndVertical();
            return false;
        }

        private static void DrawHourField(
            string label,
            SerializedProperty property,
            bool allowEndOfDay)
        {
            EditorGUI.BeginChangeCheck();
            string text = EditorGUILayout.DelayedTextField(
                label,
                FormatHour(property.floatValue, allowEndOfDay));
            if (EditorGUI.EndChangeCheck() &&
                TryParseHour(text, allowEndOfDay, out float parsed))
            {
                property.floatValue = parsed;
            }
        }

        private void AddScheduleEntry()
        {
            int index = schedulesProp.arraySize;
            schedulesProp.InsertArrayElementAtIndex(index);
            SerializedProperty entry = schedulesProp.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("controller").objectReferenceValue = null;
            entry.FindPropertyRelative("enabled").boolValue = true;
            entry.FindPropertyRelative("fullDay").boolValue = false;
            entry.FindPropertyRelative("startHour").floatValue = 0f;
            entry.FindPropertyRelative("endHour").floatValue = 24f;
            entry.FindPropertyRelative("blendInHours").floatValue = 0f;
            entry.FindPropertyRelative("blendOutHours").floatValue = 0f;
            entry.FindPropertyRelative("easing").animationCurveValue = null;
        }

        private void DrawDiagnostics(WeatherScheduleDiagnosticReport report)
        {
            if (report.Issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Schedule validation passed.", MessageType.Info);
                return;
            }

            for (int index = 0; index < report.Issues.Count; index++)
            {
                WeatherScheduleIssue issue = report.Issues[index];
                if (issue.EntryIndex >= 0) continue;
                EditorGUILayout.HelpBox(
                    $"[{issue.Code}] {issue.Message}",
                    ToMessageType(issue.Severity));
            }
        }

        private void DrawTimeSettings(DawnTODSystem system)
        {
            showTimeSettings = EditorGUILayout.Foldout(
                showTimeSettings,
                "Time Control",
                true);
            if (!showTimeSettings) return;

            EditorGUILayout.Slider(timeOfDayProp, 0f, 24f, new GUIContent("Time of Day"));
            EditorGUILayout.LabelField("Current", system.GetFormattedTime());
            EditorGUILayout.PropertyField(sunriseTimeProp, new GUIContent("Sunrise"));
            EditorGUILayout.PropertyField(sunsetTimeProp, new GUIContent("Sunset"));
            EditorGUILayout.PropertyField(autoAdvanceTimeProp, new GUIContent("Auto Advance"));
            EditorGUILayout.PropertyField(dayLengthInSecondsProp, new GUIContent("Day Length (Seconds)"));
            EditorGUILayout.PropertyField(timeScaleProp, new GUIContent("Time Scale"));
        }

        private void DrawOutputSettings(DawnTODSystem system)
        {
            showOutputSettings = EditorGUILayout.Foldout(
                showOutputSettings,
                "Scene Outputs",
                true);
            if (!showOutputSettings) return;

            EditorGUILayout.PropertyField(sunLightProp, new GUIContent("Sun Light"));
            EditorGUILayout.PropertyField(moonLightProp, new GUIContent("Moon Light"));
            EditorGUILayout.PropertyField(
                rainParticleSystemProp,
                new GUIContent("Rain Output"));
            if (rainParticleSystemProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "Rain presets require an explicit DawnGPUParticleSystem scene output. " +
                    "No GameObject will be created implicitly at runtime.",
                    MessageType.Warning);
                if (GUILayout.Button("Create or Assign Rain Output"))
                {
                    DawnRainOutputEditorUtility.EnsureRainOutput(system);
                    serializedObject.Update();
                }
            }
            EditorGUILayout.PropertyField(
                shadowEnableIntensityProp,
                new GUIContent("Shadow Enable Intensity"));
            EditorGUILayout.PropertyField(
                shadowHysteresisProp,
                new GUIContent("Shadow Hysteresis"));
#if USING_HDRP
            EditorGUILayout.PropertyField(hdrpVolumeProp, new GUIContent("HDRP Volume"));
#endif
        }

        private static void DrawPipelineCapability(DawnTODSystem system)
        {
            WeatherPipelineCapabilities capabilities =
                WeatherPipelineCapabilities.Current;
            EditorGUILayout.LabelField(
                "Detected Pipeline",
                capabilities.PipelineKind.ToString());
#if USING_HDRP
            EditorGUILayout.HelpBox(
                "HDRP consumes sun, moon, Physical Sky, Fog, Exposure and rain fields.",
                MessageType.Info);
            DrawHdrpVolumeDiagnostics(system);
#elif USING_URP
            EditorGUILayout.HelpBox(
                "URP currently consumes sun, moon and rain. Preset sky, fog and exposure fields are sampled for compatibility but have no URP scene output yet.",
                MessageType.Info);
#else
            EditorGUILayout.HelpBox(
                "Unknown render pipeline: directional lights and rain remain available; environment fields are skipped.",
                MessageType.Warning);
#endif
        }

#if USING_HDRP
        private static void DrawHdrpVolumeDiagnostics(DawnTODSystem system)
        {
            if (system.hdrpVolume == null || system.hdrpVolume.profile == null)
            {
                EditorGUILayout.HelpBox(
                    "HDRP environment output requires a Volume with a Profile. DawnTOD will not create or modify a profile implicitly.",
                    MessageType.Warning);
                return;
            }

            bool hasSky = system.hdrpVolume.profile.TryGet(out PhysicallyBasedSky _);
            bool hasFog = system.hdrpVolume.profile.TryGet(out Fog _);
            bool hasExposure = system.hdrpVolume.profile.TryGet(out Exposure _);
            if (!hasSky || !hasFog || !hasExposure)
            {
                EditorGUILayout.HelpBox(
                    "The HDRP Volume Profile is missing one or more required overrides: Physically Based Sky, Fog, Exposure. Add them explicitly to the profile.",
                    MessageType.Warning);
            }
        }
#endif

        private WeatherContributionInfo? FindContribution(int scheduleIndex)
        {
            for (int index = 0; index < contributions.Count; index++)
            {
                if (!contributions[index].IsFallback &&
                    contributions[index].ScheduleIndex == scheduleIndex)
                {
                    return contributions[index];
                }
            }
            return null;
        }

        private void OnUndoRedo()
        {
            if (target is DawnTODSystem system && system != null)
            {
                serializedObject.Update();
                system.ResetRainOutputResolution();
                system.RefreshWeatherBlendingSystem();
                SceneView.RepaintAll();
                Repaint();
            }
        }

        private static int CompareScheduleEntries(
            DawnTODSystem.WeatherControllerTimeRange left,
            DawnTODSystem.WeatherControllerTimeRange right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;
            return left.startHour.CompareTo(right.startHour);
        }

        private static Color GetEntryColor(int index, WeatherScheduleEntry entry)
        {
            if (entry.controller == null) return new Color(0.8f, 0.25f, 0.25f, 0.8f);
            Color color = Color.HSVToRGB(Mathf.Repeat(index * 0.173f, 1f), 0.58f, 0.85f);
            color.a = 0.82f;
            return color;
        }

        private static MessageType ToMessageType(WeatherScheduleIssueSeverity severity)
        {
            switch (severity)
            {
                case WeatherScheduleIssueSeverity.Error:
                    return MessageType.Error;
                case WeatherScheduleIssueSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }
    }
}
