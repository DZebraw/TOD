using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
#if USING_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

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
        // 运行时派生缓存，不再作为第二份序列化事实来源。
        [NonSerialized]
        public List<DawnWeatherController> weatherControllers = new List<DawnWeatherController>();
        // 保留旧容器名称以兼容已有 Scene/Prefab；条目已升级为 Schedule Entry 语义。
        public List<WeatherControllerTimeRange> controllerTimeRanges = new List<WeatherControllerTimeRange>();
        [SerializeField] private DawnWeatherPreset fallbackPreset;
        [SerializeField] private int scheduleSchemaVersion;

        public const int CurrentScheduleSchemaVersion = 1;

        private readonly List<WeatherScheduleWindow> scheduleWindowBuffer =
            new List<WeatherScheduleWindow>();
        private readonly List<DawnWeatherPreset> presetBuffer =
            new List<DawnWeatherPreset>();
        private readonly List<WeatherWeightContribution> weightContributionBuffer =
            new List<WeatherWeightContribution>();
        private readonly List<WeatherSampleContribution> sampleContributionBuffer =
            new List<WeatherSampleContribution>();
        private WeatherBlendResult mixedResult;
        private bool hasMixedResult;
        private double nextFallbackWarningTime;
        private double nextRainOutputWarningTime;

        private const double FallbackWarningIntervalSeconds = 5d;
        private const double RainOutputWarningIntervalSeconds = 5d;

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

        [Header("Precipitation Output")]
        [Tooltip("Scene rain output controlled by the blended precipitation result.")]
        [SerializeField] private DawnGPUParticleSystem rainParticleSystem;

        [Header("Celestial Light Shadows")]
        [Min(0f)]
        [Tooltip("Minimum light intensity required before a celestial light may start casting shadows.")]
        [SerializeField] private float shadowEnableIntensity = 0.05f;
        [Min(0f)]
        [Tooltip("Intensity margin used for shadow disable and dominant-light switching hysteresis.")]
        [SerializeField] private float shadowHysteresis = 0.01f;
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
        private Light shadowOwner;
        [NonSerialized] private DawnGPUParticleSystem resolvedLegacyRainParticleSystem;
        [NonSerialized] private bool rainOutputResolutionAttempted;

        public float SunRaiseTime => sunriseTime;
        public float SunSetTime => sunsetTime;
        public float NormalizedTime => GetNormalizedTime();
        public bool IsNight => isNight;
        public DawnGPUParticleSystem RainParticleSystem
        {
            get => rainParticleSystem;
            set
            {
                rainParticleSystem = value;
                resolvedLegacyRainParticleSystem = value;
                rainOutputResolutionAttempted = value != null;
                nextRainOutputWarningTime = 0d;
            }
        }
        public float ShadowEnableIntensity
        {
            get => shadowEnableIntensity;
            set
            {
                shadowEnableIntensity = Mathf.Max(0f, value);
                shadowHysteresis = Mathf.Clamp(
                    shadowHysteresis,
                    0f,
                    shadowEnableIntensity);
            }
        }
        public float ShadowHysteresis
        {
            get => shadowHysteresis;
            set => shadowHysteresis = Mathf.Clamp(value, 0f, shadowEnableIntensity);
        }
        public DawnWeatherPreset FallbackPreset
        {
            get => fallbackPreset;
            set => fallbackPreset = value;
        }
        public int ScheduleSchemaVersion => scheduleSchemaVersion;
        public bool NeedsScheduleMigration =>
            scheduleSchemaVersion < CurrentScheduleSchemaVersion;
        public bool AutoAdvanceTime
        {
            get => autoAdvanceTime;
            set
            {
                autoAdvanceTime = value;
                SyncTimeManagerSettings();
            }
        }
        public float DayLengthInSeconds
        {
            get => dayLengthInSeconds;
            set
            {
                dayLengthInSeconds = Mathf.Max(1f, value);
                SyncTimeManagerSettings();
            }
        }
        public float TimeScale
        {
            get => timeScale;
            set
            {
                timeScale = value;
                SyncTimeManagerSettings();
            }
        }
        public float TimeOfDay
        {
            get => timeOfDay;
            set => SetTime(value);
        }

        #region 辅助类
        [Serializable]
        public class WeatherControllerTimeRange : WeatherScheduleEntry
        {
            [SerializeField]
            [HideInInspector]
            [FormerlySerializedAs("preset")]
            private DawnWeatherPreset legacyPreset;

            // 源码兼容入口；当前评估始终读取 controller.ActivePreset。
            public DawnWeatherPreset preset
            {
                get => legacyPreset;
                set => legacyPreset = value;
            }

            public float GetSmoothWeightAt(float currentHour)
            {
                return GetRawWeightAt(currentHour);
            }

            internal DawnWeatherPreset LegacyPreset => legacyPreset;
            internal void ClearLegacyPreset() => legacyPreset = null;
        }
        #endregion

        #region 生命周期
        private void Reset()
        {
            scheduleSchemaVersion = CurrentScheduleSchemaVersion;
        }

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
            RebuildControllerCacheFromSchedule();
            //TODEvents.OnSunrise += () => Debug.Log($"Time {TimeOfDay}: 日出");
        }

        private void Start()
        {
#if USING_HDRP
            CacheVolumeComponents();
#endif

            // 调度列表是唯一序列化来源；场景扫描只由显式 Rescan 触发。
            RebuildControllerCacheFromSchedule();
            UpdateWeatherBlendingSystem();
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
#if USING_HDRP
            CacheVolumeComponents();
#endif
            shadowEnableIntensity = Mathf.Max(0f, shadowEnableIntensity);
            shadowHysteresis = Mathf.Clamp(
                shadowHysteresis,
                0f,
                shadowEnableIntensity);
            resolvedLegacyRainParticleSystem = rainParticleSystem;
            rainOutputResolutionAttempted = rainParticleSystem != null;
            nextRainOutputWarningTime = 0d;
            if (!NeedsScheduleMigration)
            {
                SanitizeScheduleEntries();
            }
            SyncTimeManagerSettings();
            RebuildControllerCacheFromSchedule();
            UpdateWeatherBlendingSystem();

            if (Application.isPlaying && autoAdvanceTime)
            {
                AdvanceTime(Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
            UnsubscribeTimeManager();
            if (_instance == this)
            {
                _instance = null;
            }
        }
        #endregion

        #region 核心：天气混合相关方法（编辑器面板配置驱动）
        public void RegisterController(DawnWeatherController controller)
        {
            if (controller == null)
            {
                return;
            }

            weatherControllers ??= new List<DawnWeatherController>();
            if (!weatherControllers.Contains(controller))
            {
                weatherControllers.Add(controller);
            }

            RefreshWeatherBlendingSystem();
        }

        public void UnregisterController(DawnWeatherController controller)
        {
            if (controller == null || weatherControllers == null)
            {
                return;
            }

            weatherControllers.Remove(controller);
            RefreshWeatherBlendingSystem();
        }

        public int RescanControllers(bool addMissingFullDaySchedules = false)
        {
            DawnWeatherController[] found =
                FindObjectsOfType<DawnWeatherController>(true);
            Array.Sort(found, CompareControllersByInstanceId);

            weatherControllers ??= new List<DawnWeatherController>();
            weatherControllers.Clear();
            for (int index = 0; index < found.Length; index++)
            {
                DawnWeatherController controller = found[index];
                if (controller == null)
                {
                    continue;
                }

                weatherControllers.Add(controller);
                if (addMissingFullDaySchedules && !HasScheduleFor(controller))
                {
                    controllerTimeRanges.Add(new WeatherControllerTimeRange
                    {
                        controller = controller,
                        enabled = true,
                        fullDay = true,
                        startHour = 0f,
                        endHour = 24f
                    });
                }
            }

            RefreshWeatherBlendingSystem();
            return found.Length;
        }

        public bool HasScheduleFor(DawnWeatherController controller)
        {
            if (controller == null || controllerTimeRanges == null)
            {
                return false;
            }

            for (int index = 0; index < controllerTimeRanges.Count; index++)
            {
                WeatherControllerTimeRange entry = controllerTimeRanges[index];
                if (entry != null && entry.controller == controller)
                {
                    return true;
                }
            }

            return false;
        }

        public void GetCurrentContributions(List<WeatherContributionInfo> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            output.Clear();
            for (int index = 0; index < weightContributionBuffer.Count; index++)
            {
                WeatherWeightContribution contribution =
                    weightContributionBuffer[index];
                if (contribution.IsFallback)
                {
                    output.Add(new WeatherContributionInfo(
                        WeatherWeightContribution.FallbackSourceIndex,
                        null,
                        fallbackPreset,
                        contribution.RawWeight,
                        contribution.NormalizedWeight,
                        true));
                    continue;
                }

                int scheduleIndex = contribution.SourceIndex;
                WeatherControllerTimeRange entry =
                    scheduleIndex >= 0 &&
                    scheduleIndex < controllerTimeRanges.Count
                        ? controllerTimeRanges[scheduleIndex]
                        : null;
                DawnWeatherController controller = entry?.controller;
                output.Add(new WeatherContributionInfo(
                    scheduleIndex,
                    controller,
                    controller != null ? controller.ActivePreset : null,
                    contribution.RawWeight,
                    contribution.NormalizedWeight,
                    false));
            }
        }

        internal void MigrateLegacyScheduleData()
        {
            if (!NeedsScheduleMigration)
            {
                return;
            }

            controllerTimeRanges ??= new List<WeatherControllerTimeRange>();
            for (int index = 0; index < controllerTimeRanges.Count; index++)
            {
                WeatherControllerTimeRange entry = controllerTimeRanges[index];
                if (entry == null)
                {
                    continue;
                }

                entry.enabled = true;
                entry.fullDay = Mathf.Approximately(entry.startHour, 0f) &&
                                Mathf.Approximately(entry.endHour, 24f);
                entry.blendInHours = 0f;
                entry.blendOutHours = 0f;
                entry.easing = null;
            }

            InferSimpleLegacyOverlaps();
            for (int index = 0; index < controllerTimeRanges.Count; index++)
            {
                WeatherControllerTimeRange entry = controllerTimeRanges[index];
                if (entry == null)
                {
                    continue;
                }

                entry.Sanitize();
                entry.ClearLegacyPreset();
            }

            scheduleSchemaVersion = CurrentScheduleSchemaVersion;
            RebuildControllerCacheFromSchedule();
            RefreshWeatherBlendingSystem();
        }

        internal void SetScheduleSchemaVersionForTests(int version)
        {
            scheduleSchemaVersion = version;
        }

        private static int CompareControllersByInstanceId(
            DawnWeatherController left,
            DawnWeatherController right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;
            return left.GetInstanceID().CompareTo(right.GetInstanceID());
        }

        private void RebuildControllerCacheFromSchedule()
        {
            weatherControllers ??= new List<DawnWeatherController>();
            weatherControllers.Clear();
            if (controllerTimeRanges == null)
            {
                return;
            }

            for (int index = 0; index < controllerTimeRanges.Count; index++)
            {
                DawnWeatherController controller = controllerTimeRanges[index]?.controller;
                if (controller != null && !weatherControllers.Contains(controller))
                {
                    weatherControllers.Add(controller);
                }
            }
        }

        private void SanitizeScheduleEntries()
        {
            if (controllerTimeRanges == null)
            {
                return;
            }

            for (int index = 0; index < controllerTimeRanges.Count; index++)
            {
                controllerTimeRanges[index]?.Sanitize();
            }
        }

        private void InferSimpleLegacyOverlaps()
        {
            for (int firstIndex = 0;
                 firstIndex < controllerTimeRanges.Count;
                 firstIndex++)
            {
                WeatherControllerTimeRange first =
                    controllerTimeRanges[firstIndex];
                if (!IsSimpleDaytimeEntry(first))
                {
                    continue;
                }

                for (int secondIndex = 0;
                     secondIndex < controllerTimeRanges.Count;
                     secondIndex++)
                {
                    if (firstIndex == secondIndex)
                    {
                        continue;
                    }

                    WeatherControllerTimeRange second =
                        controllerTimeRanges[secondIndex];
                    if (!IsSimpleDaytimeEntry(second) ||
                        second.startHour <= first.startHour)
                    {
                        continue;
                    }

                    float overlap = first.endHour - second.startHour;
                    if (overlap <= 0f || first.endHour > second.endHour)
                    {
                        continue;
                    }

                    first.blendOutHours = Mathf.Max(
                        first.blendOutHours,
                        overlap);
                    second.blendInHours = Mathf.Max(
                        second.blendInHours,
                        overlap);
                }
            }
        }

        private static bool IsSimpleDaytimeEntry(WeatherScheduleEntry entry)
        {
            return entry != null &&
                   !entry.fullDay &&
                   entry.startHour < entry.endHour;
        }

        private bool TryEvaluateWeather(out WeatherBlendResult result)
        {
            PrepareEvaluationBuffers();
            WeatherWeightResolutionMode resolutionMode =
                WeatherContributionResolver.Resolve(
                    scheduleWindowBuffer,
                    timeOfDay,
                    weightContributionBuffer);

            sampleContributionBuffer.Clear();
            if (resolutionMode == WeatherWeightResolutionMode.Scheduled)
            {
                float normalizedTime = GetNormalizedTime();
                for (int index = 0; index < weightContributionBuffer.Count; index++)
                {
                    WeatherWeightContribution contribution =
                        weightContributionBuffer[index];
                    int sourceIndex = contribution.SourceIndex;
                    if (sourceIndex < 0 || sourceIndex >= presetBuffer.Count)
                    {
                        continue;
                    }

                    DawnWeatherPreset preset = presetBuffer[sourceIndex];
                    if (!WeatherPresetSampler.TrySample(
                            preset,
                            normalizedTime,
                            out WeatherSample sample))
                    {
                        continue;
                    }

                    sampleContributionBuffer.Add(new WeatherSampleContribution(
                        sourceIndex,
                        sample,
                        contribution.NormalizedWeight));
                }

                if (sampleContributionBuffer.Count > 0 &&
                    WeatherBlender.TryBlend(sampleContributionBuffer, out result))
                {
                    return true;
                }
            }

            return TryBlendFallback(out result);
        }

        private void PrepareEvaluationBuffers()
        {
            scheduleWindowBuffer.Clear();
            presetBuffer.Clear();

            int rangeCount = controllerTimeRanges?.Count ?? 0;
            EnsureCapacity(scheduleWindowBuffer, rangeCount);
            EnsureCapacity(presetBuffer, rangeCount);
            EnsureCapacity(weightContributionBuffer, rangeCount);
            EnsureCapacity(sampleContributionBuffer, rangeCount);

            for (int index = 0; index < rangeCount; index++)
            {
                WeatherControllerTimeRange range = controllerTimeRanges[index];
                DawnWeatherPreset preset = null;
                WeatherScheduleWindow window = default;
                if (range != null &&
                    (NeedsScheduleMigration || range.enabled) &&
                    range.controller != null &&
                    range.controller.isActiveAndEnabled &&
                    range.controller.ActivePreset != null)
                {
                    preset = range.controller.ActivePreset;
                    window = NeedsScheduleMigration
                        ? range.CreateLegacyScheduleWindow()
                        : range.CreateScheduleWindow();
                }

                scheduleWindowBuffer.Add(window);
                presetBuffer.Add(preset);
            }
        }

        private bool TryBlendFallback(out WeatherBlendResult result)
        {
            sampleContributionBuffer.Clear();
            if (WeatherPresetSampler.TrySample(
                    fallbackPreset,
                    GetNormalizedTime(),
                    out WeatherSample fallbackSample))
            {
                weightContributionBuffer.Clear();
                weightContributionBuffer.Add(
                    WeatherWeightContribution.CreateFallback());
                sampleContributionBuffer.Add(new WeatherSampleContribution(
                    WeatherWeightContribution.FallbackSourceIndex,
                    fallbackSample,
                    1f));
                return WeatherBlender.TryBlend(sampleContributionBuffer, out result);
            }

            result = default;
            WarnMissingFallback();
            return false;
        }

        private void WarnMissingFallback()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextFallbackWarningTime)
            {
                return;
            }

            nextFallbackWarningTime = now + FallbackWarningIntervalSeconds;
            Debug.LogWarning(
                "DawnTODSystem has no valid scheduled weather and no valid fallback preset; " +
                "preserving the previous complete weather result.",
                this);
        }

        private static void EnsureCapacity<T>(List<T> buffer, int requiredCapacity)
        {
            if (buffer.Capacity < requiredCapacity)
            {
                buffer.Capacity = requiredCapacity;
            }
        }

        /// <summary>
        /// 应用混合后的结果到场景（光源/Volume）
        /// </summary>
        private void ApplyMixedWeatherResult()
        {
            if (!hasMixedResult)
            {
                return;
            }

            // ========== 应用太阳属性 ==========
            if (sunLight != null)
            {
                sunLight.gameObject.SetActive(true);
                sunLight.transform.rotation = CreateLookRotation(mixedResult.SunDirection);
                sunLight.intensity = mixedResult.SunIntensity;
                sunLight.color = mixedResult.SunColor;
            }

            // ========== 应用月亮属性 ==========
            if (moonLight != null)
            {
                moonLight.gameObject.SetActive(true);
                moonLight.transform.rotation = CreateLookRotation(mixedResult.MoonDirection);
                moonLight.intensity = mixedResult.MoonIntensity;
                moonLight.color = mixedResult.MoonColor;
            }

            UpdateCelestialShadows();
#if USING_HDRP
            // ========== 应用天空属性 ==========
            if (physicalSky != null)
            {
                physicalSky.spaceEmissionMultiplier.overrideState = true;
                physicalSky.spaceEmissionMultiplier.value = mixedResult.StarEmission;
            }

            // ========== 应用雾属性 ==========
            if (fog != null)
            {
                fog.meanFreePath.overrideState = true;
                fog.meanFreePath.value = mixedResult.FogDistance;

                fog.baseHeight.overrideState = true;
                fog.baseHeight.value = mixedResult.FogHeight;

                fog.enableVolumetricFog.overrideState = true;
                fog.enableVolumetricFog.value = true;

                fog.albedo.overrideState = true;
                fog.albedo.value = mixedResult.FogColor;
            }

            // ========== 应用曝光属性 ==========
            if (exposure != null)
            {
                exposure.mode.overrideState = true;
                exposure.mode.value = ExposureMode.Automatic;
                exposure.compensation.overrideState = true;
                exposure.compensation.value = mixedResult.ExposureCompensation;
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
            UpdateWeatherBlendingSystem();
        }

        /// <summary>
        /// 更新天气混合系统
        /// </summary>
        private void UpdateWeatherBlendingSystem()
        {
            if (TryEvaluateWeather(out WeatherBlendResult evaluatedResult))
            {
                mixedResult = evaluatedResult;
                hasMixedResult = true;
            }

            ApplyMixedWeatherResult();
            CheckDayNightTransition();
        }

        private static Quaternion CreateLookRotation(Vector3 direction)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.999f
                ? Vector3.forward
                : Vector3.up;
            return Quaternion.LookRotation(direction, up);
        }
        #endregion

        #region 内部私有方法
        private void InitializeTimeManager()
        {
            UnsubscribeTimeManager();
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

        private void SyncTimeManagerSettings()
        {
            if (timeManager == null)
            {
                return;
            }

            timeManager.autoAdvanceTime = autoAdvanceTime;
            timeManager.dayLengthInSeconds = Mathf.Max(1f, dayLengthInSeconds);
            timeManager.timeScale = timeScale;
            timeManager.SetSunriseSunset(sunriseTime, sunsetTime);
        }

        private void UnsubscribeTimeManager()
        {
            if (timeManager == null)
            {
                return;
            }

            timeManager.OnTimeChanged -= OnTimeManagerTimeChanged;
            timeManager.OnSunrise -= OnTimeManagerSunrise;
            timeManager.OnSunset -= OnTimeManagerSunset;
            timeManager.OnMidnight -= OnTimeManagerMidnight;
        }

        private void ApplyRainMixedResult()
        {
            DawnGPUParticleSystem rainOutput = ResolveRainParticleSystem();
            ApplyRainParameters(rainOutput, mixedResult);
            if (rainOutput == null && mixedResult.HasPrecipitation)
            {
                WarnMissingRainOutput();
            }
        }

        internal static void ApplyRainParameters(
            DawnGPUParticleSystem particleSystem,
            WeatherBlendResult result)
        {
            if (particleSystem == null)
            {
                return;
            }

            if (result.HasPrecipitation)
            {
                particleSystem.SetRainState(
                    true,
                    result.RainSpeed,
                    result.RainDensity,
                    result.RainWindZRotation);
            }
            else
            {
                particleSystem.SetRainState(false, 0f, 0f, 0f);
            }
        }

        internal DawnGPUParticleSystem ResolveRainParticleSystem()
        {
            if (rainParticleSystem != null)
            {
                return rainParticleSystem;
            }

            if (resolvedLegacyRainParticleSystem != null)
            {
                return resolvedLegacyRainParticleSystem;
            }

            if (rainOutputResolutionAttempted)
            {
                return null;
            }

            rainOutputResolutionAttempted = true;
            resolvedLegacyRainParticleSystem =
                GetComponentInChildren<DawnGPUParticleSystem>(true);
            if (resolvedLegacyRainParticleSystem == null)
            {
                resolvedLegacyRainParticleSystem =
                    FindObjectOfType<DawnGPUParticleSystem>(true);
            }

            return resolvedLegacyRainParticleSystem;
        }

        internal void ResetRainOutputResolution()
        {
            resolvedLegacyRainParticleSystem = rainParticleSystem;
            rainOutputResolutionAttempted = rainParticleSystem != null;
            nextRainOutputWarningTime = 0d;
        }

        private void WarnMissingRainOutput()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextRainOutputWarningTime)
            {
                return;
            }

            nextRainOutputWarningTime = now + RainOutputWarningIntervalSeconds;
            Debug.LogWarning(
                "The blended weather requires precipitation, but DawnTODSystem has no Rain Output. " +
                "Assign a DawnGPUParticleSystem in Scene Outputs or create GameObject/MagicDawn/Rain Output.",
                this);
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

            hdrpVolume.profile.TryGet(out physicalSky);
            hdrpVolume.profile.TryGet(out fog);
            hdrpVolume.profile.TryGet(out exposure);
            hdrpVolume.profile.TryGet(out indirectLighting);
        }
