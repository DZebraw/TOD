using System;
using UnityEngine;
using UnityEditor;

namespace NeuroTODEditor
{
    /// <summary>
    /// 时间轴刻度尺视图
    /// 支持缩放、拖拽定位和时间标记
    /// </summary>
    public class TimelineView
    {
        // ========== 常量 ==========
        private const float RULER_HEIGHT = 24f;
        private const float MAJOR_TICK_HEIGHT = 12f;
        private const float MINOR_TICK_HEIGHT = 6f;
        private const float TIME_INDICATOR_WIDTH = 2f;

        // ========== 状态 ==========
        private float viewRangeMin = 0f;
        private float viewRangeMax = 1f;
        private float currentTime = 0.5f;
        private bool isDraggingTime;
        private bool isDraggingView;
        private float dragStartX;
        private float dragStartViewMin;
        private float dragStartViewMax;

        // ========== 事件 ==========
        public event Action<float> OnTimeChanged;
        public event Action<float, float> OnViewRangeChanged;

        // ========== 属性 ==========
        public float CurrentTime
        {
            get => currentTime;
            set => currentTime = Mathf.Clamp01(value);
        }

        public float ViewRangeMin
        {
            get => viewRangeMin;
            set => viewRangeMin = Mathf.Clamp01(value);
        }

        public float ViewRangeMax
        {
            get => viewRangeMax;
            set => viewRangeMax = Mathf.Clamp01(value);
        }

        /// <summary>
        /// 绘制时间轴刻度尺
        /// </summary>
        public void Draw(Rect rect)
        {
            // 背景
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));

            // 计算可见范围
            float visibleRange = viewRangeMax - viewRangeMin;
            if (visibleRange < 0.001f) visibleRange = 1f;

            // 根据缩放级别选择刻度间隔
            float[] intervals = { 1f / 24f, 1f / 12f, 1f / 6f, 0.25f, 0.5f, 1f };
            float selectedInterval = 1f / 24f;
            foreach (float interval in intervals)
            {
                float pixelsPerInterval = (interval / visibleRange) * rect.width;
                if (pixelsPerInterval >= 40f)
                {
                    selectedInterval = interval;
                    break;
                }
            }

            // 绘制刻度
            float startTime = Mathf.Floor(viewRangeMin / selectedInterval) * selectedInterval;
            for (float t = startTime; t <= viewRangeMax + selectedInterval; t += selectedInterval)
            {
                if (t < viewRangeMin) continue;
                if (t > viewRangeMax) break;

                float normalizedPos = (t - viewRangeMin) / visibleRange;
                float x = rect.x + normalizedPos * rect.width;

                // 主刻度
                bool isMajor = Mathf.Approximately(t % (selectedInterval * 4), 0f) || Mathf.Approximately(t, 0f) || Mathf.Approximately(t, 1f);
                float tickHeight = isMajor ? MAJOR_TICK_HEIGHT : MINOR_TICK_HEIGHT;

                EditorGUI.DrawRect(new Rect(x, rect.yMax - tickHeight, 1, tickHeight), new Color(0.5f, 0.5f, 0.5f));

                // 时间标签
                if (isMajor || selectedInterval >= 1f / 12f)
                {
                    float hours = t * 24f;
                    int h = Mathf.FloorToInt(hours);
                    int m = Mathf.FloorToInt((hours - h) * 60f);
                    string label = m == 0 ? $"{h}:00" : $"{h}:{m:D2}";
                    
                    Rect labelRect = new Rect(x - 20, rect.y, 40, 14);
                    GUI.Label(labelRect, label, EditorStyles.miniLabel);
                }
            }

            // 当前时间指示线
            if (currentTime >= viewRangeMin && currentTime <= viewRangeMax)
            {
                float normalizedPos = (currentTime - viewRangeMin) / visibleRange;
                float timeX = rect.x + normalizedPos * rect.width;
                EditorGUI.DrawRect(new Rect(timeX - TIME_INDICATOR_WIDTH / 2, rect.y, TIME_INDICATOR_WIDTH, rect.height), new Color(1f, 0.3f, 0.3f));

                // 时间指示器三角形
                DrawTimeIndicatorHead(timeX, rect.y);
            }

