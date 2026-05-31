using System;
using UnityEngine;

namespace DawnTOD
{
    /// <summary>
    /// 时间管理器 - 管理 TOD 时间流逝和事件
    /// </summary>
    [Serializable]
    public class TimeManager
    {
        /// <summary>
        /// 当前时间 (0-24 小时制)
        /// </summary>
        [SerializeField]
        private float timeOfDay = 12f;

        /// <summary>
        /// 日出时间
        /// </summary>
        [SerializeField] private float sunriseTime = 6f;

        /// <summary>
        /// 日落时间
        /// </summary>
        [SerializeField] private float sunsetTime = 18f;

        /// <summary>
        /// 是否自动推进时间
        /// </summary>
        public bool autoAdvanceTime = false;

        /// <summary>
        /// 一个完整日循环的实际时长 (秒)
        /// </summary>
        public float dayLengthInSeconds = 1200f;

        /// <summary>
        /// 时间流逝速度倍率
        /// </summary>
        public float timeScale = 1f;

        /// <summary>
        /// 当前使用的预设（用于 TimeRemap）
        /// </summary>
        //private WeatherPreset activePreset;

        // ========== 事件 ==========

        /// <summary>
        /// 时间变化事件 (参数: 归一化时间 0-1)
        /// </summary>
        public event Action<float> OnTimeChanged;

        /// <summary>
        /// 日出事件
        /// </summary>
        public event Action OnSunrise;

        /// <summary>
        /// 日落事件
        /// </summary>
        public event Action OnSunset;

        /// <summary>
        /// 午夜事件 (新的一天开始)
        /// </summary>
        public event Action OnMidnight;

        // ========== 状态追踪 ==========

        private bool wasNight = false;
        private float previousTimeOfDay = 0f;

        // ========== 属性 ==========

        /// <summary>
        /// 当前时间 (0-24)
        /// </summary>
        public float TimeOfDay
        {
            get => timeOfDay;
            set => SetTime(value);
        }

        /// <summary>
        /// 获取原始归一化时间 (0-1)，不经过 TimeRemap
        /// </summary>
        //public float RawNormalizedTime => timeOfDay / 24f;

        /// <summary>
        /// 获取归一化时间 (0-1)，经过 TimeRemap 处理
        /// </summary>
        //public float NormalizedTime
        //{
        //    get
        //    {
        //        float raw = RawNormalizedTime;
        //        if (activePreset != null)
        //        {
        //            return activePreset.GetRemappedTime(raw);
        //        }
        //        return raw;
        //    }
        //}

        /// <summary>
        /// 是否为夜间 (18:00 - 06:00)
        /// </summary>
        public bool IsNight => timeOfDay < sunriseTime || timeOfDay >= sunsetTime;

        /// <summary>
        /// 是否为白天 (06:00 - 18:00)
        /// </summary>
        public bool IsDay => !IsNight;

        // ========== 方法 ==========

        /// <summary>
        /// 设置当前预设
        /// </summary>
        public void SetPreset(DawnWeatherPreset preset)
        {
            //activePreset = preset;
        }

        /// <summary>
        /// 设置日出日落时间
        /// </summary>
        public void SetSunriseSunset(float sunrise, float sunset)
        {
            sunriseTime = Mathf.Clamp(sunrise, 0f, 24f);
            sunsetTime = Mathf.Clamp(sunset, 0f, 24f);
        }

        /// <summary>
        /// 设置时间 (0-24)
        /// </summary>
        public void SetTime(float hour)
        {
            float clampedTime = Mathf.Repeat(hour, 24f);
            
            if (Mathf.Abs(timeOfDay - clampedTime) > 0.0001f)
            {
                previousTimeOfDay = timeOfDay;
                timeOfDay = clampedTime;
                
                CheckTimeEvents();
                //OnTimeChanged?.Invoke(NormalizedTime);
            }
        }

        /// <summary>
        /// 设置归一化时间 (0-1)
        /// </summary>
        public void SetNormalizedTime(float normalized)
        {
            SetTime(Mathf.Clamp01(normalized) * 24f);
        }

        /// <summary>
        /// 更新时间（每帧调用）
        /// </summary>
        public void Update(float deltaTime)
        {
            if (!autoAdvanceTime || dayLengthInSeconds <= 0f)
            {
                return;
            }

            float timePerSecond = 24f / dayLengthInSeconds;
            float newTime = timeOfDay + timePerSecond * deltaTime * timeScale;
            SetTime(newTime);
        }

        /// <summary>
        /// 检查并触发时间事件
        /// </summary>
        private void CheckTimeEvents()
        {
            // 检查午夜（跨越 24:00）
            if (previousTimeOfDay > timeOfDay && previousTimeOfDay > 20f && timeOfDay < 4f)
            {
                OnMidnight?.Invoke();
            }

            // 检查日出 (06:00)
            bool isCurrentlyNight = IsNight;
            if (wasNight && !isCurrentlyNight)
            {
                OnSunrise?.Invoke();
            }
            // 检查日落 (18:00)
            else if (!wasNight && isCurrentlyNight)
            {
                OnSunset?.Invoke();
            }

            wasNight = isCurrentlyNight;
        }

        /// <summary>
        /// 获取格式化时间字符串 (HH:MM)
        /// </summary>
        public string GetFormattedTime()
        {
            int hours = Mathf.FloorToInt(timeOfDay);
            int minutes = Mathf.FloorToInt((timeOfDay - hours) * 60f);
            return $"{hours:D2}:{minutes:D2}";
        }

        /// <summary>
        /// 获取格式化时间字符串 (HH:MM:SS)
        /// </summary>
        public string GetFormattedTimeWithSeconds()
        {
            int hours = Mathf.FloorToInt(timeOfDay);
            float remainder = (timeOfDay - hours) * 60f;
            int minutes = Mathf.FloorToInt(remainder);
            int seconds = Mathf.FloorToInt((remainder - minutes) * 60f);
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }
    }
}
