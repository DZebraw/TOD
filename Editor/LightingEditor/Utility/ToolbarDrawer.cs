using System;
using UnityEngine;
using UnityEditor;
using DawnTOD;

namespace DawnTODEditor
{
    internal class LightingEditorToolbarDrawer
    {
        public static GUIStyle CreateToolbarStyle()
        {
            return new GUIStyle(EditorStyles.toolbar)
            {
                fixedHeight = LightingEditorConstants.TOOLBAR_HEIGHT
            };
        }

        public static GUIStyle CreateTimeDisplayStyle()
        {
            return new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };
        }

        // 工具栏绘制
        public static void DrawToolbar(
            float toolbarHeight,
            GUIStyle toolbarStyle,
            GUIStyle timeDisplayStyle,
            LightingEditorState state,
            DawnWeatherController selectedController,
            Action<DawnWeatherController> onControllerSelected,
            Action onRefreshClicked,
            Action<ViewLevel> onViewLevelChanged,
            Action onTimeFormatToggled)
        {
            EditorGUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(toolbarHeight));
            {
                // Controller 选择（仅 Level 3 显示）
                if (state.CurrentViewLevel == ViewLevel.Level3_CurveEditor)
                {
                    GUILayout.Label("Controller:", GUILayout.Width(70));
                    DrawControllerSelector(selectedController, onControllerSelected);
                    GUILayout.Space(8);
                }

                // 刷新按钮
                if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), GUILayout.Width(28), GUILayout.Height(20)))
                {
                    onRefreshClicked?.Invoke();
                }

                GUILayout.FlexibleSpace();

                // 时间显示
                GUILayout.Label($"Time: {state.GetFormattedTime()}", timeDisplayStyle, GUILayout.Width(120));

                // 时间格式切换
                if (GUILayout.Button(state.TimeDisplayMode == TimeDisplayMode.Format24H ? "24H" : "12H", GUILayout.Width(40)))
                {
                    onTimeFormatToggled?.Invoke();
                }

                GUILayout.FlexibleSpace();

                // 视图级别按钮
                DrawViewLevelButtons(state, onViewLevelChanged);
            }
            EditorGUILayout.EndHorizontal();
        }

        // 抽离Controller选择器子方法
        private static void DrawControllerSelector(DawnWeatherController selectedController, Action<DawnWeatherController> onSelected)
        {
            string currentName = selectedController != null ? selectedController.name : "None";
            if (GUILayout.Button(currentName, EditorStyles.popup, GUILayout.Width(150)))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("None"), selectedController == null, () => onSelected?.Invoke(null));
                menu.AddSeparator("");

                var controllers = UnityEngine.Object.FindObjectsOfType<DawnWeatherController>();
                foreach (var controller in controllers)
                {
                    var c = controller;
                    menu.AddItem(new GUIContent(c.name), c == selectedController, () => onSelected?.Invoke(c));
                }
                menu.ShowAsContext();
            }
        }

        // 抽离视图级别按钮子方法
        private static void DrawViewLevelButtons(LightingEditorState state, Action<ViewLevel> onLevelChanged)
        {
            GUILayout.Label("View:", GUILayout.Width(35));

            Color defaultColor = GUI.backgroundColor;
            Color selectedColor = new Color(0.3f, 0.6f, 1f);

            // Level 1
            GUI.backgroundColor = state.CurrentViewLevel == ViewLevel.Level1_Playback ? selectedColor : defaultColor;
            if (GUILayout.Button("1", GUILayout.Width(24)))
            {
                onLevelChanged?.Invoke(ViewLevel.Level1_Playback);
            }

            // Level 2
            GUI.backgroundColor = state.CurrentViewLevel == ViewLevel.Level2_LightControl ? selectedColor : defaultColor;
            if (GUILayout.Button("2", GUILayout.Width(24)))
            {
                onLevelChanged?.Invoke(ViewLevel.Level2_LightControl);
            }

            // Level 3
            GUI.backgroundColor = state.CurrentViewLevel == ViewLevel.Level3_CurveEditor ? selectedColor : defaultColor;
            if (GUILayout.Button("3", GUILayout.Width(24)))
            {
                onLevelChanged?.Invoke(ViewLevel.Level3_CurveEditor);
            }

            GUI.backgroundColor = defaultColor;
        }

    }
}
