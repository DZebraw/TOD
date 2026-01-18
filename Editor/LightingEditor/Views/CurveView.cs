using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace NeuroTODEditor
{
    /// <summary>
    /// 曲线编辑视图
    /// 支持曲线绘制、关键帧编辑、多曲线叠加显示
    /// </summary>
    public class CurveView
    {
        // ========== 常量 ==========
        private const float KEYFRAME_SIZE = 8f;
        private const float KEYFRAME_HIT_SIZE = 12f;
        private const float TANGENT_HANDLE_SIZE = 6f;
        private const float TANGENT_LINE_LENGTH = 50f;

        // ========== 状态 ==========
        private List<CurveBinding> curves;
        private HashSet<KeyframeHandle> selectedKeyframes;
        private float viewRangeMin = 0f;
        private float viewRangeMax = 1f;
        private float valueRangeMin = 0f;
        private float valueRangeMax = 1f;
        private bool autoFitValueRange = true;

        // ========== 拖拽状态 ==========
        private bool isDraggingKeyframe;
        private bool isDraggingTangent;
        private bool isBoxSelecting;
        private Vector2 boxSelectStart;
        private Vector2 boxSelectEnd;
        private KeyframeHandle draggedKeyframe;
        private int draggedTangent; // 0: in, 1: out

        // ========== 事件 ==========
        public event Action<HashSet<KeyframeHandle>> OnKeyframeSelectionChanged;
        public event Action<KeyframeHandle, float, float> OnKeyframeMoved;
        public event Action<int, float, float> OnKeyframeAdded;
        public event Action<HashSet<KeyframeHandle>> OnKeyframesDeleted;

        // ========== 颜色 ==========
        private static readonly Color[] CurveColors = {
            new Color(1f, 0.4f, 0.4f),
            new Color(0.4f, 1f, 0.4f),
            new Color(0.4f, 0.4f, 1f),
            new Color(1f, 1f, 0.4f),
            new Color(1f, 0.4f, 1f),
            new Color(0.4f, 1f, 1f),
            new Color(1f, 0.7f, 0.4f),
            new Color(0.7f, 0.4f, 1f)
        };

        public CurveView()
        {
            curves = new List<CurveBinding>();
            selectedKeyframes = new HashSet<KeyframeHandle>();
        }

        /// <summary>
        /// 曲线绑定信息
        /// </summary>
        public class CurveBinding
        {
            public int TrackIndex;
            public string DisplayName;
            public AnimationCurve Curve;
            public Color Color;
            public bool IsVisible;

            public CurveBinding(int trackIndex, string name, AnimationCurve curve, Color color)
            {
                TrackIndex = trackIndex;
                DisplayName = name;
                Curve = curve;
                Color = color;
                IsVisible = true;
            }
        }

        /// <summary>
        /// 设置视图范围
        /// </summary>
        public void SetViewRange(float min, float max)
        {
            viewRangeMin = Mathf.Clamp01(min);
            viewRangeMax = Mathf.Clamp01(max);
        }

        /// <summary>
        /// 设置曲线列表
        /// </summary>
        public void SetCurves(List<CurveBinding> curveList)
        {
            curves = curveList ?? new List<CurveBinding>();
            if (autoFitValueRange)
            {
                FitValueRange();
            }
        }

        /// <summary>
        /// 添加曲线
        /// </summary>
        public void AddCurve(int trackIndex, string name, AnimationCurve curve)
        {
            Color color = CurveColors[curves.Count % CurveColors.Length];
            curves.Add(new CurveBinding(trackIndex, name, curve, color));
            if (autoFitValueRange)
            {
                FitValueRange();
            }
        }

        /// <summary>
        /// 清除所有曲线
        /// </summary>
        public void ClearCurves()
        {
            curves.Clear();
            selectedKeyframes.Clear();
        }

        /// <summary>
        /// 获取选中的关键帧
        /// </summary>
        public HashSet<KeyframeHandle> GetSelectedKeyframes() => selectedKeyframes;

        /// <summary>
        /// 自动适应值范围
        /// </summary>
        public void FitValueRange()
        {
            if (curves.Count == 0)
            {
                valueRangeMin = 0f;
                valueRangeMax = 1f;
                return;
            }

            float min = float.MaxValue;
            float max = float.MinValue;

            foreach (var binding in curves)
            {
                if (binding.Curve == null || !binding.IsVisible) continue;

                foreach (var key in binding.Curve.keys)
                {
                    min = Mathf.Min(min, key.value);
                    max = Mathf.Max(max, key.value);
                }
            }

            if (min == float.MaxValue)
            {
                min = 0f;
                max = 1f;
            }

            float range = max - min;
            if (range < 0.001f) range = 1f;

            // 添加边距
            valueRangeMin = min - range * 0.1f;
            valueRangeMax = max + range * 0.1f;
        }

        /// <summary>
        /// 绘制曲线视图
        /// </summary>
        public void Draw(Rect rect, float currentTime)
        {
            // 背景
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));

            // 绘制网格
            DrawGrid(rect);

            // 绘制曲线
            foreach (var binding in curves)
            {
                if (binding.IsVisible && binding.Curve != null)
                {
                    DrawCurve(rect, binding);
                }
            }

            // 绘制关键帧
            foreach (var binding in curves)
            {
                if (binding.IsVisible && binding.Curve != null)
                {
                    DrawKeyframes(rect, binding);
                }
            }

            // 绘制框选
            if (isBoxSelecting)
            {
                DrawBoxSelection(rect);
            }

            // 绘制当前时间指示线
            DrawTimeIndicator(rect, currentTime);

            // 绘制值刻度
            DrawValueScale(rect);

            // 处理输入
            HandleInput(rect);
        }

        private void DrawGrid(Rect rect)
        {
            float visibleTimeRange = viewRangeMax - viewRangeMin;
            float visibleValueRange = valueRangeMax - valueRangeMin;

            // 垂直线（时间刻度）
            float timeStep = CalculateGridStep(visibleTimeRange, rect.width, 50f);
            float startTime = Mathf.Floor(viewRangeMin / timeStep) * timeStep;
            for (float t = startTime; t <= viewRangeMax; t += timeStep)
            {
                if (t < viewRangeMin) continue;
                float x = TimeToX(rect, t);
                Color lineColor = Mathf.Approximately(t % (timeStep * 4), 0f)
                    ? new Color(0.3f, 0.3f, 0.3f)
                    : new Color(0.2f, 0.2f, 0.2f);
                EditorGUI.DrawRect(new Rect(x, rect.y, 1, rect.height), lineColor);
            }

            // 水平线（值刻度）
            float valueStep = CalculateGridStep(visibleValueRange, rect.height, 40f);
            float startValue = Mathf.Floor(valueRangeMin / valueStep) * valueStep;
            for (float v = startValue; v <= valueRangeMax; v += valueStep)
            {
                if (v < valueRangeMin) continue;
                float y = ValueToY(rect, v);
                Color lineColor = Mathf.Approximately(v % (valueStep * 4), 0f)
                    ? new Color(0.3f, 0.3f, 0.3f)
                    : new Color(0.2f, 0.2f, 0.2f);
                EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1), lineColor);
            }
        }

        private float CalculateGridStep(float range, float pixels, float minPixelStep)
        {
            float[] steps = { 0.001f, 0.005f, 0.01f, 0.05f, 0.1f, 0.5f, 1f, 5f, 10f, 50f, 100f, 500f, 1000f, 5000f, 10000f };
            foreach (float step in steps)
            {
                float pixelStep = (step / range) * pixels;
                if (pixelStep >= minPixelStep)
                {
                    return step;
                }
            }
            return steps[steps.Length - 1];
        }

        private void DrawCurve(Rect rect, CurveBinding binding)
        {
            if (binding.Curve.length < 2) return;

            Handles.BeginGUI();
            Handles.color = binding.Color;

            int segments = Mathf.Max(100, (int)(rect.width / 2));
            Vector3 prevPoint = Vector3.zero;
            bool prevValid = false;

            for (int i = 0; i <= segments; i++)
            {
                float t = viewRangeMin + (i / (float)segments) * (viewRangeMax - viewRangeMin);
                float value = binding.Curve.Evaluate(t);

                float x = TimeToX(rect, t);
                float y = ValueToY(rect, value);

                if (x >= rect.x && x <= rect.xMax && y >= rect.y && y <= rect.yMax)
                {
                    Vector3 point = new Vector3(x, y, 0);
                    if (prevValid)
                    {
                        Handles.DrawLine(prevPoint, point);
                    }
                    prevPoint = point;
                    prevValid = true;
                }
                else
                {
                    prevValid = false;
                }
            }

            Handles.EndGUI();
        }

        private void DrawKeyframes(Rect rect, CurveBinding binding)
        {
            var keys = binding.Curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (key.time < viewRangeMin || key.time > viewRangeMax) continue;

                float x = TimeToX(rect, key.time);
                float y = ValueToY(rect, key.value);

                KeyframeHandle handle = new KeyframeHandle(binding.TrackIndex, i);
                bool isSelected = selectedKeyframes.Contains(handle);

                // 绘制切线手柄（仅选中时）
                if (isSelected)
                {
                    DrawTangentHandles(rect, binding, i, x, y);
                }

                // 绘制关键帧菱形
                Color keyColor = isSelected ? new Color(1f, 0.8f, 0.2f) : binding.Color;
                DrawDiamond(new Vector2(x, y), KEYFRAME_SIZE, keyColor);
            }
        }

        private void DrawTangentHandles(Rect rect, CurveBinding binding, int keyIndex, float x, float y)
        {
            var keys = binding.Curve.keys;
            var key = keys[keyIndex];

            Handles.BeginGUI();
            Handles.color = new Color(0.8f, 0.8f, 0.8f);

            // 入切线
            if (keyIndex > 0)
            {
                float inAngle = Mathf.Atan(key.inTangent);
                Vector2 inDir = new Vector2(-Mathf.Cos(inAngle), Mathf.Sin(inAngle)) * TANGENT_LINE_LENGTH;
                Vector2 inHandle = new Vector2(x, y) + inDir;

                Handles.DrawLine(new Vector3(x, y, 0), new Vector3(inHandle.x, inHandle.y, 0));
                DrawCircle(inHandle, TANGENT_HANDLE_SIZE, new Color(0.6f, 0.6f, 0.6f));
            }

            // 出切线
            if (keyIndex < keys.Length - 1)
            {
                float outAngle = Mathf.Atan(key.outTangent);
                Vector2 outDir = new Vector2(Mathf.Cos(outAngle), -Mathf.Sin(outAngle)) * TANGENT_LINE_LENGTH;
                Vector2 outHandle = new Vector2(x, y) + outDir;

                Handles.DrawLine(new Vector3(x, y, 0), new Vector3(outHandle.x, outHandle.y, 0));
                DrawCircle(outHandle, TANGENT_HANDLE_SIZE, new Color(0.6f, 0.6f, 0.6f));
            }

            Handles.EndGUI();
        }

        private void DrawDiamond(Vector2 center, float size, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;

            Vector3[] points = {
                new Vector3(center.x, center.y - size / 2, 0),
                new Vector3(center.x + size / 2, center.y, 0),
                new Vector3(center.x, center.y + size / 2, 0),
                new Vector3(center.x - size / 2, center.y, 0)
            };

            Handles.DrawSolidRectangleWithOutline(points, color, Color.black);
            Handles.EndGUI();
        }

        private void DrawCircle(Vector2 center, float radius, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawSolidDisc(new Vector3(center.x, center.y, 0), Vector3.forward, radius);
            Handles.EndGUI();
        }

        private void DrawBoxSelection(Rect rect)
        {
            Rect selectionRect = new Rect(
                Mathf.Min(boxSelectStart.x, boxSelectEnd.x),
                Mathf.Min(boxSelectStart.y, boxSelectEnd.y),
                Mathf.Abs(boxSelectEnd.x - boxSelectStart.x),
                Mathf.Abs(boxSelectEnd.y - boxSelectStart.y)
            );

            EditorGUI.DrawRect(selectionRect, new Color(0.3f, 0.5f, 0.8f, 0.2f));
            Handles.BeginGUI();
            Handles.color = new Color(0.3f, 0.5f, 0.8f, 0.8f);
            Handles.DrawWireDisc(selectionRect.center, Vector3.forward, 0); // 占位，实际绘制边框
            Handles.EndGUI();

            // 绘制边框
            EditorGUI.DrawRect(new Rect(selectionRect.x, selectionRect.y, selectionRect.width, 1), new Color(0.3f, 0.5f, 0.8f));
            EditorGUI.DrawRect(new Rect(selectionRect.x, selectionRect.yMax - 1, selectionRect.width, 1), new Color(0.3f, 0.5f, 0.8f));
            EditorGUI.DrawRect(new Rect(selectionRect.x, selectionRect.y, 1, selectionRect.height), new Color(0.3f, 0.5f, 0.8f));
            EditorGUI.DrawRect(new Rect(selectionRect.xMax - 1, selectionRect.y, 1, selectionRect.height), new Color(0.3f, 0.5f, 0.8f));
        }

        private void DrawTimeIndicator(Rect rect, float currentTime)
        {
            if (currentTime < viewRangeMin || currentTime > viewRangeMax) return;

            float x = TimeToX(rect, currentTime);
            EditorGUI.DrawRect(new Rect(x - 1, rect.y, 2, rect.height), new Color(1f, 0.3f, 0.3f));
        }

        private void DrawValueScale(Rect rect)
        {
            float visibleValueRange = valueRangeMax - valueRangeMin;
            float valueStep = CalculateGridStep(visibleValueRange, rect.height, 40f);
            float startValue = Mathf.Floor(valueRangeMin / valueStep) * valueStep;

            for (float v = startValue; v <= valueRangeMax; v += valueStep)
            {
                if (v < valueRangeMin) continue;
                float y = ValueToY(rect, v);

                string label = v.ToString("F1");
                Rect labelRect = new Rect(rect.x + 2, y - 8, 50, 16);
                GUI.Label(labelRect, label, EditorStyles.miniLabel);
            }
        }

        private void HandleInput(Rect rect)
        {
            Event e = Event.current;

            // 鼠标滚轮缩放值范围
            if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                float zoomFactor = e.delta.y > 0 ? 1.1f : 0.9f;
                float mouseNormalized = (e.mousePosition.y - rect.y) / rect.height;
                float mouseValue = valueRangeMax - mouseNormalized * (valueRangeMax - valueRangeMin);

                float newRange = (valueRangeMax - valueRangeMin) * zoomFactor;
                valueRangeMin = mouseValue - (1f - mouseNormalized) * newRange;
                valueRangeMax = mouseValue + mouseNormalized * newRange;

                e.Use();
            }

            // 左键点击选择/拖拽关键帧
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                KeyframeHandle? hitKey = HitTestKeyframe(rect, e.mousePosition);

                if (hitKey.HasValue)
                {
                    if (e.control || e.command)
                    {
                        if (selectedKeyframes.Contains(hitKey.Value))
                            selectedKeyframes.Remove(hitKey.Value);
                        else
                            selectedKeyframes.Add(hitKey.Value);
                    }
                    else if (!selectedKeyframes.Contains(hitKey.Value))
                    {
                        selectedKeyframes.Clear();
                        selectedKeyframes.Add(hitKey.Value);
                    }

                    isDraggingKeyframe = true;
                    draggedKeyframe = hitKey.Value;
                    OnKeyframeSelectionChanged?.Invoke(selectedKeyframes);
                }
                else
                {
                    // 开始框选
                    if (!e.control && !e.command)
                    {
                        selectedKeyframes.Clear();
                        OnKeyframeSelectionChanged?.Invoke(selectedKeyframes);
                    }
                    isBoxSelecting = true;
                    boxSelectStart = e.mousePosition;
                    boxSelectEnd = e.mousePosition;
                }

                e.Use();
            }

            // 拖拽关键帧
            if (isDraggingKeyframe && e.type == EventType.MouseDrag)
            {
                float newTime = XToTime(rect, e.mousePosition.x);
                float newValue = YToValue(rect, e.mousePosition.y);

                OnKeyframeMoved?.Invoke(draggedKeyframe, newTime, newValue);
                e.Use();
            }

            // 框选
            if (isBoxSelecting && e.type == EventType.MouseDrag)
            {
                boxSelectEnd = e.mousePosition;
                UpdateBoxSelection(rect);
                e.Use();
            }

            // 鼠标释放
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                if (isDraggingKeyframe)
                {
                    isDraggingKeyframe = false;
                }
                if (isBoxSelecting)
                {
                    isBoxSelecting = false;
                }
                e.Use();
            }

            // 双击添加关键帧
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && rect.Contains(e.mousePosition))
            {
                float time = XToTime(rect, e.mousePosition.x);
                float value = YToValue(rect, e.mousePosition.y);

                // 找到最近的曲线
                if (curves.Count > 0)
                {
                    OnKeyframeAdded?.Invoke(curves[0].TrackIndex, time, value);
                }
                e.Use();
            }

            // Delete 键删除选中关键帧
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete && selectedKeyframes.Count > 0)
            {
                OnKeyframesDeleted?.Invoke(new HashSet<KeyframeHandle>(selectedKeyframes));
                selectedKeyframes.Clear();
                OnKeyframeSelectionChanged?.Invoke(selectedKeyframes);
                e.Use();
            }
        }

        private KeyframeHandle? HitTestKeyframe(Rect rect, Vector2 mousePos)
        {
            foreach (var binding in curves)
            {
                if (!binding.IsVisible || binding.Curve == null) continue;

                var keys = binding.Curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    var key = keys[i];
                    if (key.time < viewRangeMin || key.time > viewRangeMax) continue;

                    float x = TimeToX(rect, key.time);
                    float y = ValueToY(rect, key.value);

                    Rect hitRect = new Rect(x - KEYFRAME_HIT_SIZE / 2, y - KEYFRAME_HIT_SIZE / 2, KEYFRAME_HIT_SIZE, KEYFRAME_HIT_SIZE);
                    if (hitRect.Contains(mousePos))
                    {
                        return new KeyframeHandle(binding.TrackIndex, i);
                    }
                }
            }
            return null;
        }

        private void UpdateBoxSelection(Rect rect)
        {
            Rect selectionRect = new Rect(
                Mathf.Min(boxSelectStart.x, boxSelectEnd.x),
                Mathf.Min(boxSelectStart.y, boxSelectEnd.y),
                Mathf.Abs(boxSelectEnd.x - boxSelectStart.x),
                Mathf.Abs(boxSelectEnd.y - boxSelectStart.y)
            );

            selectedKeyframes.Clear();

            foreach (var binding in curves)
            {
                if (!binding.IsVisible || binding.Curve == null) continue;

                var keys = binding.Curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    var key = keys[i];
                    float x = TimeToX(rect, key.time);
                    float y = ValueToY(rect, key.value);

                    if (selectionRect.Contains(new Vector2(x, y)))
                    {
                        selectedKeyframes.Add(new KeyframeHandle(binding.TrackIndex, i));
                    }
                }
            }

            OnKeyframeSelectionChanged?.Invoke(selectedKeyframes);
        }

        // ========== 坐标转换 ==========
        private float TimeToX(Rect rect, float time)
        {
            float normalized = (time - viewRangeMin) / (viewRangeMax - viewRangeMin);
            return rect.x + normalized * rect.width;
        }

        private float XToTime(Rect rect, float x)
        {
            float normalized = (x - rect.x) / rect.width;
            return viewRangeMin + normalized * (viewRangeMax - viewRangeMin);
        }

        private float ValueToY(Rect rect, float value)
        {
            float normalized = (value - valueRangeMin) / (valueRangeMax - valueRangeMin);
            return rect.yMax - normalized * rect.height;
        }

        private float YToValue(Rect rect, float y)
        {
            float normalized = (rect.yMax - y) / rect.height;
            return valueRangeMin + normalized * (valueRangeMax - valueRangeMin);
        }
    }
}
