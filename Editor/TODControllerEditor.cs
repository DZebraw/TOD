using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TODController))]
public class TODControllerEditor : Editor
{
    private const float TIME_RANGE = 24f;
    private const int SLIDER_HEIGHT = 30;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 绘制默认的 Inspector，排除 timeOfDay 和 sunIntensity
        DrawDefaultInspectorExcept("timeOfDay", "sunIntensity");

        // 绘制 timeOfDay
        DrawTimeOfDayUI();

        // 绘制 sunIntensity
        DrawSunIntensityUI();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawDefaultInspectorExcept(params string[] excludeProps)
    {
        var iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (excludeProps.Contains(iterator.name)) continue;
            EditorGUILayout.PropertyField(iterator, true);
        }
    }

    private void DrawTimeOfDayUI()
    {
        SerializedProperty timeOfDayProp = serializedObject.FindProperty("timeOfDay");

        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField("Time of Day", EditorStyles.boldLabel);

        float currentTime = timeOfDayProp.floatValue;

        // 获取滑块区域
        Rect sliderRect = EditorGUILayout.GetControlRect(GUILayout.Height(SLIDER_HEIGHT));
        EditorGUI.DrawRect(sliderRect, new Color(0.15f, 0.15f, 0.15f));

        // 计算手柄位置
        float handleSize = 12f;
        float normalizedTime = Mathf.Clamp01(currentTime / TIME_RANGE);
        float handleX = Mathf.Lerp(sliderRect.x, sliderRect.xMax - handleSize, normalizedTime);
        Rect handleRect = new Rect(handleX, sliderRect.y + (SLIDER_HEIGHT - handleSize) * 0.5f, handleSize, handleSize);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && sliderRect.Contains(e.mousePosition))
        {
            SetTimeFromMousePosition(sliderRect, e.mousePosition.x, timeOfDayProp);
            e.Use(); // 标记事件已处理
        }
        else if (e.type == EventType.MouseDrag && sliderRect.Contains(e.mousePosition))
        {
            SetTimeFromMousePosition(sliderRect, e.mousePosition.x, timeOfDayProp);
            e.Use();
        }

        // 绘制手柄（纯视觉，无交互组件）
        Color oldColor = GUI.color;
        GUI.color = IsNightTime(currentTime) ? new Color(0.2f, 0.2f, 0.8f) : new Color(0.9f, 0.8f, 0.2f);
        GUI.DrawTexture(handleRect, EditorGUIUtility.whiteTexture, ScaleMode.ScaleToFit);
        GUI.color = oldColor;

        // 时间文本
        string timeText = $"{Mathf.FloorToInt(currentTime):D2}:{Mathf.FloorToInt((currentTime % 1) * 60):D2}";
        var textRect = new Rect(sliderRect.center.x - 30, sliderRect.yMax + 4, 60, 16);
        GUI.Label(textRect, timeText, EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.Space(10f);
    }

    private void DrawSunIntensityUI()
    {
        SerializedProperty sunIntensityProp = serializedObject.FindProperty("sunIntensity");

        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField("Sun Intensity", EditorStyles.boldLabel);

        // 获取曲线区域
        Rect curveRect = EditorGUILayout.GetControlRect(GUILayout.Height(SLIDER_HEIGHT));
        EditorGUI.DrawRect(curveRect, new Color(0.15f, 0.15f, 0.15f));

        // 绘制曲线
        AnimationCurve sunIntensityCurve = sunIntensityProp.animationCurveValue;
        Handles.color = Color.green;
        EditorGUI.CurveField(curveRect, sunIntensityCurve);

        EditorGUILayout.Space(10f);
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