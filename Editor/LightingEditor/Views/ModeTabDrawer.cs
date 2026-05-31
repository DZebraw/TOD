using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DawnTODEditor;

// 注：保持命名空间统一，与其他绘制器对齐
namespace DawnTODEditor
{
    /// <summary>
    /// 模式标签页绘制器
    /// 封装 Keyframes/Curves 模式切换的UI绘制与交互
    /// </summary>
    public class ModeTabDrawer : IEditorDrawer, IDisposable
    {
        private LightingEditorState _state;
        private TrackManager _trackManager;

        private Action _onModeChanged;

        private const float TAB_WIDTH = 80f;
        private readonly Color _selectedBgColor = new Color(0.3f, 0.5f, 0.8f);

        /// <summary>
        /// 初始化绘制器（实现 IEditorDrawer 接口，注入状态和 TrackManager）
        /// 注：当前 ModeTab 不依赖轨道数据，TrackManager 仅为遵循统一接口，后续可扩展
        /// </summary>
        /// <param name="state">编辑器全局状态</param>
        /// <param name="trackManager">轨道管理器</param>
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
        /// 注入模式变更回调
        /// </summary>
        /// <param name="onModeChanged">模式变更后的回调方法</param>
        public void SetModeChangedCallback(Action onModeChanged)
        {
            _onModeChanged = onModeChanged;
        }

        /// <summary>
        /// 执行绘制（接口实现，绘制两个模式Tab）
        /// </summary>
        /// <param name="drawRect">绘制区域</param>
        public void Draw(Rect drawRect)
        {
            if (_state == null || drawRect.width <= 0 || drawRect.height <= 0) return;

            DrawModeTabs(drawRect);
        }

        /// <summary>
        /// 处理绘制区域事件（接口实现，当前无额外事件处理，返回false）
        /// </summary>
        /// <param name="drawRect">绘制区域</param>
        /// <param name="evt">当前事件</param>
        /// <returns>是否处理了该事件</returns>
        public bool HandleEvent(Rect drawRect, Event evt)
        {
            return false;
        }

        /// <summary>
        /// 轨道数据变更回调
        /// </summary>
        private void OnTracksRefreshed()
        {
            // 轨道刷新后默认切回 Keyframes 模式
            // if (_state.CurrentEditorMode != EditorMode.Keyframes)
            // {
            //     _state.CurrentEditorMode = EditorMode.Keyframes;
            //     _onModeChanged?.Invoke();
            // }
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
        /// 核心绘制方法：绘制两个模式Tab，保留原有逻辑并加强健壮性
        /// </summary>
        /// <param name="drawRect">绘制区域</param>
        private void DrawModeTabs(Rect drawRect)
        {
            Color defaultBgColor = GUI.backgroundColor;

            // 1. 绘制Tab背景
            EditorGUI.DrawRect(drawRect, new Color(0.2f, 0.2f, 0.2f));

            // 2. 计算两个Tab的位置（避免超出绘制区域）
            float tabY = drawRect.y + 2;
            float tabHeight = drawRect.height - 4;
            Rect keyframesTab = new Rect(drawRect.x + 4, tabY, Mathf.Min(TAB_WIDTH, drawRect.width / 2 - 6), tabHeight);
            Rect curvesTab = new Rect(keyframesTab.xMax + 4, tabY, Mathf.Min(TAB_WIDTH, drawRect.width - keyframesTab.xMax - 6), tabHeight);

            // 3. 绘制 Keyframes Tab
            GUI.backgroundColor = _state.CurrentEditorMode == EditorMode.Keyframes ? _selectedBgColor : defaultBgColor;
            if (GUI.Button(keyframesTab, "Keyframes"))
            {
                if (_state.CurrentEditorMode != EditorMode.Keyframes)
                {
                    _state.CurrentEditorMode = EditorMode.Keyframes;
                    _onModeChanged?.Invoke();
                }
            }

            // 4. 绘制 Curves Tab
            GUI.backgroundColor = _state.CurrentEditorMode == EditorMode.Curves ? _selectedBgColor : defaultBgColor;
            if (GUI.Button(curvesTab, "Curves"))
            {
                if (_state.CurrentEditorMode != EditorMode.Curves)
                {
                    _state.CurrentEditorMode = EditorMode.Curves;
                    _onModeChanged?.Invoke();
                }
            }

            // 5. 恢复默认背景色（避免污染其他UI，关键优化）
            GUI.backgroundColor = defaultBgColor;
        }
    }
}