using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TODController))]
public class TODControllerEditor : Editor
{
    private const float TIME_RANGE = 24f;
    private const int SLIDER_HEIGHT = 20;
    private static readonly int s_SliderControlID = "TODTimeSlider".GetHashCode();

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        DrawDefaultInspector();

        // 绘制 timeOfDay
        DrawTimeOfDayUI();
        
        //绘制曲线
        TODController controller = (TODController)target;
        if (controller != null && controller.todState != null)
        {
            DrawFloatCurveUI(controller.todState.sunIntensity, "Sun Intensity");
            DrawGradientUI(controller.todState.sunColor, "Sun Color");
            DrawFloatCurveUI(controller.todState.moonIntensity, "Moon Intensity");
            DrawGradientUI(controller.todState.moonColor, "Moon Color");
            DrawFloatCurveUI(controller.todState.starEmission, "Star Emission");
        }
        else
        {
            EditorGUILayout.HelpBox("Please assign a TOD State Asset to view and edit curves.", MessageType.Warning);
        }
        
        //TODO：天气的Toggle开关选项

        serializedObject.ApplyModifiedProperties();
    }
    
    private void DrawTimeOfDayUI()
    {
        SerializedProperty timeOfDayProp = serializedObject.FindProperty("timeOfDay");
        float currentTime = timeOfDayProp.floatValue;
        
        EditorGUILayout.LabelField("Time of Day", EditorStyles.boldLabel);

        // 获取滑块区域
        Rect sliderRect = EditorGUILayout.GetControlRect(GUILayout.Height(SLIDER_HEIGHT));
        EditorGUI.DrawRect(sliderRect, new Color(0.15f, 0.15f, 0.15f));

        // 计算手柄位置
        float handleSize = 12f;
        float normalizedTime = Mathf.Clamp01(currentTime / TIME_RANGE);
        float handleX = Mathf.Lerp(sliderRect.x, sliderRect.xMax - handleSize, normalizedTime);
        Rect handleRect = new Rect(handleX, sliderRect.y + (SLIDER_HEIGHT - handleSize) * 0.5f, handleSize, handleSize);

        Event e = Event.current;
        int controlID = GUIUtility.GetControlID(s_SliderControlID, FocusType.Passive, sliderRect);

        switch (e.GetTypeForControl(controlID))
        {
            case EventType.MouseDown:
                if (sliderRect.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = controlID;
                    SetTimeFromMousePosition(sliderRect, e.mousePosition.x, timeOfDayProp);
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlID)
                {
                    SetTimeFromMousePosition(sliderRect, e.mousePosition.x, timeOfDayProp);
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
        Color oldColor = GUI.color;
        GUI.color = IsNightTime(currentTime) ? new Color(0.2f, 0.2f, 0.8f) : new Color(0.9f, 0.8f, 0.2f);
        GUI.DrawTexture(handleRect, EditorGUIUtility.whiteTexture, ScaleMode.ScaleToFit);
        GUI.color = oldColor;

        // 时间文本
        string timeText = $"{Mathf.FloorToInt(currentTime):D2}:{Mathf.FloorToInt((currentTime % 1) * 60):D2}";
        var textRect = new Rect(sliderRect.center.x - 30, sliderRect.yMax + 4, 60, 16);
        GUI.Label(textRect, timeText, EditorStyles.centeredGreyMiniLabel);
    }

    private void DrawFloatCurveUI(AnimationCurve animationCurve,string label)
    {
        EditorGUILayout.LabelField(label);

        // 获取曲线区域
        Rect curveRect = EditorGUILayout.GetControlRect(GUILayout.Height(SLIDER_HEIGHT));
        EditorGUI.DrawRect(curveRect, new Color(0.15f, 0.15f, 0.15f));

        // 绘制曲线
        Handles.color = Color.green;
        EditorGUI.CurveField(curveRect, animationCurve);
    }

    private void DrawGradientUI(Gradient gradient, string label)
    {
        EditorGUILayout.LabelField(label);
        
        Rect gradientRect = EditorGUILayout.GetControlRect(GUILayout.Height(SLIDER_HEIGHT));
        EditorGUI.DrawRect(gradientRect, new Color(0.15f, 0.15f, 0.15f));
        
        EditorGUI.GradientField(gradientRect, gradient);
    }

    void SetTimeFromMousePosition(Rect sliderRect, float mouseX, SerializedProperty timeProp)
    {
        float handleSize = 12f;
        float minX = sliderRect.x;
        float maxX = sliderRect.xMax - handleSize;
        float clampedX = Mathf.Clamp(mouseX, minX, maxX);
        float normalized = Mathf.InverseLerp(minX, maxX, clampedX);
        timeProp.floatValue = normalized * TIME_RANGE;
    }

    bool IsNightTime(float time)
    {
        return time < 6f || time > 18f;
    }
}