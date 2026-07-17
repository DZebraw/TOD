using UnityEngine;

namespace DawnTOD
{
    [ExecuteAlways]
    public class DawnWeatherController : MonoBehaviour
    {
        [Tooltip("激活的 TOD 预设")]
        [SerializeField]
        private DawnWeatherPreset activePreset;

        [SerializeField] private float sunriseTime = 6f;
        [SerializeField] private float sunsetTime = 18f;

        [Header("Time Control")]
        [Range(0f, 24f)]
        [Tooltip("当前时间（0-24 小时制）")]
        [SerializeField]
        private float timeOfDay = 12f;

        [Tooltip("一个完整日循环的实际时长（秒）")]
        [SerializeField]
        private float dayLengthInSeconds = 1200f;

        [Tooltip("时间流逝速度倍率")]
        [SerializeField]
        private float timeScale = 1f;

        public DawnWeatherPreset ActivePreset
        {
            get => activePreset;
            set => SetPreset(value);
        }

        public float TimeOfDay
        {
            get => timeOfDay;
            set => SetTime(value);
        }

        public float SunRaiseTime => sunriseTime;
        public float SunSetTime => sunsetTime;
        public float NormalizedTime => GetNormalizedTime();

        public bool IsNight
        {
            get
            {
                float hour = DawnTODSystem.Instance != null
                    ? DawnTODSystem.Instance.TimeOfDay
                    : timeOfDay;
                return hour < sunriseTime || hour >= sunsetTime;
            }
        }

        public float DayLengthInSeconds
        {
            get => dayLengthInSeconds;
            set => dayLengthInSeconds = Mathf.Max(1f, value);
        }

        public float TimeScale
        {
            get => timeScale;
            set => timeScale = value;
        }

        private void OnEnable()
        {
            DawnTODSystem todSystem = DawnTODSystem.Instance;
            if (todSystem == null)
            {
                return;
            }

            SyncTimeSettings(todSystem);
            todSystem.RegisterController(this);
        }

        private void OnDisable()
        {
            DawnTODSystem todSystem = DawnTODSystem.Instance;
            if (todSystem != null)
            {
                todSystem.UnregisterController(this);
            }
        }

        private void OnValidate()
        {
            timeOfDay = Mathf.Repeat(timeOfDay, 24f);
            dayLengthInSeconds = Mathf.Max(1f, dayLengthInSeconds);
            DawnTODSystem todSystem = DawnTODSystem.Instance;
            if (todSystem != null)
            {
                SyncTimeSettings(todSystem);
                todSystem.RefreshWeatherBlendingSystem();
            }
        }

        /// <summary>
        /// 兼容旧调用方；场景输出统一由 DawnTODSystem 重新评估并写入。
        /// </summary>
        public void Refresh()
        {
            DawnTODSystem todSystem = DawnTODSystem.Instance;
            if (todSystem != null && todSystem.isActiveAndEnabled)
            {
                todSystem.RefreshWeatherBlendingSystem();
            }
        }

        /// <summary>
        /// 设置当前时间（0-24），并委托 DawnTODSystem 完成一次完整评估。
        /// </summary>
        public void SetTime(float hour)
        {
            timeOfDay = Mathf.Repeat(hour, 24f);
            DawnTODSystem todSystem = DawnTODSystem.Instance;
            if (todSystem != null)
            {
                todSystem.SetTime(timeOfDay);
            }
        }

        /// <summary>
        /// 获取用于预览当前天气的归一化时间（0-1）。
        /// </summary>
        public float GetNormalizedTime()
        {
            DawnTODSystem todSystem = DawnTODSystem.Instance;
            return todSystem != null
                ? todSystem.NormalizedTime
                : timeOfDay / 24f;
        }

        public void SetPreset(DawnWeatherPreset preset)
        {
            activePreset = preset;
            TODEvents.RaisePresetChanged(preset);
            Refresh();
        }

        public void AdvanceTime(float deltaTime)
        {
            if (dayLengthInSeconds <= 0f)
            {
                return;
            }

            float timePerSecond = 24f / dayLengthInSeconds;
            float newTime = timeOfDay + timePerSecond * deltaTime * timeScale;
            SetTime(newTime);
        }

        public string GetFormattedTime()
        {
            int hours = Mathf.FloorToInt(timeOfDay);
            int minutes = Mathf.FloorToInt((timeOfDay - hours) * 60f);
            return $"{hours:D2}:{minutes:D2}";
        }

        private void SyncTimeSettings(DawnTODSystem todSystem)
        {
            sunriseTime = todSystem.SunRaiseTime;
            sunsetTime = todSystem.SunSetTime;
        }
    }
}
