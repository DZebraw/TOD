using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using DawnTOD;

namespace DawnTODEditor
{
    /// <summary>
    /// 网格背景绘制器
    /// 封装网格的绘制逻辑，与主窗口解耦
    /// </summary>
    public class GridDrawer : IEditorDrawer
    {
        private const float TRACK_HEIGHT = 24f;

        private LightingEditorState _state;
        private TrackManager _trackManager;

        /// <summary>
        /// 初始化绘制器
        /// </summary>
        public void Initialize(LightingEditorState state, TrackManager trackManager)
        {
            _state = state;
            _trackManager = trackManager;
        }

        /// <summary>
        /// IEditorDrawer 接口实现（默认绘制，留空）
        /// </summary>
        public void Draw(Rect drawRect)
        {
            // 调用核心绘制方法
            DrawGrid(drawRect);
        }

        /// <summary>
        /// IEditorDrawer 接口实现（无交互，返回false）
        /// </summary>
        public bool HandleEvent(Rect drawRect, Event evt)
        {
            return false;
        }

        /// <summary>
        /// 对外暴露的核心方法：绘制网格背景
        /// 与主窗口原有 DrawGrid 逻辑完全一致
        /// </summary>
        public void DrawGrid(Rect rect)
        {
            // 垂直线（时间刻度）
            int numVertical = 24;
            for (int i = 0; i <= numVertical; i++)
            {
                float x = rect.x + (i / (float)numVertical) * rect.width;
                Color lineColor = (i % 6 == 0) ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.2f, 0.2f, 0.2f);
                EditorGUI.DrawRect(new Rect(x, rect.y, 1, rect.height), lineColor);
            }

            // 水平线：每 24px 一条（匹配轨道高度）
            int numHorizontal = Mathf.CeilToInt(rect.height / TRACK_HEIGHT);
            for (int i = 0; i <= numHorizontal; i++)
            {
                float y = rect.y + i * TRACK_HEIGHT;
                if (y <= rect.yMax)
                {
                    EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1), new Color(0.1f, 0.1f, 0.1f));
                }
            }
        }
    }
}