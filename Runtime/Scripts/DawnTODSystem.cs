using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
#if USING_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif
using UnityEngine.SocialPlatforms;

namespace DawnTOD
{
#if USING_URP
    [RequireComponent(typeof(RuntimeSkySetting))]
#endif
    [ExecuteAlways]
    public class DawnTODSystem : MonoBehaviour
    {
        private static DawnTODSystem _instance;
        public static DawnTODSystem Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<DawnTODSystem>();
                }
                return _instance;
            }
        }

        // ========== 天气混合相关 ==========
        // 收集场景中的所有WeatherController
        public List<DawnWeatherController> weatherControllers = new List<DawnWeatherController>();
        // 存储「WeatherController-WeatherPreset-生效时间段」
        public List<WeatherControllerTimeRange> controllerTimeRanges = new List<WeatherControllerTimeRange>();
        // 混合结果缓存
        private MixedWeatherResult mixedResult = new MixedWeatherResult();

        // ========== 时间控制 ==========
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
        public Light sunLight;
        [Tooltip("月亮方向光")]
        [SerializeField]
        public Light moonLight;
        // ========== HDRP Volume ==========
#if USING_HDRP
        [Tooltip("HDRP Volume")]
        [SerializeField]
        public Volume hdrpVolume;
#endif

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
        public float TimeOfDay
        {
            get => timeOfDay;
            set => SetTime(value);
        }

        #region 辅助类
        [Serializable]
        public class WeatherControllerTimeRange
        {
            public DawnWeatherController controller;
            public DawnWeatherPreset preset;
            public float startHour = 0f;
            public float endHour = 24f;

            public bool IsActiveAt(float currentHour)
            {
                currentHour = Mathf.Repeat(currentHour, 24f);
                startHour = Mathf.Clamp(startHour, 0f, 24f);
                endHour = Mathf.Clamp(endHour, 0f, 24f);

                if (Mathf.Approximately(startHour, 0f) && Mathf.Approximately(endHour, 24f))
                {
                    return true;
                }

                if (startHour < endHour)
                {
                    return currentHour >= startHour && currentHour < endHour;
                }
                else
                {
                    return currentHour >= startHour || currentHour < endHour;
                }
            }

            public float GetSmoothWeightAt(float currentHour)
            {
                return IsActiveAt(currentHour) ? 1f : 0f;
            }
        }

        // ========== 辅助类：存储混合后的最终结果 ==========
        private class MixedWeatherResult
        {
            // 太阳相关
            public Vector3 sunDir;
            public float sunIntensity;
            public Color sunColor;
            // 月亮相关
            public Vector3 moonDir;
            public float moonIntensity;
            public Color moonColor;
            // 天空/雾相关
            public float starEmission;
            public float fogDistance;
            public float fogHeight;
            public Color fogColor;
            //曝光相关
            public float exposureCompensation;
            //雨天相关
            public float rainySpeed;
            public float rainDensity;
            public float rainWindZRotation;
            public bool hasRain;

            // 重置混合结果
            public void Reset()
            {
                sunDir = Vector3.zero;
                sunIntensity = 0f;
                sunColor = Color.black;
                moonDir = Vector3.zero;
                moonIntensity = 0f;
                moonColor = Color.black;
                starEmission = 0f;
                fogDistance = 0f;
                fogHeight = 0f;
                fogColor = Color.black;
                exposureCompensation = 0f;
                rainySpeed = 0f;
                rainDensity = 0f;
                rainWindZRotation = 0f;
                hasRain = false;
            }
        }
        #endregion

        #region 生命周期
        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else
            {
                if (_instance != this)
                {
                    DestroyImmediate(gameObject);
                    return;
                }
            }

            InitializeTimeManager();
            //TODEvents.OnSunrise += () => Debug.Log($"Time {TimeOfDay}: 日出");
        }

        private void Start()
        {
#if USING_HDRP
            CacheVolumeComponents();
#endif

            // 1. 自动收集场景中所有的WeatherController
            CollectAllWeatherControllers();

            // 2. 增量同步控制器和时间段
            SyncControllerTimeRanges();

            // 3. 初始化混合结果并更新系统
            mixedResult.Reset();
            UpdateWeatherBlendingSystem();
        }

        private void Update()
        {
            if (Application.isPlaying && autoAdvanceTime)
            {
                AdvanceTime(Time.deltaTime);
                UpdateWeatherBlendingSystem();
            }
        }

        private void OnValidate()
        {
#if USING_HDRP
            CacheVolumeComponents();
#endif
            CollectAllWeatherControllers();
            SyncControllerTimeRanges();
            mixedResult.Reset();
            UpdateWeatherBlendingSystem();

            if (Application.isPlaying && autoAdvanceTime)
            {
                AdvanceTime(Time.deltaTime);
                UpdateWeatherBlendingSystem();
            }
        }
        #endregion

        #region 核心：天气混合相关方法（编辑器面板配置驱动）
        /// <summary>
        /// 自动收集场景中所有启用的WeatherController
        /// </summary>
        private void CollectAllWeatherControllers()
        {
            weatherControllers.Clear();
            DawnWeatherController[] allControllers = FindObjectsOfType<DawnWeatherController>(true); //查找所有激活/未激活的对象
            foreach (var controller in allControllers)
            {
                if (controller != null && controller.ActivePreset != null)
                {
                    weatherControllers.Add(controller);
                    //Debug.Log($"收集到WeatherController：{controller.gameObject.name}，对应的Preset：{controller.ActivePreset.name}");
                }
            }
        }

        /// <summary>
        /// 增量同步WeatherController和controllerTimeRanges
        /// </summary>
        private void SyncControllerTimeRanges()
        {
            if (weatherControllers == null || controllerTimeRanges == null) return;

            HashSet<DawnWeatherController> existingControllers = new HashSet<DawnWeatherController>();
            foreach (var range in controllerTimeRanges)
            {
                if (range.controller != null)
                {
                    existingControllers.Add(range.controller);
                }
            }

            foreach (var controller in weatherControllers)
            {
                if (controller == null || controller.ActivePreset == null) continue;

                if (!existingControllers.Contains(controller))
                {
                    controllerTimeRanges.Add(new WeatherControllerTimeRange
                    {
                        controller = controller,
                        preset = controller.ActivePreset,
                        startHour = 0f,
                        endHour = 24f
                    });
                    existingControllers.Add(controller);
                }
            }

            // 清理无效配置
            List<WeatherControllerTimeRange> invalidRanges = new List<WeatherControllerTimeRange>();
            foreach (var range in controllerTimeRanges)
            {
                if (range.controller == null)
                {
                    invalidRanges.Add(range);
                    continue;
                }

                // 检查Controller是否仍在有效列表中
                bool controllerIsStillValid = weatherControllers.Contains(range.controller);
                if (!controllerIsStillValid)
                {
                    invalidRanges.Add(range);
                }
                // 同步Preset（防止Controller切换Preset后配置不更新）
                else if (range.preset != range.controller.ActivePreset)
                {
                    range.preset = range.controller.ActivePreset;
                }
            }

            // 移除无效配置
            foreach (var invalidRange in invalidRanges)
            {
                controllerTimeRanges.Remove(invalidRange);
            }
        }

        /// <summary>
        /// 查询当前时间下的所有有效预设并计算权重
        /// </summary>
        private List<(DawnWeatherPreset preset, float weight)> GetActivePresetsAtCurrentTime()
        {
            var currentHour = Mathf.Repeat(timeOfDay, 24f);
            var activeRanges = controllerTimeRanges
                .Where(r => r.controller != null && r.preset != null && r.IsActiveAt(currentHour))
                .ToList();

            if (activeRanges.Count == 0)
                return new List<(DawnWeatherPreset, float)>();

            if (activeRanges.Count == 1)
                return new List<(DawnWeatherPreset, float)> { (activeRanges[0].preset, 1f) };

            if (activeRanges.Count == 2)
            {
                var r1 = activeRanges[0];
                var r2 = activeRanges[1];

                if (r1.startHour > r2.startHour)
                {
                    var tmp = r1;
                    r1 = r2;
                    r2 = tmp;
                }

                // 计算重叠区间
                float overlapStart = Mathf.Max(r1.startHour, r2.startHour);
                float overlapEnd = Mathf.Min(r1.endHour, r2.endHour);

                if (overlapStart >= overlapEnd)
                {
                    return new List<(DawnWeatherPreset, float)>
                    {
                        (r1.preset, 0.5f),
                        (r2.preset, 0.5f)
                    };
                }

                if (currentHour >= overlapStart && currentHour <= overlapEnd)
                {
                    float t = Mathf.InverseLerp(overlapStart, overlapEnd, currentHour);
                    return new List<(DawnWeatherPreset, float)>
                    {
                        (r1.preset, 1f - t),
                        (r2.preset, t)
                    };
                }
                else
                {
                    //跨天情况,回退到平均
                    return new List<(DawnWeatherPreset, float)>
                    {
                        (r1.preset, 0.5f),
                        (r2.preset, 0.5f)
                    };
                }
            }

            float equalWeight = 1f / activeRanges.Count;
            return activeRanges.Select(r => (r.preset, equalWeight)).ToList();
        }

        /// <summary>
        /// 核心：混合多个有效预设的属性值
        /// </summary>
        private void BlendActivePresets()
        {
            mixedResult.Reset();

            // 获取当前有效预设（带归一化权重，由面板时间段配置决定）
            List<(DawnWeatherPreset preset, float weight)> activePresets = GetActivePresetsAtCurrentTime();
            if (activePresets.Count == 0) return;

            float normalizedTime = GetNormalizedTime();
            bool anyRainEnabled = false; 

            foreach (var (preset, weight) in activePresets)
            {
                if (preset == null) continue;

                // ========== 太阳属性混合 ==========
                Quaternion sunRot = preset.SampleSunRotation(normalizedTime);
                Vector3 sunForward = sunRot * Vector3.forward;
                float sunInt = preset.sunIntensityCurve.Evaluate(normalizedTime);
                Color sunCol = preset.sunColorGradient.Evaluate(normalizedTime);

                mixedResult.sunDir += sunForward * weight;
                mixedResult.sunIntensity += sunInt * weight;
                mixedResult.sunColor += sunCol * weight;

                // ========== 月亮属性混合 ==========
                Quaternion moonRot = preset.SampleMoonRotation(normalizedTime);
                Vector3 moonForward = moonRot * Vector3.forward;
                float moonInt = preset.moonIntensityCurve.Evaluate(normalizedTime);
                Color moonCol = preset.moonColorGradient.Evaluate(normalizedTime);

                mixedResult.moonDir += moonForward * weight;
                mixedResult.moonIntensity += moonInt * weight;
                mixedResult.moonColor += moonCol * weight;

                // ========== 天空/雾属性混合 ==========
                float starEm = preset.starEmissionCurve.Evaluate(normalizedTime);
                float fogDist = preset.fogDistanceCurve.Evaluate(normalizedTime);
                float fogH = preset.fogHeightCurve.Evaluate(normalizedTime);
                Color fogCol = preset.fogColorGradient.Evaluate(normalizedTime);

                mixedResult.starEmission += starEm * weight;
                mixedResult.fogDistance += fogDist * weight;
                mixedResult.fogHeight += fogH * weight;
                mixedResult.fogColor += fogCol * weight;

                // ========== 曝光补偿混合 ==========
                float exposureExpensation = preset.exposureCompensationCurve.Evaluate(normalizedTime);
                mixedResult.exposureCompensation += exposureExpensation * weight;

                // ========== 雨天参数混合==========
                if (preset.rainyEnable)
                {
                    anyRainEnabled = true;
                    float currentRainSpeed = preset.rainySpeedCurve?.Evaluate(normalizedTime) ?? 1.0f;
                    float currentRainDensity = preset.rainDensityCurve?.Evaluate(normalizedTime) ?? 1.0f;
                    float currentRainWindZRotation = preset.rainWindZRotationCurve?.Evaluate(normalizedTime) ?? 0.0f;
                    
                    mixedResult.rainySpeed += currentRainSpeed * weight;
                    mixedResult.rainDensity += currentRainDensity * weight;
                    mixedResult.rainWindZRotation += currentRainWindZRotation * weight;
                }
            }
            mixedResult.hasRain = anyRainEnabled;
            mixedResult.rainWindZRotation = Mathf.Clamp(mixedResult.rainWindZRotation, -45f, 45f);
        }

        /// <summary>
        /// 应用混合后的结果到场景（光源/Volume）
        /// </summary>
        private void ApplyMixedWeatherResult()
        {
            // ========== 应用太阳属性 ==========
            if (sunLight != null && mixedResult.sunDir.sqrMagnitude > 0.0001f)
            {
                Vector3 dir = mixedResult.sunDir.normalized;
                sunLight.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                sunLight.intensity = mixedResult.sunIntensity;
                sunLight.color = mixedResult.sunColor;
            }

            // ========== 应用月亮属性 ==========
            if (moonLight != null && mixedResult.moonDir.sqrMagnitude > 0.0001f)
            {
                Vector3 dir = mixedResult.moonDir.normalized;
                moonLight.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                moonLight.intensity = mixedResult.moonIntensity;
                moonLight.color = mixedResult.moonColor;
            }
#if USING_HDRP
            // ========== 应用天空属性 ==========
            if (physicalSky != null)
            {
                physicalSky.spaceEmissionMultiplier.overrideState = true;
                physicalSky.spaceEmissionMultiplier.value = mixedResult.starEmission;
            }

            // ========== 应用雾属性 ==========
            if (fog != null)
            {
                fog.meanFreePath.overrideState = true;
                fog.meanFreePath.value = mixedResult.fogDistance;

                fog.baseHeight.overrideState = true;
                fog.baseHeight.value = mixedResult.fogHeight;

                fog.enableVolumetricFog.overrideState = true;
                fog.enableVolumetricFog.value = true;

                fog.albedo.overrideState = true;
                fog.albedo.value = mixedResult.fogColor;
            }

            // ========== 应用曝光属性 ==========
            if (exposure != null)
            {
                exposure.mode.overrideState = true;
                exposure.mode.value = ExposureMode.Automatic;
                exposure.compensation.overrideState = true;
                exposure.compensation.value = mixedResult.exposureCompensation;
            }
#endif
            // ========== 应用雨水混合结果 ==========
            ApplyRainMixedResult();
        }

        /// <summary>
        /// 刷新天气混合系统（供编辑器面板调用，实时同步修改）
        /// </summary>
        public void RefreshWeatherBlendingSystem()
        {
            mixedResult.Reset();
            BlendActivePresets();
            ApplyMixedWeatherResult();
            CheckDayNightTransition();
        }

        /// <summary>
        /// 更新天气混合系统
        /// </summary>
        private void UpdateWeatherBlendingSystem()
        {
            BlendActivePresets();
            ApplyMixedWeatherResult();
            CheckDayNightTransition();
        }
        #endregion

        #region 内部私有方法
        private void InitializeTimeManager()
        {
            timeManager = new TimeManager
            {
                autoAdvanceTime = autoAdvanceTime,
                dayLengthInSeconds = dayLengthInSeconds,
                timeScale = timeScale
            };
            timeManager.SetSunriseSunset(sunriseTime, sunsetTime);
            timeManager.SetTime(timeOfDay);

            // 订阅事件
            timeManager.OnTimeChanged += OnTimeManagerTimeChanged;
            timeManager.OnSunrise += OnTimeManagerSunrise;
            timeManager.OnSunset += OnTimeManagerSunset;
            timeManager.OnMidnight += OnTimeManagerMidnight;
        }
        
        private void ApplyRainMixedResult()
        {
            if (DawnGPUParticleSystem.Instance == null) return;
            
            DawnGPUParticleSystem.Instance.ParticleShow = mixedResult.hasRain;
            
            if (mixedResult.hasRain)
            {
                DawnGPUParticleSystem.Instance.baseFallSpeed = mixedResult.rainySpeed;
                DawnGPUParticleSystem.Instance.rainDensity = mixedResult.rainDensity;
                DawnGPUParticleSystem.Instance.rainWindZRotation = mixedResult.rainWindZRotation;
            }
            else
            {
                DawnGPUParticleSystem.Instance.baseFallSpeed = 0f;
                DawnGPUParticleSystem.Instance.rainDensity = 0f;
                DawnGPUParticleSystem.Instance.rainWindZRotation = 0f;
            }
        }