#endif

        private void CheckDayNightTransition()
        {
            // Sunrise/sunset remain event and display boundaries only. Preset curves own
            // celestial intensity, so twilight never toggles either light GameObject.
            isNight = timeOfDay < sunriseTime || timeOfDay >= sunsetTime;
        }

        private void UpdateCelestialShadows()
        {
            if (shadowOwner != sunLight && shadowOwner != moonLight)
            {
                shadowOwner = null;
            }

            float sunIntensity = GetEffectiveIntensity(sunLight);
            float moonIntensity = GetEffectiveIntensity(moonLight);
            Light candidate = sunIntensity >= moonIntensity ? sunLight : moonLight;
            float candidateIntensity = Mathf.Max(sunIntensity, moonIntensity);
            float disableIntensity = Mathf.Max(
                0f,
                shadowEnableIntensity - shadowHysteresis);

            if (shadowOwner != null)
            {
                float ownerIntensity = GetEffectiveIntensity(shadowOwner);
                bool ownerIsSun = shadowOwner == sunLight;
                float competitorIntensity = ownerIsSun ? moonIntensity : sunIntensity;
                Light competitor = ownerIsSun ? moonLight : sunLight;

                if (ownerIntensity < disableIntensity)
                {
                    shadowOwner = null;
                }
                else if (competitor != null &&
                         competitorIntensity >= shadowEnableIntensity &&
                         competitorIntensity > ownerIntensity + shadowHysteresis)
                {
                    shadowOwner = competitor;
                }
            }

            if (shadowOwner == null &&
                candidate != null &&
                candidateIntensity >= shadowEnableIntensity)
            {
                shadowOwner = candidate;
            }

            ApplyShadowMode(sunLight, shadowOwner == sunLight);
            ApplyShadowMode(moonLight, shadowOwner == moonLight);
        }

        private static float GetEffectiveIntensity(Light light)
        {
            if (light == null || !light.enabled || !light.gameObject.activeInHierarchy)
            {
                return 0f;
            }

            return Mathf.Max(0f, light.intensity);
        }

        private static void ApplyShadowMode(Light light, bool enabled)
        {
            if (light != null)
            {
                light.shadows = enabled ? LightShadows.Soft : LightShadows.None;
            }
        }

        internal Light GetShadowOwnerForTests()
        {
            return shadowOwner;
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
            hour = WeatherScheduleWeightResolver.NormalizeHour(hour);
            return Evaluate(hour / 24f);
        }

        /// <summary>
        /// 根据当前日夜状态获取对应的主平行光
        /// </summary>
        /// <returns>白天返回太阳光，夜间返回月光，如果都不存在则返回null</returns>
        public Light GetMainDirectionalLight()
        {
            if (sunLight == null)
            {
                return moonLight;
            }

            if (moonLight == null)
            {
                return sunLight;
            }

            return GetEffectiveIntensity(sunLight) >= GetEffectiveIntensity(moonLight)
                ? sunLight
                : moonLight;
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
            timeOfDay = WeatherScheduleWeightResolver.NormalizeHour(hour);
            if (timeManager != null)
            {
                timeManager.SetTime(timeOfDay);
            }

            UpdateWeatherBlendingSystem();
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
