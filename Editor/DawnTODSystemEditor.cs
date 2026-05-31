using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DawnTOD;

namespace DawnTODEditor
{
    [CustomEditor(typeof(DawnTODSystem))]
    public class DawnTODSystemEditor : Editor
    {
        private const float TIME_RANGE = 24f;
        private const int SLIDER_HEIGHT = 24;
        private static readonly int s_SliderControlID = "TODTimeSlider".GetHashCode();

        private SerializedProperty weatherControllersProp;
        private SerializedProperty timeOfDayProp;
        private SerializedProperty sunriseTime;
        private SerializedProperty sunsetTime;
        private SerializedProperty autoAdvanceTimeProp;
        private SerializedProperty dayLengthInSecondsProp;
        private SerializedProperty timeScaleProp;
        private SerializedProperty sunLightProp;
        private SerializedProperty moonLightProp;
#if USING_HDRP
        private SerializedProperty hdrpVolumeProp;
#endif
        private SerializedProperty controllerTimeRangesProp;

        private bool showAdvancedSettings = false;

        private void OnEnable()
        {
            weatherControllersProp = serializedObject.FindProperty("weatherControllers");
            timeOfDayProp = serializedObject.FindProperty("timeOfDay");
            sunriseTime = serializedObject.FindProperty("sunriseTime");
            sunsetTime = serializedObject.FindProperty("sunsetTime");
            autoAdvanceTimeProp = serializedObject.FindProperty("autoAdvanceTime");
            dayLengthInSecondsProp = serializedObject.FindProperty("dayLengthInSeconds");
            timeScaleProp = serializedObject.FindProperty("timeScale");
            sunLightProp = serializedObject.FindProperty("sunLight");
            moonLightProp = serializedObject.FindProperty("moonLight");
#if USING_HDRP
            hdrpVolumeProp = serializedObject.FindProperty("hdrpVolume");
#endif
            controllerTimeRangesProp = serializedObject.FindProperty("controllerTimeRanges");
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            serializedObject.Update();

            DawnTODSystem todSystem = target as DawnTODSystem;
            if (todSystem == null)
            {
                EditorGUILayout.HelpBox("TODSystem target is null!", MessageType.Error);
                return;
            }

            // ========== 预设 + 时间段调试 ==========
            EditorGUILayout.LabelField("Weather Controllers & Time Ranges", EditorStyles.boldLabel);
            if (weatherControllersProp == null || controllerTimeRangesProp == null)
            {
                EditorGUILayout.HelpBox("Controllers or Time Ranges List is invalid (null property).", MessageType.Warning);
            }
            else if (weatherControllersProp.arraySize <= 0)
            {
                EditorGUILayout.HelpBox("No WeatherControllers found in scene.", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < weatherControllersProp.arraySize; i++)
                {
                    SerializedProperty controllerProp = weatherControllersProp.GetArrayElementAtIndex(i);
                    if (controllerProp == null) continue;

                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        if (controllerProp != null)
                        {
                            EditorGUILayout.PropertyField(controllerProp, new GUIContent($"[{i}] Controller"));
                        }
                        else
                        {
                            EditorGUILayout.LabelField($"[{i}] Controller", "Null Reference");
                        }
                        EditorGUI.EndDisabledGroup();

                        DawnWeatherController controller = controllerProp?.objectReferenceValue as DawnWeatherController;
                        SerializedProperty timeRangeProp = null;

                        if (controller != null && controllerTimeRangesProp != null)
                        {
                            for (int j = 0; j < controllerTimeRangesProp.arraySize; j++)
                            {
                                var elem = controllerTimeRangesProp.GetArrayElementAtIndex(j);
                                if (elem == null) continue;

                                var ctrlProp = elem.FindPropertyRelative("controller");
                                if (ctrlProp != null && ctrlProp.objectReferenceValue == controller)
                                {
                                    timeRangeProp = elem;
                                    break;
                                }
                            }
                        }

                        //绘制startHour输入框
                        EditorGUI.BeginDisabledGroup(timeRangeProp == null);
                        float startValue = 0f;
                        if (timeRangeProp != null)
                        {
                            var startProp = timeRangeProp.FindPropertyRelative("startHour");
                            if (startProp != null)
                            {
                                startValue = EditorGUILayout.FloatField(startProp.floatValue, GUILayout.Width(50));
                                startValue = Mathf.Clamp(startValue, 0f, 24f);
                                startProp.floatValue = startValue;
                            }
                            else
                            {
                                EditorGUILayout.FloatField(0f, GUILayout.Width(50));
                            }
                        }
                        else
                        {
                            EditorGUILayout.FloatField(0f, GUILayout.Width(50));
                        }
                        EditorGUI.EndDisabledGroup();

                        EditorGUILayout.LabelField("~", GUILayout.Width(20));

                        EditorGUI.BeginDisabledGroup(timeRangeProp == null);
                        float endValue = 24f;
                        if (timeRangeProp != null)
                        {
                            var endProp = timeRangeProp.FindPropertyRelative("endHour");
                            var startProp = timeRangeProp.FindPropertyRelative("startHour");
                            if (endProp != null && startProp != null)
                            {
                                endValue = EditorGUILayout.FloatField(endProp.floatValue, GUILayout.Width(50));
                                endValue = Mathf.Clamp(endValue, 0f, 24f);

                                if (endValue <= startProp.floatValue)
                                {
                                    endValue = startProp.floatValue + 0.1f;
                                }
                                endProp.floatValue = endValue;
                            }
                            else
                            {
                                EditorGUILayout.FloatField(24f, GUILayout.Width(50));
                            }
                        }
                        else
                        {
                            EditorGUILayout.FloatField(24f, GUILayout.Width(50));
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                }
            }
            EditorGUILayout.Space(5);

            // ========== 时间控制 ==========
            EditorGUILayout.LabelField("Time Control", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            if (timeOfDayProp != null)
            {
                DrawTimeSlider(todSystem);
            }
            else
            {
                EditorGUILayout.HelpBox("TimeOfDay property not found!", MessageType.Warning);
            }
            EditorGUILayout.Space(3);

            // ========== 日出日落时间 ==========
            if (sunriseTime != null && sunsetTime != null)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    float originalSunrise = sunriseTime.floatValue;
                    float originalSunset = sunsetTime.floatValue;

                    float displayedSunrise = Mathf.Clamp(originalSunrise, 0f, 24f);
                    float displayedSunset = Mathf.Clamp(originalSunset, 0f, 24f);

                    EditorGUI.BeginChangeCheck();
                    float newSunrise = EditorGUILayout.FloatField("Sunrise Time", displayedSunrise, GUILayout.ExpandWidth(false));
                    float newSunset = EditorGUILayout.FloatField("Sunset Time", displayedSunset, GUILayout.ExpandWidth(false));

                    if (EditorGUI.EndChangeCheck())
                    {
                        newSunrise = Mathf.Clamp(newSunrise, 0f, 24f);
                        newSunset = Mathf.Clamp(newSunset, 0f, 24f);

                        if (newSunrise >= newSunset)
                        {
                            newSunrise = Mathf.Clamp(newSunrise, 0f, 23.9f);
                            newSunset = newSunrise + 0.1f;
                        }

                        sunriseTime.floatValue = newSunrise;
                        sunsetTime.floatValue = newSunset;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("Sunrise/Sunset time properties not found!", MessageType.Warning);
            }

            // ========== 自动流逝 ==========
            if (autoAdvanceTimeProp != null)
            {
                EditorGUILayout.PropertyField(autoAdvanceTimeProp, new GUIContent("Auto Advance"));

                if (autoAdvanceTimeProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    if (dayLengthInSecondsProp != null)
                    {
                        EditorGUILayout.PropertyField(dayLengthInSecondsProp, new GUIContent("Day Length (sec)"));
                    }
                    if (timeScaleProp != null)
                    {
                        EditorGUILayout.PropertyField(timeScaleProp, new GUIContent("Time Scale"));
                    }
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("AutoAdvanceTime property not found!", MessageType.Warning);
            }
            EditorGUILayout.Space(5);

            // ========== 光源引用 ==========
            EditorGUILayout.LabelField("Lights", EditorStyles.boldLabel);
            if (sunLightProp != null)
            {
                EditorGUILayout.PropertyField(sunLightProp, new GUIContent("Sun Light"));
            }
            else
            {
                EditorGUILayout.LabelField("Sun Light", "Null Reference");
            }
            if (moonLightProp != null)
            {
                EditorGUILayout.PropertyField(moonLightProp, new GUIContent("Moon Light"));
            }
            else
            {
                EditorGUILayout.LabelField("Moon Light", "Null Reference");
            }
            EditorGUILayout.Space(5);

            // ========== HDRP ==========
#if USING_HDRP
            EditorGUILayout.LabelField("HDRP", EditorStyles.boldLabel);
            if (hdrpVolumeProp != null)
            {
                EditorGUILayout.PropertyField(hdrpVolumeProp, new GUIContent("HDRP Volume"));
            }
            else
            {
                EditorGUILayout.LabelField("HDRP Volume", "Null Reference");
            }
            EditorGUILayout.Space(5);
#endif

            // ========== 高级设置 ==========
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced", true);
            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                DrawAdvancedInfo(todSystem);
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();

            todSystem.RefreshWeatherBlendingSystem();

            if (EditorGUI.EndChangeCheck())
            {
                // 调用TODSystem的公开刷新方法，同步面板修改到运行逻辑
                todSystem.RefreshWeatherBlendingSystem();
                EditorUtility.SetDirty(target);
            }
        }

        private void DrawTimeSlider(DawnTODSystem todsystem)
        {
            if (todsystem == null || timeOfDayProp == null) return;

            float currentTime = timeOfDayProp.floatValue;

            Rect sliderRect = EditorGUILayout.GetControlRect(GUILayout.Height(SLIDER_HEIGHT));

            if (sunriseTime != null && sunsetTime != null)
            {
                DrawDayNightGradient(sliderRect, sunriseTime.floatValue, sunsetTime.floatValue);
            }
            else
            {
                EditorGUI.DrawRect(sliderRect, Color.gray);
            }

            // 计算手柄位置
            float handleWidth = 4f;
            float normalizedTime = Mathf.Clamp01(currentTime / TIME_RANGE);
            float handleX = Mathf.Lerp(sliderRect.x, sliderRect.xMax - handleWidth, normalizedTime);
            Rect handleRect = new Rect(handleX, sliderRect.y, handleWidth, sliderRect.height);

            Event e = Event.current;
            int controlID = GUIUtility.GetControlID(s_SliderControlID, FocusType.Passive, sliderRect);

            switch (e.GetTypeForControl(controlID))
            {
                case EventType.MouseDown:
                    if (sliderRect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = controlID;
                        SetTimeFromMousePosition(sliderRect, e.mousePosition.x, handleWidth);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlID)
                    {
                        SetTimeFromMousePosition(sliderRect, e.mousePosition.x, handleWidth);
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlID)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }

            // 绘制手柄
            EditorGUI.DrawRect(handleRect, Color.white);

            // 绘制时间刻度
            DrawTimeMarkers(sliderRect);

            // 时间文本
            int hours = Mathf.FloorToInt(currentTime);
            int minutes = Mathf.FloorToInt((currentTime - hours) * 60f);
            string timeText = $"{hours:D2}:{minutes:D2}";

            GUIStyle centerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = IsNightTime(todsystem, currentTime) ? Color.cyan : Color.yellow }
            };

            Rect textRect = new Rect(sliderRect.center.x - 30, sliderRect.yMax + 2, 60, 16);
            GUI.Label(textRect, timeText, centerStyle);

            EditorGUILayout.Space(18);
        }

        private void DrawDayNightGradient(Rect rect, float sunrise, float sunset)
        {
            Color dayColor = new Color(0.8f, 0.8f, 0.2f, 1f);
            Color nightColor = new Color(0.2f, 0.2f, 0.45f, 1f);

            float normalizeSunrise = Mathf.Clamp01(sunrise / 24f);
            float normalizeSunset = Mathf.Clamp01(sunset / 24f);

            if (normalizeSunrise < normalizeSunset)
            {
                // 左侧夜晚
                if (normalizeSunrise > 0)
                {
                    Rect leftNight = new Rect(rect.x, rect.y, rect.width * normalizeSunrise, rect.height);
                    EditorGUI.DrawRect(leftNight, nightColor);
                }

                // 中间白天
                Rect day = new Rect(rect.x + rect.width * normalizeSunrise, rect.y, rect.width * (normalizeSunset - normalizeSunrise), rect.height);
                EditorGUI.DrawRect(day, dayColor);

                // 右侧夜晚
                if (normalizeSunset < 1)
                {
                    Rect rightNight = new Rect(rect.x + rect.width * normalizeSunset, rect.y, rect.width * (1 - normalizeSunset), rect.height);
                    EditorGUI.DrawRect(rightNight, nightColor);
                }
            }
            else
            {
                EditorGUI.DrawRect(rect, nightColor);
            }
        }

        private void DrawTimeMarkers(Rect sliderRect)
        {
            GUIStyle markerStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };

            // 每6小时一个刻度
            for (int hour = 0; hour <= 24; hour += 6)
            {
                float x = sliderRect.x + sliderRect.width * (hour / 24f);
                Rect markerRect = new Rect(x - 10, sliderRect.y - 14, 20, 12);
                GUI.Label(markerRect, hour.ToString(), markerStyle);
            }
        }

        private void SetTimeFromMousePosition(Rect sliderRect, float mouseX, float handleWidth)
        {
            if (timeOfDayProp == null) return;

            float minX = sliderRect.x;
            float maxX = sliderRect.xMax - handleWidth;
            float clampedX = Mathf.Clamp(mouseX, minX, maxX);
            float normalized = Mathf.InverseLerp(minX, maxX, clampedX);
            timeOfDayProp.floatValue = normalized * TIME_RANGE;
        }

        private void DrawAdvancedInfo(DawnTODSystem todsystem)
        {
            if (todsystem == null) return;

            EditorGUILayout.LabelField("Normalized Time", todsystem.NormalizedTime.ToString("F4"));
            EditorGUILayout.LabelField("Is Night", todsystem.IsNight.ToString());
            EditorGUILayout.LabelField("Formatted Time", todsystem.GetFormattedTime());
        }

        private bool IsNightTime(DawnTODSystem todsystem, float time)
        {
            if (todsystem == null) return true;
            float sunrise = todsystem.SunRaiseTime;
            float sunset = todsystem.SunSetTime;
            return time < sunrise || time >= sunset;
        }
    }
}