#if USING_HDRP
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

            if (!hdrpVolume.profile.TryGet(out physicalSky))
            {
                physicalSky = hdrpVolume.profile.Add<PhysicallyBasedSky>();
                physicalSky.active = true;
            }

            if (!hdrpVolume.profile.TryGet(out fog))
            {
                fog = hdrpVolume.profile.Add<Fog>();
                fog.active = true;
            }

            if (!hdrpVolume.profile.TryGet(out exposure))
            {
                exposure = hdrpVolume.profile.Add<Exposure>();
                exposure.active = true;
            }

            if (!hdrpVolume.profile.TryGet(out indirectLighting))
            {
                indirectLighting = hdrpVolume.profile.Add<IndirectLightingController>();
                indirectLighting.active = true;
            }
        }
#endif

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
        #endregion

        #region 公开方法
        /// <summary>
        /// 根据归一化时间更新所有场景组件
        /// </summary>
        /// <param name="normalizedTime">归一化时间 (0-1)，0=0点，1=24点</param>
        /// <returns>是否成功应用</returns>
        public bool Evaluate(float normalizedTime)
        {
            try
            {
                normalizedTime = Mathf.Clamp01(normalizedTime);
                
                // 设置时间并更新系统
                float newTimeOfDay = normalizedTime * 24f;
                SetTime(newTimeOfDay);
                UpdateWeatherBlendingSystem();
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Evaluate failed: {e.Message}\n{e.StackTrace}", this);
                return false;
            }
        }

        /// <summary>
        /// 通过24小时制时间直接更新
        /// </summary>
        /// <param name="hour">24小时制时间 (0-24)</param>
        /// <returns>是否成功应用</returns>
        public bool EvaluateByHour(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            return Evaluate(hour / 24f);
        }

        /// <summary>
        /// 根据当前日夜状态获取对应的主平行光
        /// </summary>
        /// <returns>白天返回太阳光，夜间返回月光，如果都不存在则返回null</returns>
        public Light GetMainDirectionalLight()
        {
            if (isNight)
            {
                return moonLight != null ? moonLight : sunLight;
            }
            else
            {
                return sunLight != null ? sunLight : moonLight;
            }
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
        /// 设置当前时间 (0-24)
        /// </summary>
        public void SetTime(float hour)
        {
            timeOfDay = Mathf.Repeat(hour, 24f);
            if (timeManager != null)
            {
                timeManager.SetTime(timeOfDay);
            }
        }

        /// <summary>
        /// 获取归一化时间 (0-1)
        /// </summary>
        public float GetNormalizedTime()
        {
            float raw = timeOfDay / 24f;
            return raw;
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
        #endregion

        #region 事件相关
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
        #endregion
    }
}