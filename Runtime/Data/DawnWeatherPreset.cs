using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace DawnTOD
{
    public class DawnWeatherPreset : ScriptableObject
    {
        // ========== 太阳曲线 ==========
        [Tooltip("太阳方位角曲线 (X: 0-1 归一化时间, Y: Azimuth 角度, 0°=东方, 顺时针增加)")]
        public AnimationCurve sunAzimuthCurve = CreateDefaultSunAzimuthCurve();

        [Tooltip("太阳仰角曲线 (X: 0-1 归一化时间, Y: Elevation 角度, 0°=水平, 90°=天顶)")]
        public AnimationCurve sunElevationCurve = CreateDefaultSunElevationCurve();

        [Tooltip("太阳强度曲线 (X: 0-1 归一化时间, Y: 强度 lux)")]
        public AnimationCurve sunIntensityCurve =
            CreateDefaultSunIntensityCurve(WeatherRenderPipelineKind.Unknown);

        [Tooltip("太阳颜色渐变 (X: 0-1 归一化时间)")]
        public Gradient sunColorGradient = CreateDefaultSunColorGradient();

        // ========== 月亮曲线 ==========
        [Tooltip("月亮方位角曲线 (X: 0-1 归一化时间, Y: Azimuth 角度)")]
        public AnimationCurve moonAzimuthCurve = CreateDefaultMoonAzimuthCurve();

        [Tooltip("月亮仰角曲线 (X: 0-1 归一化时间, Y: Elevation 角度)")]
        public AnimationCurve moonElevationCurve = CreateDefaultMoonElevationCurve();

        [Tooltip("月亮强度曲线 (X: 0-1 归一化时间, Y: 强度 lux)")]
        public AnimationCurve moonIntensityCurve =
            CreateDefaultMoonIntensityCurve(WeatherRenderPipelineKind.Unknown);

        [Tooltip("月亮颜色渐变 (X: 0-1 归一化时间)")]
        public Gradient moonColorGradient = CreateDefaultMoonColorGradient();

        // ========== 天空光曲线 ==========
        [Tooltip("星空发射强度曲线 (X: 0-1 归一化时间, Y: 强度)")]
        public AnimationCurve starEmissionCurve = CreateDefaultStarEmissionCurve();

        // ========== UE Exponential Height Fog ==========
        [Tooltip("UE Fog Height Falloff artist value. Density halves every 10 / value meters.")]
        [Range(0f, 2f)] public float fogHeightFalloff = 0.2f;

        [Range(0f, 10f)] public float fogDensity = 0.02f;
        [Tooltip("Enable UE's second exponential fog layer. Color, opacity and distances are shared by both layers.")]
        public bool secondFogEnabled;
        [Range(0f, 10f)] public float secondFogDensity;
        [Range(0f, 2f)] public float secondFogHeightFalloff = 0.2f;
        [Tooltip("UE second fog height offset in meters, relative to DawnTODSystem's world height.")]
        public float secondFogHeightOffset;
        [Tooltip("Additional fog inscattering luminance, as in UE. Black leaves atmospheric illumination intact.")]
        [ColorUsage(false, true)] public Color fogColor = Color.black;
        [Tooltip("Add atmospheric sky ambient and Sun/Moon scattering to the fog.")]
        public bool fogAtmosphereLighting = true;
        [ColorUsage(false, true)] public Color fogSkyAtmosphereAmbientContributionColorScale = Color.white;
        [Tooltip("UE Sky Atmosphere Height Fog Contribution; scales both sky ambient and atmospheric directional scattering.")]
        [Min(0f)] public float fogHeightFogContribution = 1f;
        [ColorUsage(false, true)] public Color fogDirectionalInscatteringColor = Color.black;
        [Range(0.000001f, 1000f)] public float fogDirectionalInscatteringExponent = 4f;
        [Tooltip("UE default 10000 cm = 100 m. Applied after the main fog start distance.")]
        [Min(0f)] public float fogDirectionalInscatteringStartDistance = 100f;
        [Range(0f, 1f)] public float fogMaxOpacity = 1f;
        [Min(0f)] public float fogStartDistance;
        [Tooltip("UE End Distance in meters, measured in the horizontal plane. Zero disables the limit.")]
        [Min(0f)] public float fogEndDistance;
        [Tooltip("UE Fog Cutoff Distance in meters. Receivers beyond this distance have no fog. Zero disables cutoff.")]
        [Min(0f)] public float fogCutoffDistance;

        public DawnVolumetricFogSettings volumetricFog = DawnVolumetricFogSettings.Default;

        // ========= 曝光 ============
        [Tooltip("自动曝光补偿值")]
        public AnimationCurve exposureCompensationCurve = CreateDefaultExposureCompensationCurve();

        // ========== 雨天 ===========
        [Tooltip("雨天控制开关")]
        public bool rainyEnable = false;
        [Tooltip("Continuous precipitation amount (0 = dry, 1 = full rain intensity). The value is ignored when Enable Rainy is off.")]
        public AnimationCurve precipitationAmountCurve = CreateDefaultPrecipitationAmountCurve();
        public AnimationCurve rainySpeedCurve = CreateDefaultRainySpeedCurve();
        public AnimationCurve rainDensityCurve = CreateDefaultRainDensityCurve();
        public AnimationCurve rainWindZRotationCurve = CreateDefaultRainWindZRotationCurve();

        // ========== 时间控制曲线 ==========
        // [Tooltip("时间重映射曲线 (X: 实际流逝时间 0-1, Y: 映射后 TOD 时间 0-1)，必须单调递增")]
        // public AnimationCurve timeRemapCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // ========== 默认曲线工厂方法 ==========

        private static AnimationCurve CreateDefaultSunAzimuthCurve()
        {
            // 太阳从东方(90°)升起，经过南方(180°)，到西方(270°)落下
            return new AnimationCurve(
                new Keyframe(0f, 90f),      // 00:00 东方
                new Keyframe(0.5f, 270f),   // 12:00 西方
                new Keyframe(1f, 450f)      // 24:00 回到东方 (90+360)
            );
        }

        private static AnimationCurve CreateDefaultSunElevationCurve()
        {
            // 太阳仰角：日出日落时为0°，正午最高约60°
            var curve = new AnimationCurve(
                new Keyframe(0f, -60f),     // 00:00 地平线以下
                new Keyframe(0.25f, 0f),    // 06:00 日出
                new Keyframe(0.5f, 60f),    // 12:00 正午最高点
                new Keyframe(0.75f, 0f),    // 18:00 日落
                new Keyframe(1f, -60f)      // 24:00 地平线以下
            );
            // 设置平滑切线
            for (int i = 0; i < curve.length; i++)
            {
                curve.SmoothTangents(i, 0f);
            }
            return curve;
        }

        /// <summary>
        /// Creates an unsaved preset with intensity defaults for the requested
        /// render pipeline. Use this instead of CreateInstance when the pipeline
        /// specific HDRP light units are required.
        /// </summary>
        public static DawnWeatherPreset CreateWithDefaults(
            WeatherRenderPipelineKind pipelineKind)
        {
            DawnWeatherPreset preset =
                CreateInstance<DawnWeatherPreset>();
            preset.sunIntensityCurve =
                CreateDefaultSunIntensityCurve(pipelineKind);
            preset.moonIntensityCurve =
                CreateDefaultMoonIntensityCurve(pipelineKind);
            return preset;
        }

        private static AnimationCurve CreateDefaultSunIntensityCurve(
            WeatherRenderPipelineKind pipelineKind)
        {
            if (pipelineKind == WeatherRenderPipelineKind.HighDefinition)
            {
                return new AnimationCurve(
                    new Keyframe(0f, 0f),       // 00:00
                    new Keyframe(0.25f, 5000f),// 06:00 日出
                    new Keyframe(0.5f, 130000f),// 12:00 正午
                    new Keyframe(0.75f, 5000f),// 18:00 日落
                    new Keyframe(1f, 0f)        // 24:00
                );
            }

            return new AnimationCurve(
                new Keyframe(0f, 0f),       // 00:00
                new Keyframe(0.25f, 0.2f),// 06:00 日出
                new Keyframe(0.5f, 2f),// 12:00 正午
                new Keyframe(0.75f, 0.2f),// 18:00 日落
                new Keyframe(1f, 0f)        // 24:00
            );
        }

        private static Gradient CreateDefaultSunColorGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.1f, 0.1f, 0.2f), 0f),     // 午夜蓝
                    new GradientColorKey(new Color(1f, 0.5f, 0.2f), 0.25f),    // 日出橙
                    new GradientColorKey(new Color(1f, 0.98f, 0.92f), 0.5f),   // 正午白
                    new GradientColorKey(new Color(1f, 0.5f, 0.2f), 0.75f),    // 日落橙
                    new GradientColorKey(new Color(0.1f, 0.1f, 0.2f), 1f)      // 午夜蓝
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
            return gradient;
        }

        private static AnimationCurve CreateDefaultMoonAzimuthCurve()
        {
            // 月亮与太阳相差180°
            return new AnimationCurve(
                new Keyframe(0f, 270f),     // 00:00 西方
                new Keyframe(0.5f, 450f),   // 12:00 东方 (90+360)
                new Keyframe(1f, 630f)      // 24:00 西方 (270+360)
            );
        }

        private static AnimationCurve CreateDefaultMoonElevationCurve()
        {
            // 月亮仰角：与太阳相反
            var curve = new AnimationCurve(
                new Keyframe(0f, 60f),      // 00:00 最高点
                new Keyframe(0.25f, 0f),    // 06:00 月落
                new Keyframe(0.5f, -60f),   // 12:00 地平线以下
                new Keyframe(0.75f, 0f),    // 18:00 月升
                new Keyframe(1f, 60f)       // 24:00 最高点
            );
            for (int i = 0; i < curve.length; i++)
            {
                curve.SmoothTangents(i, 0f);
            }
            return curve;
        }

        private static AnimationCurve CreateDefaultMoonIntensityCurve(
            WeatherRenderPipelineKind pipelineKind)
        {
            if (pipelineKind == WeatherRenderPipelineKind.HighDefinition)
            {
                return new AnimationCurve(
                    new Keyframe(0f, 5000f),    // 00:00 午夜
                    new Keyframe(1f, 5000f)     // 24:00
                );
            }

            return new AnimationCurve(
                new Keyframe(0f, 0.2f),    // 00:00 午夜
                new Keyframe(1f, 0.2f)     // 24:00
            );
        }

        private static Gradient CreateDefaultMoonColorGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.7f, 0.8f, 1f), 0f),
                    new GradientColorKey(new Color(0.7f, 0.8f, 1f), 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
            return gradient;
        }

        private static AnimationCurve CreateDefaultStarEmissionCurve()
        {
            // 星空：夜间可见，白天不可见
            return new AnimationCurve(
                new Keyframe(0f, 1000f),    // 00:00 午夜星空明亮
                new Keyframe(0.2f, 1000f),  // 04:48
                new Keyframe(0.25f, 0f),    // 06:00 日出后消失
                new Keyframe(0.75f, 0f),    // 18:00 日落前
                new Keyframe(0.8f, 1000f),  // 19:12 夜间出现
                new Keyframe(1f, 1000f)     // 24:00
            );
        }

        private static AnimationCurve CreateDefaultExposureCompensationCurve()
        {
            return new AnimationCurve(new Keyframe(0f, -2f),
                    new Keyframe(0.25f, 0f),new Keyframe(0.5f,0f),
                    new Keyframe(0.75f,0f),new Keyframe(1f,-2f));
        }

        private static AnimationCurve CreateDefaultPrecipitationAmountCurve()
        {
            // Keep existing rainy presets visually compatible: enabled rain defaults to 100%.
            return new AnimationCurve(new Keyframe(0f, 1f),
                                      new Keyframe(1f, 1f));
        }

        private static AnimationCurve CreateDefaultRainySpeedCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 40f),
                                                new Keyframe(1f, 40f));
        }

        private static AnimationCurve CreateDefaultRainDensityCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 1f),
                                                new Keyframe(1f, 1f));
        }
        
        private static AnimationCurve CreateDefaultRainWindZRotationCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 15f),
                                                new Keyframe(1f, 15f));
        }

        // ========== 辅助方法 ==========

        /// <summary>
        /// 获取重映射后的归一化时间
        /// </summary>
        /// <param name="rawNormalizedTime">原始归一化时间 (0-1)</param>
        /// <returns>重映射后的归一化时间 (0-1)</returns>
        // public float GetRemappedTime(float rawNormalizedTime)
        // {
        //     if (timeRemapCurve != null && timeRemapCurve.length > 0)
        //     {
        //         return Mathf.Clamp01(timeRemapCurve.Evaluate(rawNormalizedTime));
        //     }
        //     return rawNormalizedTime;
        // }

        /// <summary>
        /// 采样太阳旋转
        /// </summary>
        public Quaternion SampleSunRotation(float normalizedTime)
        {
            float azimuth = sunAzimuthCurve.Evaluate(normalizedTime);
            float elevation = sunElevationCurve.Evaluate(normalizedTime);
            return AzimuthElevationToRotation(azimuth, elevation);
        }

        /// <summary>
        /// 采样月亮旋转
        /// </summary>
        public Quaternion SampleMoonRotation(float normalizedTime)
        {
            float azimuth = moonAzimuthCurve.Evaluate(normalizedTime);
            float elevation = moonElevationCurve.Evaluate(normalizedTime);
            return AzimuthElevationToRotation(azimuth, elevation);
        }

        /// <summary>
        /// 将方位角和仰角转换为 Unity 旋转
        /// </summary>
        /// <param name="azimuth">方位角 (0°=北, 90°=东, 180°=南, 270°=西)</param>
        /// <param name="elevation">仰角 (0°=水平, 90°=天顶, 负值=地平线以下)</param>
        public static Quaternion AzimuthElevationToRotation(float azimuth, float elevation)
        {
            // Unity 坐标系：Y 轴向上，Z 轴向前
            // 方位角从北方(Z+)开始，顺时针增加
            // 仰角从水平面开始，向上为正
            float azRad = azimuth * Mathf.Deg2Rad;
            float elRad = elevation * Mathf.Deg2Rad;

            // 计算光线方向（从光源指向地面）
            float x = -Mathf.Cos(elRad) * Mathf.Sin(azRad);
            float y = -Mathf.Sin(elRad);
            float z = -Mathf.Cos(elRad) * Mathf.Cos(azRad);

            Vector3 direction = new Vector3(x, y, z).normalized;
            
            if (direction.sqrMagnitude < 0.001f)
            {
                return Quaternion.identity;
            }
            
            return Quaternion.LookRotation(direction);
        }
    }
}