            // 处理输入
            HandleInput(rect);
        }

        private void DrawTimeIndicatorHead(float x, float y)
        {
            // 简单的矩形头部
            Rect headRect = new Rect(x - 5, y, 10, 8);
            EditorGUI.DrawRect(headRect, new Color(1f, 0.3f, 0.3f));
        }

        private void HandleInput(Rect rect)
        {
            Event e = Event.current;

            // 鼠标滚轮缩放
            if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                float zoomFactor = e.delta.y > 0 ? 1.1f : 0.9f;
                float mouseNormalized = (e.mousePosition.x - rect.x) / rect.width;
                float mouseTime = viewRangeMin + mouseNormalized * (viewRangeMax - viewRangeMin);

                float newRange = (viewRangeMax - viewRangeMin) * zoomFactor;
                newRange = Mathf.Clamp(newRange, 0.01f, 1f);

                float newMin = mouseTime - mouseNormalized * newRange;
                float newMax = mouseTime + (1f - mouseNormalized) * newRange;

                // 限制范围
                if (newMin < 0f)
                {
                    newMax -= newMin;
                    newMin = 0f;
                }
                if (newMax > 1f)
                {
                    newMin -= (newMax - 1f);
                    newMax = 1f;
                }

                viewRangeMin = Mathf.Max(0f, newMin);
                viewRangeMax = Mathf.Min(1f, newMax);
                OnViewRangeChanged?.Invoke(viewRangeMin, viewRangeMax);

                e.Use();
            }

            // 左键点击/拖拽设置时间
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                isDraggingTime = true;
                SetTimeFromMousePosition(rect, e.mousePosition.x);
                e.Use();
            }

            if (isDraggingTime)
            {
                if (e.type == EventType.MouseDrag)
                {
                    SetTimeFromMousePosition(rect, e.mousePosition.x);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    isDraggingTime = false;
                    e.Use();
                }
            }

            // 中键拖拽平移视图
            if (e.type == EventType.MouseDown && e.button == 2 && rect.Contains(e.mousePosition))
            {
                isDraggingView = true;
                dragStartX = e.mousePosition.x;
                dragStartViewMin = viewRangeMin;
                dragStartViewMax = viewRangeMax;
                e.Use();
            }

            if (isDraggingView)
            {
                if (e.type == EventType.MouseDrag)
                {
                    float deltaX = e.mousePosition.x - dragStartX;
                    float deltaTime = -deltaX / rect.width * (dragStartViewMax - dragStartViewMin);

                    float newMin = dragStartViewMin + deltaTime;
                    float newMax = dragStartViewMax + deltaTime;

                    // 限制范围
                    if (newMin < 0f)
                    {
                        newMax -= newMin;
                        newMin = 0f;
                    }
                    if (newMax > 1f)
                    {
                        newMin -= (newMax - 1f);
                        newMax = 1f;
                    }

                    viewRangeMin = Mathf.Max(0f, newMin);
                    viewRangeMax = Mathf.Min(1f, newMax);
                    OnViewRangeChanged?.Invoke(viewRangeMin, viewRangeMax);

                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    isDraggingView = false;
                    e.Use();
                }
            }
        }

        private void SetTimeFromMousePosition(Rect rect, float mouseX)
        {
            float normalizedPos = (mouseX - rect.x) / rect.width;
            float newTime = viewRangeMin + normalizedPos * (viewRangeMax - viewRangeMin);
            currentTime = Mathf.Clamp01(newTime);
            OnTimeChanged?.Invoke(currentTime);
        }

        /// <summary>
        /// 缩放到适合全部内容
        /// </summary>
        public void ZoomToFit()
        {
            viewRangeMin = 0f;
            viewRangeMax = 1f;
            OnViewRangeChanged?.Invoke(viewRangeMin, viewRangeMax);
        }

        /// <summary>
        /// 缩放到指定范围
        /// </summary>
        public void ZoomToRange(float min, float max)
        {
            viewRangeMin = Mathf.Clamp01(min);
            viewRangeMax = Mathf.Clamp01(max);
            if (viewRangeMax <= viewRangeMin)
            {
                viewRangeMax = viewRangeMin + 0.01f;
            }
            OnViewRangeChanged?.Invoke(viewRangeMin, viewRangeMax);
        }

        /// <summary>
        /// 确保当前时间在可见范围内
        /// </summary>
        public void EnsureTimeVisible()
        {
            if (currentTime < viewRangeMin)
            {
                float range = viewRangeMax - viewRangeMin;
                viewRangeMin = currentTime;
                viewRangeMax = currentTime + range;
                if (viewRangeMax > 1f)
                {
                    viewRangeMax = 1f;
                    viewRangeMin = 1f - range;
                }
                OnViewRangeChanged?.Invoke(viewRangeMin, viewRangeMax);
            }
            else if (currentTime > viewRangeMax)
            {
                float range = viewRangeMax - viewRangeMin;
                viewRangeMax = currentTime;
                viewRangeMin = currentTime - range;
                if (viewRangeMin < 0f)
                {
                    viewRangeMin = 0f;
                    viewRangeMax = range;
                }
                OnViewRangeChanged?.Invoke(viewRangeMin, viewRangeMax);
            }
        }
    }
}
