using UnityEditor;
using UnityEngine;
using DawnTOD;

namespace DawnTODEditor
{
    [CustomEditor(typeof(DawnWeatherController))]
    public class DawnWeatherControllerEditor : Editor
    {
        private const float TIME_RANGE = 24f;
        private const int SLIDER_HEIGHT = 24;
        private const int CURVE_HEIGHT = 24;
        private const float FIELD_LABEL_WIDTH = 130f;
        private static readonly int s_SliderControlID = "TODTimeSlider".GetHashCode();

        private SerializedProperty activePresetProp;
        private SerializedProperty timeOfDayProp;
#if USING_URP
        private SerializedProperty fogEnabledProp;
        private SerializedProperty fogAffectSkyProp;
#endif

        private SerializedObject presetSerializedObject;
        private SerializedProperty sunAzimuthCurveProp;
        private SerializedProperty sunElevationProp;
        private SerializedProperty sunIntensityCurveProp;
        private SerializedProperty sunColorGradientProp;
        private SerializedProperty moonAzimuthCurveProp;
        private SerializedProperty moonElevationProp;
        private SerializedProperty moonIntensityCurveProp;
        private SerializedProperty moonColorGradientProp;
#if USING_HDRP || USING_URP
        private SerializedProperty starEmissionCurveProp;
        private SerializedProperty fogHeightCurveProp;
        private SerializedProperty fogDistanceCurveProp;
        private SerializedProperty fogColorGradientProp;
#endif
#if USING_HDRP
        private SerializedProperty exposureCompensationCurveProp;
#endif
        private SerializedProperty rainySpeedCurveProp;
        private SerializedProperty precipitationAmountCurveProp;
        private SerializedProperty rainDensityCurveProp;
        private SerializedProperty rainWindZRotationCurveProp;

        private bool showSunSettings = true;
        private bool showMoonSettings = true;
        private bool showSkySettings = true;
        private bool showFogSettings = true;
#if USING_HDRP
        private bool showExposureSettings = true;
#endif
        private bool showRainySettings = true;
        private DawnTODSystem debugPreviewSystem;
        private DawnWeatherController debugPreviewController;

        private void OnEnable()
        {
            activePresetProp = serializedObject.FindProperty("activePreset");
            timeOfDayProp = serializedObject.FindProperty("timeOfDay");
#if USING_URP
            fogEnabledProp = serializedObject.FindProperty("fogEnabled");
            fogAffectSkyProp = serializedObject.FindProperty("fogAffectSky");
#endif

            UpdatePresetSerializedProperties();
        }

        private void OnDisable()
        {
            RestoreDebugWeatherPreview();
        }

