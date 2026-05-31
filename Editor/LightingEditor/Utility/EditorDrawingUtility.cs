using UnityEngine;
using UnityEditor;

namespace DawnTODEditor
{
    /// <summary>
    /// 编辑器通用绘制工具类
    /// 封装无状态的通用绘制辅助方法，便于复用
    /// </summary>
    public static class EditorDrawingUtility
    {
        /// <summary>
        /// 绘制水平分隔线（与主窗口原有逻辑一致）
        /// </summary>
        public static void DrawHorizontalLine()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
        }

        /// <summary>
        /// 获取轨道对应的颜色（与主窗口原有逻辑一致）
        /// </summary>
        /// <param name="index">轨道索引</param>
        /// <returns>轨道对应的颜色</returns>
        public static Color GetTrackColor(int index)
        {
            Color[] colors = {
                new Color(1f, 0.4f, 0.4f),
                new Color(0.4f, 1f, 0.4f),
                new Color(0.4f, 0.4f, 1f),
                new Color(1f, 1f, 0.4f),
                new Color(1f, 0.4f, 1f),
                new Color(0.4f, 1f, 1f)
            };
            return colors[index % colors.Length];
        }
    }
}