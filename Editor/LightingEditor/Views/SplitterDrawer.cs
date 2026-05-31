using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using DawnTOD;

namespace DawnTODEditor
{
    /// <summary>
    /// 分割条绘制器
    /// 封装分割条的绘制与拖拽交互，与主窗口解耦，保持原有视觉和交互效果
    /// </summary>
    public class SplitterDrawer : IEditorDrawer, IDisposable
    {
        public const float SPLITTER_WIDTH = 6f;

        private bool _isDraggingSplitter;

        private LightingEditorState _state;
        private TrackManager _trackManager;

        private Action<float> _onOutlinerWidthChanged;

        private float _minOutlinerWidth;
        private float _maxWidthRatio = 0.5f;

        /// <summary>
        /// 注入配置：宽度变更回调 + 最小左侧宽度
        /// </summary>
        /// <param name="onOutlinerWidthChanged">左侧宽度变更回调</param>
        /// <param name="minOutlinerWidth">左侧最小宽度限制</param>
        public void SetSplitterConfig(Action<float> onOutlinerWidthChanged, float minOutlinerWidth)
        {
            _onOutlinerWidthChanged = onOutlinerWidthChanged;
            _minOutlinerWidth = minOutlinerWidth;
        }


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
        /// 执行绘制（接口实现，绘制分割条背景和拖拽光标）
        /// </summary>
        public void Draw(Rect drawRect)
        {
            DrawSplitterVisual(drawRect);
        }

        /// <summary>
        /// 处理事件（接口实现，处理拖拽的按下/拖拽/抬起逻辑）
        /// </summary>
        public bool HandleEvent(Rect drawRect, Event evt)
        {
            return HandleSplitterInteraction(drawRect, evt);
        }

        /// <summary>
        /// 轨道数据变更回调（预留扩展，当前无实际逻辑）
        /// </summary>
        private void OnTracksRefreshed()
        {
            if (_isDraggingSplitter)
            {
                _isDraggingSplitter = false;
            }
        }

        /// <summary>
        /// 释放资源（解除事件监听，防止内存泄漏，完善 IDisposable 实现）
        /// </summary>
        public void Dispose()
        {
            if (_trackManager != null)
            {
                _trackManager.OnTracksRefreshed -= OnTracksRefreshed;
            }
        }

        /// <summary>
        /// 核心绘制方法：绘制分割条视觉效果（保留原有核心逻辑，加强健壮性）
        /// </summary>
        private void DrawSplitterVisual(Rect rect)
        {
            if (rect.width <= 0 || rect.height <= 0) return;

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
        }

        /// <summary>
        /// 核心交互方法：处理拖拽逻辑（保留原有核心逻辑，加强健壮性）
        /// </summary>
        private bool HandleSplitterInteraction(Rect rect, Event evt)
        {
            if (_onOutlinerWidthChanged == null || evt == null || rect.width <= 0 || rect.height <= 0) return false;

            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                _isDraggingSplitter = true;
                evt.Use();
                return true;
            }

            if (_isDraggingSplitter)
            {
                if (evt.type == EventType.MouseDrag)
                {
                    float maxWidth = EditorGUIUtility.currentViewWidth > 0
                        ? EditorGUIUtility.currentViewWidth * _maxWidthRatio
                        : _minOutlinerWidth * 2;

                    float newOutlinerWidth = Mathf.Clamp(
                        evt.mousePosition.x,
                        _minOutlinerWidth,
                        maxWidth
                    );

                    _onOutlinerWidthChanged(newOutlinerWidth);
                    evt.Use();
                    return true;
                }
                else if (evt.type == EventType.MouseUp)
                {
                    _isDraggingSplitter = false;
                    evt.Use();
                    return true;
                }
            }
            return false;
        }
    }
}