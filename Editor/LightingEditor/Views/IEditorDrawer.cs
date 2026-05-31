using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;

namespace DawnTODEditor
{
    /// <summary>
    /// 绘制器基础接口
    /// </summary>
    public interface IEditorDrawer
    {
        /// <summary>
        /// 初始化绘制器（传递必要的上下文）
        /// </summary>
        void Initialize(LightingEditorState state, TrackManager trackManager);

        /// <summary>
        /// 执行绘制
        /// </summary>
        void Draw(Rect drawRect);

        /// <summary>
        /// 处理绘制区域的事件（鼠标/键盘）
        /// </summary>
        bool HandleEvent(Rect drawRect, Event evt);
    }

    /// <summary>
    /// 带状态回调的绘制器接口
    /// </summary>
    public interface IStateAwareDrawer : IEditorDrawer
    {
        /// <summary>
        /// 注册状态变更回调
        /// </summary>
        void RegisterCallbacks(Action onRepaint);
    }
}