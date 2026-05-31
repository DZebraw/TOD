using System;
using UnityEngine;
using UnityEngine.Rendering;
#if USING_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace DawnTOD
{
    [ExecuteAlways]
    public class DawnWeatherController : MonoBehaviour 
    {
        // ========== 预设 ==========
        [Tooltip("激活的 TOD 预设")]
        [SerializeField]
        private DawnWeatherPreset activePreset;

        // ========== 时间控制 ==========
        [SerializeField] private float sunriseTime = 6f;
        [SerializeField] private float sunsetTime = 18f;
        
        [Header("Time Control")]
        [Range(0f, 24f)]
        [Tooltip("当前时间 (0-24 小时制)")]
        [SerializeField]
        private float timeOfDay = 12f;

        [Tooltip("一个完整日循环的实际时长 (秒)")]
        [SerializeField]
        private float dayLengthInSeconds = 1200f;

        [Tooltip("时间流逝速度倍率")]
        [SerializeField]
        private float timeScale = 1f;

        //========光源引用(TODSystem)==========
        private Light sunLight;
        private Light moonLight;
        private Volume hdrpVolume;

        // ========== 私有状态 ==========
        private TimeManager timeManager;
#if USING_HDRP
        private PhysicallyBasedSky physicalSky;
        private Fog fog;
        private Exposure exposure;
        private IndirectLightingController indirectLighting;
#endif
        private bool isNight = false;
        private bool wasNight = false;

        // ========== 属性 ==========
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
        public bool IsNight => isNight;

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
        private void OnEnable()
        {
            if (DawnTODSystem.Instance != null)
            {
                sunLight = DawnTODSystem.Instance.sunLight;
                moonLight = DawnTODSystem.Instance.moonLight;
#if USING_HDRP
                hdrpVolume = DawnTODSystem.Instance.hdrpVolume;
#endif
                sunriseTime = DawnTODSystem.Instance.SunRaiseTime;
                sunsetTime = DawnTODSystem.Instance.SunSetTime;
            }
            else
            {
                Debug.LogError("DawnTODSystem does not exist. Please add DawnTODSystem first.");
            }
            // 初始化TimeManager
            InitializeTimeManager();
        }

        private void InitializeTimeManager()
        {
            if (timeManager == null)
            {
                timeManager = new TimeManager
                {
                    autoAdvanceTime = false,
                    dayLengthInSeconds = dayLengthInSeconds,
                    timeScale = timeScale
                };
                timeManager.SetSunriseSunset(sunriseTime, sunsetTime);
                timeManager.SetTime(timeOfDay);
            }
        }

        private void OnValidate()
        {
            if (DawnTODSystem.Instance != null)
            {
                sunLight = DawnTODSystem.Instance.sunLight;
                moonLight = DawnTODSystem.Instance.moonLight;
#if USING_HDRP
                hdrpVolume = DawnTODSystem.Instance.hdrpVolume;
#endif
                sunriseTime = DawnTODSystem.Instance.SunRaiseTime;
                sunsetTime = DawnTODSystem.Instance.SunSetTime;
                CacheVolumeComponents();
            }
            else
            {
                Debug.LogError("DawnTODSystem does not exist. Please add DawnTODSystem first.");
            }
            InitializeTimeManager();
            UpdateAllSystems();
        }

        private void CacheVolumeComponents()
        {
#if USING_HDRP
            if (hdrpVolume == null || hdrpVolume.profile == null)
            {
                physicalSky = null;
                fog = null;
                exposure = null;
                indirectLighting = null;
                return;
            }

            if (!hdrpVolume.profile.TryGet(out physicalSky))
            {
                physicalSky = hdrpVolume.profile.Add<PhysicallyBasedSky>();
                physicalSky.active = true;
            }

            if (!hdrpVolume.profile.TryGet(out fog))
            {
                fog = hdrpVolume.profile.Add<Fog>();
                fog.active = true;
                
                fog.meanFreePath.overrideState = true;
                fog.baseHeight.overrideState = true;
                fog.enableVolumetricFog.overrideState = true;
                fog.enableVolumetricFog.value = true;
                fog.albedo.overrideState = true;
            }

            if (!hdrpVolume.profile.TryGet(out exposure))
            {
                exposure = hdrpVolume.profile.Add<Exposure>();
                exposure.active = true;
                exposure.mode.overrideState = true;
                exposure.mode.value = ExposureMode.Automatic;
                exposure.compensation.overrideState = true;
            }

            if (!hdrpVolume.profile.TryGet(out indirectLighting))
            {
                indirectLighting = hdrpVolume.profile.Add<IndirectLightingController>();
                indirectLighting.active = true;
            }
#endif
        }

        // ========== 公共方法 ==========
        /// <summary>
        /// 用于编辑器刷新
        /// </summary>
        public void Refresh()
        {
            UpdateAllSystems();
        }

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
        /// 获取归一化时间 (0-1)，支持 TimeRemap
        /// </summary>
        public float GetNormalizedTime()
        {
            float raw = timeOfDay / 24f;
            return raw;
        }

        /// <summary>
        /// 设置预设
        /// </summary>
        public void SetPreset(DawnWeatherPreset preset)
        {
            activePreset = preset;
            if (timeManager != null)
            {
                //timeManager.SetPreset(preset);
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
#if USING_HDRP
            UpdateSkyParameters(normalizedTime);
            UpdateFogParameters(normalizedTime);
            UpdateExposureParameters(normalizedTime);
#endif
            UpdateRainyParamers(normalizedTime);
            
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

#if USING_HDRP
        private void UpdateSkyParameters(float normalizedTime)
        {
            if (activePreset == null)
            {
                return;
            }

            // 更新星空发射强度
            if (physicalSky != null)
            {
                physicalSky.spaceEmissionMultiplier.overrideState = true;
                physicalSky.spaceEmissionMultiplier.value = activePreset.starEmissionCurve.Evaluate(normalizedTime);
            }
        }

        private void UpdateFogParameters(float normalizedTime)
        {
            if (fog == null || activePreset == null)
            {
                return;
            }

            fog.meanFreePath.overrideState = true;
            fog.meanFreePath.value = activePreset.fogDistanceCurve.Evaluate(normalizedTime);

            fog.baseHeight.overrideState = true;
            fog.baseHeight.value = activePreset.fogHeightCurve.Evaluate(normalizedTime);

            fog.enableVolumetricFog.overrideState = true;
            fog.enableVolumetricFog.value = true;
            fog.albedo.overrideState = true;
            fog.albedo.value = activePreset.fogColorGradient.Evaluate(normalizedTime);
        }

        private void UpdateExposureParameters(float normalizedTime)
        {
            if (exposure == null || activePreset == null) return;

            exposure.mode.overrideState = true;
            exposure.mode.value = ExposureMode.Automatic;
            exposure.compensation.overrideState = true;
            exposure.compensation.value = activePreset.exposureCompensationCurve.Evaluate(normalizedTime);
        }
#endif

        // ========== 雨粒子逻辑 ==========
        private void UpdateRainyParamers(float normalizedTime)
        {
            if (activePreset != null && activePreset.rainyEnable)
            {
                DawnGPUParticleSystem particleSystem = FindObjectOfType<DawnGPUParticleSystem>();
                
                if (particleSystem == null)
                {
                    GameObject particleObj = new GameObject("DawnParticleSystem [Auto Create]");
                    particleSystem = particleObj.AddComponent<DawnGPUParticleSystem>();
                }

                DawnGPUParticleSystem.Instance.ParticleShow = true;
                DawnGPUParticleSystem.Instance.baseFallSpeed = activePreset.rainySpeedCurve.Evaluate(normalizedTime);
                DawnGPUParticleSystem.Instance.rainDensity = activePreset.rainDensityCurve.Evaluate(normalizedTime);
                DawnGPUParticleSystem.Instance.rainWindZRotation = activePreset.rainWindZRotationCurve.Evaluate(normalizedTime);
            }
            else if (DawnGPUParticleSystem.Instance != null)
            {
                DawnGPUParticleSystem.Instance.ParticleShow = false; 
            }
            
        }

        private void CheckDayNightTransition()
        {
            isNight = timeOfDay < sunriseTime || timeOfDay >= sunsetTime;
            if (sunLight != null)
            {
                sunLight.gameObject.SetActive(!isNight);
            }
            if (moonLight != null)
            {
                moonLight.gameObject.SetActive(isNight);
            }
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
    }
}