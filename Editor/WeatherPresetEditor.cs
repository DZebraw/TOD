//using UnityEditor;
//using UnityEngine;
//using TOD;

//namespace TODEditor
//{
//    [CustomEditor(typeof(WeatherPreset))]
//    public class WeatherPresetEditor : Editor
//    {
//        private const int CURVE_HEIGHT = 24;
//        private const int GRADIENT_HEIGHT = 24;

//        //用于折叠
//        private bool showSunSettings = true;
//        private bool showMoonSettings = true;
//        private bool showSkySettings = true;
//        private bool showFogSettings = true;
//        private bool showExposureSettings = true;
//        private bool showRainSettings = true;
//        //private bool showTimeSettings = true;

//        private float previewTime = 0.5f;

//        public override void OnInspectorGUI()
//        {
//            serializedObject.Update();

//            WeatherPreset preset = (WeatherPreset)target;

//            // ========== 预览时间滑块 ==========
//            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
//            previewTime = EditorGUILayout.Slider("Preview Time", previewTime, 0f, 1f);
            
//            string timeText = GetTimeString(previewTime);
//            EditorGUILayout.LabelField("Time", timeText);
//            EditorGUILayout.Space(10);

//            // ========== 太阳设置 ==========
//            showSunSettings = EditorGUILayout.Foldout(showSunSettings, "Sun Settings", true, EditorStyles.foldoutHeader);
//            if (showSunSettings)
//            {
//                EditorGUI.indentLevel++;
//                DrawCurveWithPreview("Azimuth", serializedObject.FindProperty("sunAzimuthCurve"), previewTime, "°");
//                DrawCurveWithPreview("Elevation", serializedObject.FindProperty("sunElevationCurve"), previewTime, "°");
//                DrawCurveWithPreview("Intensity", serializedObject.FindProperty("sunIntensityCurve"), previewTime, " lux");
//                DrawGradientWithPreview("Color", serializedObject.FindProperty("sunColorGradient"), previewTime);
//                EditorGUI.indentLevel--;
//            }
//            EditorGUILayout.Space(5);

//            // ========== 月亮设置 ==========
//            showMoonSettings = EditorGUILayout.Foldout(showMoonSettings, "Moon Settings", true, EditorStyles.foldoutHeader);
//            if (showMoonSettings)
//            {
//                EditorGUI.indentLevel++;
//                DrawCurveWithPreview("Azimuth", serializedObject.FindProperty("moonAzimuthCurve"), previewTime, "°");
//                DrawCurveWithPreview("Elevation", serializedObject.FindProperty("moonElevationCurve"), previewTime, "°");
//                DrawCurveWithPreview("Intensity", serializedObject.FindProperty("moonIntensityCurve"), previewTime, " lux");
//                DrawGradientWithPreview("Color", serializedObject.FindProperty("moonColorGradient"), previewTime);
//                EditorGUI.indentLevel--;
//            }
//            EditorGUILayout.Space(5);

//            // ========== 天空设置 ==========
//            showSkySettings = EditorGUILayout.Foldout(showSkySettings, "Sky Settings", true, EditorStyles.foldoutHeader);
//            if (showSkySettings)
//            {
//                EditorGUI.indentLevel++;
//                DrawCurveWithPreview("Star Emission", serializedObject.FindProperty("starEmissionCurve"), previewTime, "");
//                EditorGUI.indentLevel--;
//            }
//            EditorGUILayout.Space(5);

//            // ========== 雾效设置 ==========
//            showFogSettings = EditorGUILayout.Foldout(showFogSettings, "Fog Settings", true, EditorStyles.foldoutHeader);
//            if (showFogSettings)
//            {
//                EditorGUI.indentLevel++;
//                DrawCurveWithPreview("Height", serializedObject.FindProperty("fogHeightCurve"), previewTime, "");
//                DrawCurveWithPreview("Distance (MFP)", serializedObject.FindProperty("fogDistanceCurve"), previewTime, " m");
//                DrawGradientWithPreview("Color", serializedObject.FindProperty("fogColorGradient"), previewTime);
//                EditorGUI.indentLevel--;
//            }
//            EditorGUILayout.Space(5);
            
//            // ========== 曝光设置 ==========
//            showExposureSettings = EditorGUILayout.Foldout(showExposureSettings, "Exposure Settings", true, EditorStyles.foldoutHeader);
//            if (showExposureSettings)
//            {
//                EditorGUI.indentLevel++;
//                DrawCurveWithPreview("ExposureCompensation", serializedObject.FindProperty("exposureCompensationCurve"), previewTime, "");
//                EditorGUI.indentLevel--;
//            }
//            EditorGUILayout.Space(5);

