using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using DawnTOD;

namespace DawnTODEditor
{
    /// <summary>
    /// 动画曲线绘制器
    /// 抽离 AnimationCurve 的所有绘制逻辑，与 GradientDrawer 结构保持一致
    /// </summary>
    public class AnimationCurveDrawer : IEditorDrawer
    {
        private LightingEditorState _state;
        private TrackManager _trackManager;

        /// <summary>
        /// 初始化绘制器
        /// </summary>
        /// <param name="state">编辑器全局状态</param>
        /// <param name="trackManager">轨道管理器（唯一数据源）</param>
        public void Initialize(LightingEditorState state, TrackManager trackManager)
        {
            _state = state;
            _trackManager = trackManager;
        }

        /// <summary>
        /// IEditorDrawer 接口实现（默认绘制，留空）
        /// </summary>
        /// <param name="drawRect">绘制区域</param>
        public void Draw(Rect drawRect)
        {
            // 留空，对外暴露带具体参数的核心绘制方法
        }

        /// <summary>
        /// IEditorDrawer 接口实现（曲线绘制暂无需额外独立事件处理，返回 false）
        /// </summary>
        /// <param name="drawRect">绘制区域</param>
        /// <param name="evt">当前事件</param>
        /// <returns>是否处理了该事件</returns>
        public bool HandleEvent(Rect drawRect, Event evt)
        {
            return false;
        }

        /// <summary>
        /// 对外暴露的核心方法：绘制动画曲线
        /// 与原有 DrawCurve 逻辑完全一致，参数不变
        /// </summary>
        /// <param name="rect">绘制区域</param>
        /// <param name="curve">要绘制的动画曲线</param>
        /// <param name="color">曲线绘制颜色</param>
        /// <param name="trackIndex">所属轨道索引</param>
        public void DrawAnimationCurve(Rect rect, AnimationCurve curve, Color color, int trackIndex)
        {
            if (curve == null || curve.length < 2) return;

            float viewMin = float.MaxValue;
            float viewMax = float.MinValue;

            // ===== 采样整条曲线（而不是只看关键帧）=====
            const int sampleCount = 256;
            for (int i = 0; i <= sampleCount; i++)
            {
                float t = i / (float)sampleCount;
                float v = curve.Evaluate(t);
                viewMin = Mathf.Min(viewMin, v);
                viewMax = Mathf.Max(viewMax, v);
            }

            // 防止极端情况（曲线值无变化）
            if (Mathf.Abs(viewMax - viewMin) < 0.0001f)
            {
                viewMax = viewMin + 1f;
            }

            // === 2. 绘制曲线 ===
            Handles.BeginGUI();
            Handles.color = color;

            const int segments = 100;
            Vector3 prevPoint = Vector3.zero;

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float value = curve.Evaluate(t);

                float normalizedValue = Mathf.InverseLerp(viewMin, viewMax, value);

                // 2. 计算屏幕 X 坐标 (0 -> 1 映射到 rect.x -> rect.xMax)
                float x = rect.x + t * rect.width;

                // 3. 计算屏幕 Y 坐标（从上到下对应值从大到小，与原有逻辑一致）
                float y = Mathf.Lerp(rect.yMax, rect.y, normalizedValue);

                Vector3 point = new Vector3(x, y, 0);

                if (i > 0)
                {
                    Handles.DrawLine(prevPoint, point);
                }
                prevPoint = point;
            }

            // === 3. 绘制关键帧点 ===
            foreach (var key in curve.keys)
            {
                Vector2 screenPos = GetKeyframeScreenPosition(key, rect, viewMin, viewMax);
                Handles.DrawSolidDisc(new Vector3(screenPos.x, screenPos.y, 0), Vector3.forward, 3f);
            }

            // === 4. 绘制切线手柄（如果该关键帧被选中）===
            if (_state.SelectedKeyframe.HasValue &&
                _state.CurrentCurveEditMode != CurveEditMode.None &&
                _state.SelectedKeyframe.Value.trackIndex == trackIndex)
            {
                int selectedKeyIndex = _state.SelectedKeyframe.Value.keyIndex;
                if (selectedKeyIndex >= 0 && selectedKeyIndex < curve.keys.Length)
                {
                    var key = curve.keys[selectedKeyIndex];
                    Vector2 keyPos = GetKeyframeScreenPosition(key, rect, viewMin, viewMax);

                    const float HANDLER_LENGTH_PX = 32f;

                    Handles.color = Color.white;

                    // 1. 计算右侧手柄 (Out Tangent)
                    Vector2 outHandleEnd = CalculateTangentHandlePosition(keyPos, key.outTangent, HANDLER_LENGTH_PX, rect, viewMin, viewMax, isOut: true);

                    // 2. 计算左侧手柄 (In Tangent)
                    Vector2 inHandleEnd = CalculateTangentHandlePosition(keyPos, key.inTangent, HANDLER_LENGTH_PX, rect, viewMin, viewMax, isOut: false);

                    Handles.DrawLine(keyPos, outHandleEnd);
                    Handles.DrawLine(keyPos, inHandleEnd);
                    Handles.DrawSolidDisc(outHandleEnd, Vector3.forward, 3f);
                    Handles.DrawSolidDisc(inHandleEnd, Vector3.forward, 3f);
                }
            }

            Handles.EndGUI();
        }

        /// <summary>
        /// 内部辅助方法：计算切线手柄在屏幕上的终点位置
        /// 与原有逻辑完全一致
        /// </summary>
        private Vector2 CalculateTangentHandlePosition(
            Vector2 keyPos,
            float tangentSlope,
            float screenLength,
            Rect viewRect,
            float viewMin,
            float viewMax,
            bool isOut)
        {
            float dataDX = 0.05f; // 初始数据空间步长 (时间轴)
            if (!isOut) dataDX = -dataDX; // In Tangent 方向相反

            for (int i = 0; i < 5; i++)
            {
                float dataDY = tangentSlope * dataDX;

                float screenDX = dataDX * viewRect.width;
                float screenDY = -(dataDY * viewRect.height) / (viewMax - viewMin);

                float currentLength = Mathf.Sqrt(screenDX * screenDX + screenDY * screenDY);

                if (Mathf.Approximately(currentLength, screenLength) || currentLength < 0.01f)
                {
                    return keyPos + new Vector2(screenDX, screenDY);
                }

                float scale = screenLength / currentLength;
                dataDX *= scale;
            }

            float finalDataDY = tangentSlope * dataDX;
            float finalScreenDX = dataDX * viewRect.width;
            float finalScreenDY = -(finalDataDY * viewRect.height) / (viewMax - viewMin);

            return keyPos + new Vector2(finalScreenDX, finalScreenDY);
        }

        /// <summary>
        /// 内部辅助方法：计算关键帧在屏幕上的坐标
        /// 与原有逻辑完全一致
        /// </summary>
        private Vector2 GetKeyframeScreenPosition(Keyframe key, Rect rect, float viewMin, float viewMax)
        {
            float range = viewMax - viewMin;
            if (range < 0.001f) range = 1f;

            // 归一化 Y 值
            float normalizedValue = (key.value - viewMin) / range;

            // 计算屏幕坐标（与原有逻辑一致）
            float x = rect.x + key.time * rect.width;
            float y = rect.yMax - normalizedValue * rect.height;

            return new Vector2(x, y);
        }
    }
}