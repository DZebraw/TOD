using System;
using UnityEngine;

namespace NeuroTOD
{
    /// <summary>
    /// TOD 事件系统 - 提供昼夜循环相关的事件订阅
    /// </summary>
    public static class TODEvents
    {
        /// <summary>
        /// 时间变化事件 (参数: 归一化时间 0-1)
        /// </summary>
        public static event Action<float> OnTimeChanged;

        /// <summary>
        /// 日出事件
        /// </summary>
        public static event Action OnSunrise;

        /// <summary>
        /// 日落事件
        /// </summary>
        public static event Action OnSunset;

        /// <summary>
        /// 午夜事件
        /// </summary>
        public static event Action OnMidnight;

        /// <summary>
        /// 预设切换事件 (参数: 新预设)
        /// </summary>
        public static event Action<TODPreset> OnPresetChanged;

        // ========== 内部触发方法 ==========

        internal static void RaiseTimeChanged(float normalizedTime)
        {
            OnTimeChanged?.Invoke(normalizedTime);
        }

        internal static void RaiseSunrise()
        {
            OnSunrise?.Invoke();
        }

        internal static void RaiseSunset()
        {
            OnSunset?.Invoke();
        }

        internal static void RaiseMidnight()
        {
            OnMidnight?.Invoke();
        }

        internal static void RaisePresetChanged(TODPreset preset)
        {
            OnPresetChanged?.Invoke(preset);
        }

        /// <summary>
        /// 清除所有事件订阅（用于场景切换时）
        /// </summary>
        public static void ClearAllListeners()
        {
            OnTimeChanged = null;
            OnSunrise = null;
            OnSunset = null;
            OnMidnight = null;
            OnPresetChanged = null;
        }
    }
}
