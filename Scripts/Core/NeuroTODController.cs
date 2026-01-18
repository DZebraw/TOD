using System;
using Unity.VisualScripting.YamlDotNet.Core.Tokens;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NeuroTOD
{
    /// <summary>
    /// NeuroTOD 主控制器
    /// 参照 NeuroTOD (Unreal) 项目设计
    /// </summary>
    [ExecuteAlways]
    public class NeuroTODController : MonoBehaviour
    {
        // ========== 单例 ==========

        private static NeuroTODController instance;
        public static NeuroTODController Instance => instance;

        // ========== 预设 ==========

        //[Header("Preset")]
        [Tooltip("激活的 TOD 预设")]
        [SerializeField]
        private TODPreset activePreset;

        // ========== 时间控制 ==========
        //TODO:自定义日出日落时间
        [SerializeField] private float sunriseTime = 6f;
        [SerializeField] private float sunsetTime = 18f;
        
        [Header("Time Control")]
        [Range(0f, 24f)]
        [Tooltip("当前时间 (0-24 小时制)")]
        [SerializeField]
        private float timeOfDay = 12f;

        [Tooltip("是否自动推进时间")]
        [SerializeField]
        private bool autoAdvanceTime = false;

        [Tooltip("一个完整日循环的实际时长 (秒)")]
        [SerializeField]
        private float dayLengthInSeconds = 1200f;

        [Tooltip("时间流逝速度倍率")]
        [SerializeField]
        private float timeScale = 1f;

        // ========== 光源引用 ==========
        
        [Tooltip("太阳方向光")]
        [SerializeField]
        private Light sunLight;

        [Tooltip("月亮方向光")]
        [SerializeField]
        private Light moonLight;

        // ========== HDRP Volume ==========
        
        [Tooltip("HDRP Volume")]
        [SerializeField]
        private Volume hdrpVolume;

        // ========== 私有状态 ==========

        private TimeManager timeManager;
        private PhysicallyBasedSky physicalSky;
        private Fog fog;
        private Exposure exposure;
        private IndirectLightingController indirectLighting;

        private bool isNight = false;
        private bool wasNight = false;

        // ========== 属性 ==========

        public TODPreset ActivePreset
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

        public bool IsNight => isNight;

        public bool AutoAdvanceTime
        {
            get => autoAdvanceTime;
            set => autoAdvanceTime = value;
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

        // ========== 生命周期 ==========

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            else if (instance != this)
            {
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            InitializeTimeManager();
            
            TODEvents.OnSunrise += () => Debug.Log($"Time {TimeOfDay}: 日出"); 
        }

        private void Start()
        {
            CacheVolumeComponents();
            UpdateAllSystems();
        }

        private void Update()
        {
            if (Application.isPlaying && autoAdvanceTime)
            {
                AdvanceTime(Time.deltaTime);
            }
        }

        private void OnValidate()
        {
            // 编辑器中实时预览
            CacheVolumeComponents();
            UpdateAllSystems();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        // ========== 初始化 ==========

        private void InitializeTimeManager()
        {
            timeManager = new TimeManager
            {
                autoAdvanceTime = autoAdvanceTime,
                dayLengthInSeconds = dayLengthInSeconds,
                timeScale = timeScale
            };
            timeManager.SetSunriseSunset(sunriseTime, sunsetTime);
            timeManager.SetPreset(activePreset);
            timeManager.SetTime(timeOfDay);

            // 订阅事件
            timeManager.OnTimeChanged += OnTimeManagerTimeChanged;
            timeManager.OnSunrise += OnTimeManagerSunrise;
            timeManager.OnSunset += OnTimeManagerSunset;
            timeManager.OnMidnight += OnTimeManagerMidnight;
        }

        private void CacheVolumeComponents()
        {
            if (hdrpVolume == null || hdrpVolume.profile == null)
            {
                physicalSky = null;
                fog = null;
                exposure = null;
                indirectLighting = null;
                return;
            }

            hdrpVolume.profile.TryGet(out physicalSky);
            hdrpVolume.profile.TryGet(out fog);
            hdrpVolume.profile.TryGet(out exposure);
            hdrpVolume.profile.TryGet(out indirectLighting);
        }

        // ========== 公共方法 ==========

        /// <summary>
        /// 设置当前时间 (0-24)
        /// </summary>
        public void SetTime(float hour)
        {
            timeOfDay = Mathf.Repeat(hour, 24f);
            if (timeManager != null)
            {
                timeManager.SetTime(timeOfDay);
            }
            UpdateAllSystems();
        }

        /// <summary>
        /// 设置归一化时间 (0-1)
        /// </summary>
        public void SetNormalizedTime(float normalized)
        {
            SetTime(Mathf.Clamp01(normalized) * 24f);
        }

        /// <summary>
        /// 获取归一化时间 (0-1)，支持 TimeRemap
        /// </summary>
        public float GetNormalizedTime()
        {
            float raw = timeOfDay / 24f;
            if (activePreset != null)
            {
                return activePreset.GetRemappedTime(raw);
            }
            return raw;
        }

        /// <summary>
        /// 设置预设
        /// </summary>
        public void SetPreset(TODPreset preset)
        {
            activePreset = preset;
            if (timeManager != null)
            {
                timeManager.SetPreset(preset);
            }
            TODEvents.RaisePresetChanged(preset);
            UpdateAllSystems();
        }

        /// <summary>
        /// 推进时间
        /// </summary>
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

        /// <summary>
        /// 获取格式化时间字符串 (HH:MM)
        /// </summary>
        public string GetFormattedTime()
        {
            int hours = Mathf.FloorToInt(timeOfDay);
            int minutes = Mathf.FloorToInt((timeOfDay - hours) * 60f);
            return $"{hours:D2}:{minutes:D2}";
        }

        // ========== 系统更新 ==========

        private void UpdateAllSystems()
        {
            if (activePreset == null)
            {
                return;
            }

            float normalizedTime = GetNormalizedTime();

            UpdateSunLight(normalizedTime);
            UpdateMoonLight(normalizedTime);
            UpdateSkyParameters(normalizedTime);
            UpdateFogParameters(normalizedTime);
            CheckDayNightTransition();
        }

        private void UpdateSunLight(float normalizedTime)
        {
            if (sunLight == null || activePreset == null)
            {
                return;
            }

            // 更新旋转
            sunLight.transform.rotation = activePreset.SampleSunRotation(normalizedTime);

            // 更新强度
            sunLight.intensity = activePreset.sunIntensityCurve.Evaluate(normalizedTime);

            // 更新颜色
            sunLight.color = activePreset.sunColorGradient.Evaluate(normalizedTime);
        }

        private void UpdateMoonLight(float normalizedTime)
        {
            if (moonLight == null || activePreset == null)
            {
                return;
            }

            // 更新旋转
            moonLight.transform.rotation = activePreset.SampleMoonRotation(normalizedTime);

            // 更新强度
            moonLight.intensity = activePreset.moonIntensityCurve.Evaluate(normalizedTime);

            // 更新颜色
            moonLight.color = activePreset.moonColorGradient.Evaluate(normalizedTime);
        }

        private void UpdateSkyParameters(float normalizedTime)
        {
            if (activePreset == null)
            {
                return;
            }

            // 更新星空发射强度
            if (physicalSky != null)
            {
                physicalSky.spaceEmissionMultiplier.value = activePreset.starEmissionCurve.Evaluate(normalizedTime);
            }

            // 更新间接光照
            if (indirectLighting != null)
            {
                float skyIntensity = activePreset.skyLightIntensityCurve.Evaluate(normalizedTime);
                indirectLighting.indirectDiffuseLightingMultiplier.value = skyIntensity;
            }
        }

        private void UpdateFogParameters(float normalizedTime)
        {
            if (fog == null || activePreset == null)
            {
                return;
            }

            // 更新雾距离 (Mean Free Path)
            fog.meanFreePath.value = activePreset.fogDistanceCurve.Evaluate(normalizedTime);

            // 更新雾颜色
            fog.albedo.value = activePreset.fogColorGradient.Evaluate(normalizedTime);
        }

        private void CheckDayNightTransition()
        {
            // 判断当前是否为夜间 (18:00 - 06:00)
            isNight = timeOfDay < sunriseTime || timeOfDay >= sunsetTime;

            if (wasNight != isNight)
            {
                if (isNight)
                {
                    StartNight();
                }
                else
                {
                    StartDay();
                }
                wasNight = isNight;
            }
        }

        private void StartDay()
        {
            if (sunLight != null)
            {
                sunLight.shadows = LightShadows.Soft;
            }
            if (moonLight != null)
            {
                moonLight.shadows = LightShadows.None;
            }
        }

        private void StartNight()
        {
            if (sunLight != null)
            {
                sunLight.shadows = LightShadows.None;
            }
            if (moonLight != null)
            {
                moonLight.shadows = LightShadows.Soft;
            }
        }

        // ========== TimeManager 事件转发到 TODEvents 广播 ==========

        private void OnTimeManagerTimeChanged(float normalizedTime)
        {
            TODEvents.RaiseTimeChanged(normalizedTime);
        }

        private void OnTimeManagerSunrise()
        {
            TODEvents.RaiseSunrise();
        }

        private void OnTimeManagerSunset()
        {
            TODEvents.RaiseSunset();
        }

        private void OnTimeManagerMidnight()
        {
            TODEvents.RaiseMidnight();
        }
    }
}
