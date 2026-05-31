using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using DawnTOD;

namespace DawnTODEditor
{
    /// <summary>
    /// 时间轴刻度尺绘制器
    /// 封装时间刻度、时间标签、当前时间线的绘制，以及点击设置时间的交互
    /// </summary>
    public class TimelineRulerDrawer : IEditorDrawer, IDisposable
    {
        private LightingEditorState _state;
        private TrackManager _trackManager;
        // 用于将点击后的时间值传递给主窗口
        private Action<float> _onSetCurrentTime;

        /// <summary>
        /// 注入时间设置回调（主窗口传递 SetCurrentTime 方法）
        /// </summary>
        /// <param name="onSetCurrentTime">时间设置回调</param>
        public void SetOnSetCurrentTimeCallback(Action<float> onSetCurrentTime)
        {
            _onSetCurrentTime = onSetCurrentTime;
        }

        /// <summary>
        /// 初始化绘制器（实现 IEditorDrawer 接口，注入状态和 TrackManager）
        /// </summary>
        /// <param name="state">编辑器全局状态</param>
        /// <param name="trackManager">轨道管理器（唯一数据源）</param>
        public void Initialize(LightingEditorState state, TrackManager trackManager)
        {
            _state = state;
            _trackManager = trackManager;

            if (_trackManager != null)
            {
                _trackManager.OnTracksRefreshed += OnTracksRefreshed;
            }
        }

        /// <summary>
        /// 执行绘制（接口实现，直接调用核心绘制方法）
        /// </summary>
        /// <param name="drawRect">绘制区域</param>
        public void Draw(Rect drawRect)
        {
            DrawTimelineRuler(drawRect);
        }

        /// <summary>
        /// 处理绘制区域事件（接口实现，封装点击交互逻辑）
        /// </summary>
        /// <param name="drawRect">绘制区域</param>
        /// <param name="evt">当前事件</param>
        /// <returns>是否处理了该事件</returns>
        public bool HandleEvent(Rect drawRect, Event evt)
        {
            return HandleTimelineRulerEvent(drawRect, evt);
        }

        /// <summary>
        /// 轨道数据变更回调（预留扩展，当前无实际逻辑）
        /// </summary>
        private void OnTracksRefreshed()
        {

        }

        /// <summary>
        /// 释放资源（解除事件监听，防止内存泄漏）
        /// </summary>
        public void Dispose()
        {
            if (_trackManager != null)
            {
                _trackManager.OnTracksRefreshed -= OnTracksRefreshed;
            }
        }

        /// <summary>
        /// 核心绘制方法：绘制时间轴刻度尺
        /// </summary>
        /// <param name="rect">绘制区域</param>
        private void DrawTimelineRuler(Rect rect)
        {
            if (_state == null || rect.width <= 0 || rect.height <= 0) return;

            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));

            int numTicks = 24;
            float tickSpacing = rect.width / numTicks;

            for (int i = 0; i <= numTicks; i++)
            {
                float x = rect.x + i * tickSpacing;
                // 每6小时绘制长刻度，其余绘制短刻度
                float tickHeight = (i % 6 == 0) ? 12 : 6;

                Rect tickRect = new Rect(x, rect.yMax - tickHeight, 1, tickHeight);
                EditorGUI.DrawRect(tickRect, new Color(0.5f, 0.5f, 0.5f));

                if (i % 6 == 0)
                {
                    Rect labelRect = new Rect(x - 15, rect.y, 30, 12);
                    GUI.Label(labelRect, $"{i}:00", EditorStyles.miniLabel);
                }
            }

            float timeX = rect.x + _state.CurrentTime * rect.width;
            timeX = Mathf.Clamp(timeX, rect.x, rect.xMax); 
            Rect timeLineRect = new Rect(timeX - 1, rect.y, 2, rect.height);
            EditorGUI.DrawRect(timeLineRect, new Color(1f, 0.3f, 0.3f));
        }

        /// <summary>
        /// 核心交互方法：处理时间轴的点击事件
        /// </summary>
        /// <param name="rect">绘制区域</param>
        /// <param name="evt">当前事件</param>
        /// <returns>是否处理了该事件</returns>
        private bool HandleTimelineRulerEvent(Rect rect, Event evt)
        {
            if (_state == null || _onSetCurrentTime == null || evt == null) return false;

            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                float newTime = rect.width > 0 ? (evt.mousePosition.x - rect.x) / rect.width : 0f;
                _onSetCurrentTime(Mathf.Clamp01(newTime));
                evt.Use();
                return true;
            }
            return false;
        }
    }
}