        private void UpdatePresetSerializedProperties()
        {
            DawnWeatherPreset preset = activePresetProp.objectReferenceValue as DawnWeatherPreset;
            if (preset == null)
            {
                presetSerializedObject = null;
                return;
            }

            presetSerializedObject = new SerializedObject(preset);

            sunAzimuthCurveProp = presetSerializedObject.FindProperty("sunAzimuthCurve");
            sunElevationProp = presetSerializedObject.FindProperty("sunElevationCurve");
            sunIntensityCurveProp = presetSerializedObject.FindProperty("sunIntensityCurve");
            sunColorGradientProp = presetSerializedObject.FindProperty("sunColorGradient");
            moonAzimuthCurveProp = presetSerializedObject.FindProperty("moonAzimuthCurve");
            moonElevationProp = presetSerializedObject.FindProperty("moonElevationCurve");
            moonIntensityCurveProp = presetSerializedObject.FindProperty("moonIntensityCurve");
            moonColorGradientProp = presetSerializedObject.FindProperty("moonColorGradient");
#if USING_HDRP || USING_URP
            starEmissionCurveProp = presetSerializedObject.FindProperty("starEmissionCurve");
            fogHeightCurveProp = presetSerializedObject.FindProperty("fogHeightCurve");
            fogDistanceCurveProp = presetSerializedObject.FindProperty("fogDistanceCurve");
            fogColorGradientProp = presetSerializedObject.FindProperty("fogColorGradient");
#endif
#if USING_HDRP
            exposureCompensationCurveProp = presetSerializedObject.FindProperty("exposureCompensationCurve");
#endif
            rainySpeedCurveProp = presetSerializedObject.FindProperty("rainySpeedCurve");
            precipitationAmountCurveProp = presetSerializedObject.FindProperty("precipitationAmountCurve");
            rainDensityCurveProp = presetSerializedObject.FindProperty("rainDensityCurve");
            rainWindZRotationCurveProp = presetSerializedObject.FindProperty("rainWindZRotationCurve");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DawnWeatherController controller = (DawnWeatherController)target;

            // ========== 预设 ==========
            EditorGUILayout.LabelField("Preset", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(activePresetProp, new GUIContent("Active Preset"));
            if (EditorGUI.EndChangeCheck())
            {
                ApplyControllerModifiedPropertiesAndRefresh(
                    serializedObject,
                    controller);
                UpdatePresetSerializedProperties();
            }
            EditorGUILayout.Space(5);

            DrawDebugWeatherPreview(controller);
            EditorGUILayout.Space(5);

            // ========== 时间控制 ==========
            EditorGUILayout.LabelField("Time Control", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            DrawTimeSlider(controller);
            EditorGUILayout.Space(3);

            // ========== 预设显示 ==========
            DawnWeatherPreset preset = activePresetProp.objectReferenceValue as DawnWeatherPreset;
            if (preset != null && presetSerializedObject != null)
            {
                presetSerializedObject.Update();

                //====Sun====
                showSunSettings = EditorGUILayout.Foldout(showSunSettings, "Sun Settings", true);
                if (showSunSettings)
                {
                    EditorGUI.indentLevel++;
                    DrawCurveField("Sun Azimation", sunAzimuthCurveProp, controller.NormalizedTime);
                    DrawCurveField("Sun Elevation", sunElevationProp, controller.NormalizedTime);
                    DrawCurveField("Sun Intensity", sunIntensityCurveProp, controller.NormalizedTime);
                    DrawGradientField("Sun Color", sunColorGradientProp, controller.NormalizedTime);
                    EditorGUI.indentLevel--;
                }
                //====Moon====
                showMoonSettings = EditorGUILayout.Foldout(showMoonSettings, "Moon Settings", true);
                if (showMoonSettings)
                {
                    EditorGUI.indentLevel++;
                    DrawCurveField("Moon Azimation", moonAzimuthCurveProp, controller.NormalizedTime);
                    DrawCurveField("Moon Elevation", moonElevationProp, controller.NormalizedTime);
                    DrawCurveField("Moon Intensity", moonIntensityCurveProp, controller.NormalizedTime);
                    DrawGradientField("Moon Color", moonColorGradientProp, controller.NormalizedTime);
                    EditorGUI.indentLevel--;
                }
#if USING_HDRP || USING_URP
                //====Sky====
                showSkySettings = EditorGUILayout.Foldout(showSkySettings, "Sky Settings", true);
                if (showSkySettings)
                {
                    EditorGUI.indentLevel++;
                    DrawCurveField("Star Emission", starEmissionCurveProp, controller.NormalizedTime);
                    EditorGUI.indentLevel--;
                }
#endif
#if USING_HDRP || USING_URP
                //====Fog====
                showFogSettings = EditorGUILayout.Foldout(showFogSettings, "Fog Settings", true);
                if (showFogSettings)
                {
                    EditorGUI.indentLevel++;
#if USING_URP
                    EditorGUILayout.PropertyField(fogEnabledProp, new GUIContent("Enable"));
                    EditorGUILayout.PropertyField(fogAffectSkyProp, new GUIContent("Affect Sky"));
#endif
                    DrawCurveField("Base Height (m)", fogHeightCurveProp, controller.NormalizedTime);
                    DrawCurveField("Mean Free Path (m)", fogDistanceCurveProp, controller.NormalizedTime);
                    DrawGradientField("Albedo", fogColorGradientProp, controller.NormalizedTime);
#if USING_URP
                    if (DawnFogRendererFeatureEditorUtility.IsInstalled(out var rendererData))
                    {
                        EditorGUILayout.HelpBox(
                            $"URP output is active on '{rendererData.name}'.",
                            MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "These fog tracks are sampled, but the default URP Renderer Data is missing Dawn TOD Fog.",
                            MessageType.Warning);
                        if (GUILayout.Button("Install Dawn TOD Fog Renderer Feature"))
                        {
                            DawnFogRendererFeatureEditorUtility.InstallOnDefaultRenderer(out _);
                        }
                    }
#endif
                    EditorGUI.indentLevel--;
                }
#endif
#if USING_HDRP
                //====Exposure====
                showExposureSettings = EditorGUILayout.Foldout(showExposureSettings, "Exposure Settings", true);
                if (showExposureSettings)
                {
                    EditorGUI.indentLevel++;
                    DrawCurveField("Exposure Compensation", exposureCompensationCurveProp, controller.NormalizedTime);
                    EditorGUI.indentLevel--;
                }
#endif
                //====Rain====
                showRainySettings = EditorGUILayout.Foldout(showRainySettings, "Rainy Settings", true);
                if (showRainySettings)
                {
                    EditorGUI.indentLevel++;
                    SerializedProperty rainyEnable = presetSerializedObject.FindProperty("rainyEnable");
                    EditorGUILayout.PropertyField(rainyEnable, new GUIContent("Enable Rainy"));
                    DrawCurveField("Precipitation Amount", precipitationAmountCurveProp, controller.NormalizedTime);
                    DrawCurveField("Rainy Speed", rainySpeedCurveProp, controller.NormalizedTime);
                    DrawCurveField("Rain Density", rainDensityCurveProp, controller.NormalizedTime);
                    DrawCurveField("Rain WindZRotation", rainWindZRotationCurveProp, controller.NormalizedTime);
                    EditorGUI.indentLevel--;
                }

                ApplyPresetModifiedPropertiesAndRefresh(
                    presetSerializedObject,
                    preset,
                    controller);
            }
            else
            {
                EditorGUILayout.HelpBox("请设置 Active Preset 以查看预设信息", MessageType.Info);
            }

            ApplyControllerModifiedPropertiesAndRefresh(
                serializedObject,
                controller);
        }

        private void DrawDebugWeatherPreview(
            DawnWeatherController controller)
        {
            DawnTODSystem todSystem = DawnTODSystem.Instance;
            bool isPreviewingThisController =
                todSystem != null &&
                todSystem.IsDebugWeatherPreview(controller);

            if (isPreviewingThisController)
            {
                debugPreviewSystem = todSystem;
                debugPreviewController = controller;
                EditorGUILayout.HelpBox(
                    "Solo weather preview is active. The normal multi-weather blend " +
                    "will be restored when this Inspector closes.",
                    MessageType.Warning);
                if (GUILayout.Button("Restore Multi-Weather Blending"))
                {
                    todSystem.ClearDebugWeatherPreview(controller);
                    debugPreviewSystem = null;
                    debugPreviewController = null;
                    SceneView.RepaintAll();
                }

                return;
            }

            bool hasSystem = todSystem != null;
            bool hasPreset = controller != null && controller.ActivePreset != null;
            bool isControllerActive =
                controller != null && controller.isActiveAndEnabled;
            using (new EditorGUI.DisabledScope(
                       !hasSystem || !hasPreset || !isControllerActive))
            {
                if (GUILayout.Button("Solo Preview This Weather"))
                {
                    if (todSystem.SetDebugWeatherPreview(controller))
                    {
                        debugPreviewSystem = todSystem;
                        debugPreviewController = controller;
                        SceneView.RepaintAll();
                    }
                }
            }

            if (!hasSystem)
            {
                EditorGUILayout.HelpBox(
                    "A Dawn TOD System is required for solo weather preview.",
                    MessageType.Info);
            }
            else if (!hasPreset)
            {
                EditorGUILayout.HelpBox(
                    "Assign an Active Preset to enable solo weather preview.",
                    MessageType.Info);
            }
            else if (!isControllerActive)
            {
                EditorGUILayout.HelpBox(
                    "Enable this Weather Controller to use solo weather preview.",
                    MessageType.Info);
            }
        }

        private void RestoreDebugWeatherPreview()
        {
            if (debugPreviewSystem != null &&
                debugPreviewController != null)
            {
                debugPreviewSystem.ClearDebugWeatherPreview(
                    debugPreviewController);
            }

            debugPreviewSystem = null;
            debugPreviewController = null;
        }

        internal static bool ApplyControllerModifiedPropertiesAndRefresh(
            SerializedObject serializedObject,
            DawnWeatherController controller)
        {
            if (!serializedObject.ApplyModifiedProperties())
            {
                return false;
            }

            EditorUtility.SetDirty(controller);
            controller.Refresh();
            SceneView.RepaintAll();
            return true;
        }

        internal static bool ApplyPresetModifiedPropertiesAndRefresh(
            SerializedObject serializedObject,
            DawnWeatherPreset preset,
            DawnWeatherController controller)
        {
            if (!serializedObject.ApplyModifiedProperties())
            {
                return false;
            }

            EditorUtility.SetDirty(preset);
            controller.Refresh();
            SceneView.RepaintAll();
            return true;
        }

        private void DrawTimeSlider(DawnWeatherController controller)
        {
            float currentTime = DawnTODSystem.Instance != null
                ? DawnTODSystem.Instance.TimeOfDay
                : timeOfDayProp.floatValue;
            Rect sliderRect = EditorGUILayout.GetControlRect(GUILayout.Height(SLIDER_HEIGHT));
            DrawDayNightGradient(sliderRect);

            float handleWidth = 4f;
            float normalizedTime = Mathf.Clamp01(currentTime / TIME_RANGE);
            float handleX = Mathf.Lerp(sliderRect.x, sliderRect.xMax - handleWidth, normalizedTime);
            Rect handleRect = new Rect(handleX, sliderRect.y, handleWidth, sliderRect.height);

            Event e = Event.current;
            int controlID = GUIUtility.GetControlID(s_SliderControlID, FocusType.Keyboard, sliderRect);

            switch (e.GetTypeForControl(controlID))
            {
                case EventType.MouseDown:
                    if (sliderRect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = controlID;
                        SetTimeFromMousePosition(controller, sliderRect, e.mousePosition.x, handleWidth);
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlID)
                    {
                        SetTimeFromMousePosition(controller, sliderRect, e.mousePosition.x, handleWidth);
                        e.Use();
                        Repaint();
                        SceneView.RepaintAll();
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

            EditorGUI.DrawRect(handleRect, Color.white);
            DrawTimeMarkers(sliderRect);

            int hours = Mathf.FloorToInt(currentTime);
            int minutes = Mathf.FloorToInt((currentTime - hours) * 60f);
            string timeText = $"{hours:D2}:{minutes:D2}";

            GUIStyle centerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.cyan }
            };

            Rect textRect = new Rect(sliderRect.center.x - 30, sliderRect.yMax + 2, 60, 16);
            GUI.Label(textRect, timeText, centerStyle);
            EditorGUILayout.Space(18);
        }

        private void DrawDayNightGradient(Rect rect)
        {
            Color grayBackground = new Color(0.533f, 0.533f, 0.533f, 1f);
            EditorGUI.DrawRect(rect, grayBackground);
        }

        private void DrawTimeMarkers(Rect sliderRect)
        {
            GUIStyle markerStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };

            for (int hour = 0; hour <= 24; hour += 6)
            {
                float x = sliderRect.x + sliderRect.width * (hour / 24f);
                Rect markerRect = new Rect(x - 10, sliderRect.y - 14, 20, 12);
                GUI.Label(markerRect, hour.ToString(), markerStyle);
            }
        }

        private void SetTimeFromMousePosition(DawnWeatherController controller, Rect sliderRect, float mouseX, float handleWidth)
        {
            float minX = sliderRect.x;
            float maxX = sliderRect.xMax - handleWidth;
            float clampedX = Mathf.Clamp(mouseX, minX, maxX);
            float normalized = Mathf.InverseLerp(minX, maxX, clampedX);
            float newTime = normalized * TIME_RANGE;

            timeOfDayProp.floatValue = newTime;
            Undo.RecordObject(controller, "Change TOD Time");
            controller.SetTime(newTime);
            EditorUtility.SetDirty(controller);
            serializedObject.Update();
        }

        private void DrawCurveField(string label, SerializedProperty curveProp, float normalizedTime)
        {
            if (curveProp == null) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(FIELD_LABEL_WIDTH));

            Rect curveRect = EditorGUILayout.GetControlRect(GUILayout.Height(CURVE_HEIGHT));
            EditorGUI.PropertyField(curveRect, curveProp, GUIContent.none);

            float x = curveRect.x + curveRect.width * normalizedTime;
            EditorGUI.DrawRect(new Rect(x, curveRect.y, 1, curveRect.height), Color.red);

            AnimationCurve curve = curveProp.animationCurveValue;
            float value = curve.Evaluate(normalizedTime);
            EditorGUILayout.LabelField(value.ToString("F1"), GUILayout.Width(60));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawGradientField(string label, SerializedProperty gradientProp, float normalizedTime)
        {
            if (gradientProp == null) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(FIELD_LABEL_WIDTH));

            Rect gradientRect = EditorGUILayout.GetControlRect(GUILayout.Height(20));
            EditorGUI.PropertyField(gradientRect, gradientProp, GUIContent.none);

            float x = gradientRect.x + gradientRect.width * normalizedTime;
            EditorGUI.DrawRect(new Rect(x, gradientRect.y, 1, gradientRect.height), Color.red);

            Gradient gradient = gradientProp.gradientValue;
            Color currentColor = gradient.Evaluate(normalizedTime);
            EditorGUI.DrawRect(EditorGUILayout.GetControlRect(GUILayout.Width(60), GUILayout.Height(20)), currentColor);

            EditorGUILayout.EndHorizontal();
        }

    }
}