//            //=========雨天设置============
//            showRainSettings = EditorGUILayout.Foldout(showRainSettings, "Rain Settings", true, EditorStyles.foldoutHeader);
//            if (showRainSettings)
//            {
//                EditorGUI.indentLevel++;
//                SerializedProperty rainyEnable = serializedObject.FindProperty("rainyEnable");
//                SerializedProperty rainyPrefab = serializedObject.FindProperty("rainyPrefab");
//                EditorGUILayout.PropertyField(rainyEnable, new GUIContent("Enable Rainy"));
//                EditorGUILayout.PropertyField(rainyPrefab, new GUIContent("Rainy Prefab"));
//                EditorGUI.indentLevel--;
//            }
//            EditorGUILayout.Space(5);


//            // ========== 时间控制 ==========
//            // showTimeSettings = EditorGUILayout.Foldout(showTimeSettings, "Time Control", true, EditorStyles.foldoutHeader);
//            // if (showTimeSettings)
//            // {
//            //     EditorGUI.indentLevel++;
//            //     DrawCurveWithPreview("Time Remap", serializedObject.FindProperty("timeRemapCurve"), previewTime, "");
//            //     
//            //     // 显示重映射后的时间
//            //     float remappedTime = preset.GetRemappedTime(previewTime);
//            //     EditorGUILayout.LabelField("Remapped Time", remappedTime.ToString("F4"));
//            //     EditorGUI.indentLevel--;
//            // }

//            serializedObject.ApplyModifiedProperties();
//        }

//        private void DrawCurveWithPreview(string label, SerializedProperty curveProp, float time, string unit)
//        {
//            EditorGUILayout.BeginHorizontal();
            
//            EditorGUILayout.LabelField(label, GUILayout.Width(120));
            
//            Rect curveRect = EditorGUILayout.GetControlRect(GUILayout.Height(CURVE_HEIGHT));
//            EditorGUI.PropertyField(curveRect, curveProp, GUIContent.none);
            
//            // 绘制时间指示线
//            float x = curveRect.x + curveRect.width * time;
//            EditorGUI.DrawRect(new Rect(x, curveRect.y, 1, curveRect.height), new Color(1f, 0.3f, 0.3f, 0.8f));
            
//            // 显示当前值
//            AnimationCurve curve = curveProp.animationCurveValue;
//            float value = curve.Evaluate(time);
//            EditorGUILayout.LabelField(value.ToString("F1") + unit, GUILayout.Width(80));
            
//            EditorGUILayout.EndHorizontal();
//        }

//        private void DrawGradientWithPreview(string label, SerializedProperty gradientProp, float time)
//        {
//            EditorGUILayout.BeginHorizontal();
            
//            EditorGUILayout.LabelField(label, GUILayout.Width(120));
            
//            Rect gradientRect = EditorGUILayout.GetControlRect(GUILayout.Height(GRADIENT_HEIGHT));
//            EditorGUI.PropertyField(gradientRect, gradientProp, GUIContent.none);
            
//            // 绘制时间指示线
//            float x = gradientRect.x + gradientRect.width * time;
//            EditorGUI.DrawRect(new Rect(x, gradientRect.y, 1, gradientRect.height), new Color(1f, 0.3f, 0.3f, 0.8f));
            
//            // 显示当前颜色
//            Gradient gradient = GetGradientValue(gradientProp);
//            if (gradient != null)
//            {
//                Color currentColor = gradient.Evaluate(time);
//                Rect colorRect = EditorGUILayout.GetControlRect(GUILayout.Width(80), GUILayout.Height(GRADIENT_HEIGHT));
//                EditorGUI.DrawRect(colorRect, currentColor);
//            }
            
//            EditorGUILayout.EndHorizontal();
//        }

//        private Gradient GetGradientValue(SerializedProperty prop)
//        {
//            // Unity 的 SerializedProperty 不直接支持 Gradient，需要通过反射获取
//            System.Reflection.PropertyInfo propertyInfo = typeof(SerializedProperty).GetProperty(
//                "gradientValue",
//                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
//            );
            
//            if (propertyInfo != null)
//            {
//                return propertyInfo.GetValue(prop) as Gradient;
//            }
//            return null;
//        }

//        private string GetTimeString(float normalizedTime)
//        {
//            float hours = normalizedTime * 24f;
//            int h = Mathf.FloorToInt(hours);
//            int m = Mathf.FloorToInt((hours - h) * 60f);
//            return $"{h:D2}:{m:D2}";
//        }
//    }
//}
