using UnityEditor;
using UnityEngine;
using NeuroTOD;

namespace NeuroTODEditor
{
    [CustomEditor(typeof(NeuroTODController))]
    public class NeuroTODControllerEditor : Editor
    {
        private const float TIME_RANGE = 24f;
        private const int SLIDER_HEIGHT = 24;
        private const int CURVE_HEIGHT = 40;
        private static readonly int s_SliderControlID = "NeuroTODTimeSlider".GetHashCode();

        private SerializedProperty activePresetProp;
        private SerializedProperty timeOfDayProp;
        private SerializedProperty sunriseTime;
        private SerializedProperty sunsetTime;
        private SerializedProperty autoAdvanceTimeProp;
        private SerializedProperty dayLengthInSecondsProp;
        private SerializedProperty timeScaleProp;
        private SerializedProperty sunLightProp;
        private SerializedProperty moonLightProp;
        private SerializedProperty hdrpVolumeProp;

        private bool showCurvePreview = true;
        private bool showAdvancedSettings = false;

        private void OnEnable()
        {
            activePresetProp = serializedObject.FindProperty("activePreset");
            timeOfDayProp = serializedObject.FindProperty("timeOfDay");
            sunriseTime = serializedObject.FindProperty("sunriseTime");
            sunsetTime = serializedObject.FindProperty("sunsetTime");
            autoAdvanceTimeProp = serializedObject.FindProperty("autoAdvanceTime");
            dayLengthInSecondsProp = serializedObject.FindProperty("dayLengthInSeconds");
            timeScaleProp = serializedObject.FindProperty("timeScale");
            sunLightProp = serializedObject.FindProperty("sunLight");
            moonLightProp = serializedObject.FindProperty("moonLight");
            hdrpVolumeProp = serializedObject.FindProperty("hdrpVolume");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            NeuroTODController controller = (NeuroTODController)target;

            // ========== 预设 ==========
            EditorGUILayout.LabelField("Preset", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(activePresetProp, new GUIContent("Active Preset"));
            EditorGUILayout.Space(5);

            // ========== 时间控制 ==========
            EditorGUILayout.LabelField("Time Control", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            DrawTimeSlider(controller);
            EditorGUILayout.Space(3);
            
            // ========== 日出日落时间 ==========
            EditorGUILayout.BeginHorizontal();
            {
                // 保存原始值（用于比较）
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
            
            // ========== 自动流逝 ==========
            EditorGUILayout.PropertyField(autoAdvanceTimeProp, new GUIContent("Auto Advance"));
            
            if (autoAdvanceTimeProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(dayLengthInSecondsProp, new GUIContent("Day Length (sec)"));
                EditorGUILayout.PropertyField(timeScaleProp, new GUIContent("Time Scale"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(5);

            // ========== 光源引用 ==========
            EditorGUILayout.LabelField("Lights", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(sunLightProp, new GUIContent("Sun Light"));
            EditorGUILayout.PropertyField(moonLightProp, new GUIContent("Moon Light"));
            EditorGUILayout.Space(5);

            // ========== HDRP ==========
            EditorGUILayout.LabelField("HDRP", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(hdrpVolumeProp, new GUIContent("HDRP Volume"));
            EditorGUILayout.Space(5);

            // ========== 曲线预览 ==========
            TODPreset preset = activePresetProp.objectReferenceValue as TODPreset;
            if (preset != null)
            {
                showCurvePreview = EditorGUILayout.Foldout(showCurvePreview, "Curve Preview", true);
                if (showCurvePreview)
                {
                    EditorGUI.indentLevel++;
                    DrawCurvePreview(preset, controller.NormalizedTime);
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("请设置 Active Preset 以查看曲线预览", MessageType.Info);
            }

            // ========== 高级设置 ==========
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced", true);
            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                DrawAdvancedInfo(controller);
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTimeSlider(NeuroTODController controller)
        {
            float currentTime = timeOfDayProp.floatValue;

            // 滑块背景
            Rect sliderRect = EditorGUILayout.GetControlRect(GUILayout.Height(SLIDER_HEIGHT));
            
            // 绘制昼夜渐变背景
            DrawDayNightGradient(sliderRect,sunriseTime.floatValue,sunsetTime.floatValue);

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
                normal = { textColor = IsNightTime(controller,currentTime) ? Color.cyan : Color.yellow }
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
                    Rect leftNight = new Rect(
                        rect.x,
                        rect.y,
                        rect.width * normalizeSunrise,
                        rect.height
                    );
                    EditorGUI.DrawRect(leftNight, nightColor);
                }

                // 中间白天
                Rect day = new Rect(
                    rect.x + rect.width * normalizeSunrise,
                    rect.y,
                    rect.width * (normalizeSunset - normalizeSunrise),
                    rect.height
                );
                EditorGUI.DrawRect(day, dayColor);

                // 右侧夜晚
                if (normalizeSunset < 1)
                {
                    Rect rightNight = new Rect(
                        rect.x + rect.width * normalizeSunset,
                        rect.y,
                        rect.width * (1 - normalizeSunset),
                        rect.height
                    );
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
            float minX = sliderRect.x;
            float maxX = sliderRect.xMax - handleWidth;
            float clampedX = Mathf.Clamp(mouseX, minX, maxX);
            float normalized = Mathf.InverseLerp(minX, maxX, clampedX);
            timeOfDayProp.floatValue = normalized * TIME_RANGE;
        }

        private void DrawCurvePreview(TODPreset preset, float normalizedTime)
        {
            // 太阳强度
            DrawCurveField("Sun Intensity", preset.sunIntensityCurve, normalizedTime);
            DrawGradientField("Sun Color", preset.sunColorGradient, normalizedTime);
            
            // 月亮强度
            DrawCurveField("Moon Intensity", preset.moonIntensityCurve, normalizedTime);
            DrawGradientField("Moon Color", preset.moonColorGradient, normalizedTime);
            
            // 星空
            DrawCurveField("Star Emission", preset.starEmissionCurve, normalizedTime);
            
            // 雾效
            DrawCurveField("Fog Distance", preset.fogDistanceCurve, normalizedTime);
            DrawGradientField("Fog Color", preset.fogColorGradient, normalizedTime);
        }

        private void DrawCurveField(string label, AnimationCurve curve, float normalizedTime)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(100));
            
            Rect curveRect = EditorGUILayout.GetControlRect(GUILayout.Height(CURVE_HEIGHT));
            EditorGUI.CurveField(curveRect, curve);
            
            // 绘制当前时间指示线
            float x = curveRect.x + curveRect.width * normalizedTime;
            EditorGUI.DrawRect(new Rect(x, curveRect.y, 1, curveRect.height), Color.red);
            
            // 显示当前值
            float value = curve.Evaluate(normalizedTime);
            EditorGUILayout.LabelField(value.ToString("F1"), GUILayout.Width(60));
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGradientField(string label, Gradient gradient, float normalizedTime)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(100));
            
            Rect gradientRect = EditorGUILayout.GetControlRect(GUILayout.Height(20));
            EditorGUI.GradientField(gradientRect, gradient);
            
            // 绘制当前时间指示线
            float x = gradientRect.x + gradientRect.width * normalizedTime;
            EditorGUI.DrawRect(new Rect(x, gradientRect.y, 1, gradientRect.height), Color.red);
            
            // 显示当前颜色
            Color currentColor = gradient.Evaluate(normalizedTime);
            EditorGUI.DrawRect(EditorGUILayout.GetControlRect(GUILayout.Width(60), GUILayout.Height(20)), currentColor);
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAdvancedInfo(NeuroTODController controller)
        {
            EditorGUILayout.LabelField("Normalized Time", controller.NormalizedTime.ToString("F4"));
            EditorGUILayout.LabelField("Is Night", controller.IsNight.ToString());
            EditorGUILayout.LabelField("Formatted Time", controller.GetFormattedTime());
        }

        private bool IsNightTime(NeuroTODController controller,float time)
        {
            if (controller == null) return true;
            float sunrise = controller.SunRaiseTime;
            float sunset = controller.SunSetTime;
            return time < sunrise || time >= sunset;
        }
    }
}
