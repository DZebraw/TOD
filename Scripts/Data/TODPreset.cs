using System;
using UnityEngine;

namespace NeuroTOD
{
    /// <summary>
    /// TOD 预设数据资产
    /// 存储所有 TOD 相关的曲线配置，支持跨场景复用
    /// 参照 NeuroTOD (Unreal) 项目设计
    /// </summary>
    [CreateAssetMenu(fileName = "TODPreset", menuName = "NeuroTOD/Preset", order = 100)]
    public class TODPreset : ScriptableObject
    {
        // ========== 太阳曲线 ==========
        [Tooltip("太阳方位角曲线 (X: 0-1 归一化时间, Y: Azimuth 角度, 0°=东方, 顺时针增加)")]
        public AnimationCurve sunAzimuthCurve = CreateDefaultSunAzimuthCurve();

        [Tooltip("太阳仰角曲线 (X: 0-1 归一化时间, Y: Elevation 角度, 0°=水平, 90°=天顶)")]
        public AnimationCurve sunElevationCurve = CreateDefaultSunElevationCurve();

        [Tooltip("太阳强度曲线 (X: 0-1 归一化时间, Y: 强度 lux)")]
        public AnimationCurve sunIntensityCurve = CreateDefaultSunIntensityCurve();

        [Tooltip("太阳颜色渐变 (X: 0-1 归一化时间)")]
        public Gradient sunColorGradient = CreateDefaultSunColorGradient();

        // ========== 月亮曲线 ==========
        [Tooltip("月亮方位角曲线 (X: 0-1 归一化时间, Y: Azimuth 角度)")]
        public AnimationCurve moonAzimuthCurve = CreateDefaultMoonAzimuthCurve();

        [Tooltip("月亮仰角曲线 (X: 0-1 归一化时间, Y: Elevation 角度)")]
        public AnimationCurve moonElevationCurve = CreateDefaultMoonElevationCurve();

        [Tooltip("月亮强度曲线 (X: 0-1 归一化时间, Y: 强度 lux)")]
        public AnimationCurve moonIntensityCurve = CreateDefaultMoonIntensityCurve();

        [Tooltip("月亮颜色渐变 (X: 0-1 归一化时间)")]
        public Gradient moonColorGradient = CreateDefaultMoonColorGradient();

        // ========== 天空光曲线 ==========
        [Tooltip("天空光强度曲线 (X: 0-1 归一化时间, Y: 强度)")]
        public AnimationCurve skyLightIntensityCurve = CreateDefaultSkyLightIntensityCurve();

        [Tooltip("天空光颜色渐变 (X: 0-1 归一化时间)")]
        public Gradient skyLightColorGradient = CreateDefaultSkyLightColorGradient();

        [Tooltip("星空发射强度曲线 (X: 0-1 归一化时间, Y: 强度)")]
        public AnimationCurve starEmissionCurve = CreateDefaultStarEmissionCurve();

        // ========== 雾效曲线 ==========
        [Tooltip("雾密度曲线 (X: 0-1 归一化时间, Y: 密度)")]
        public AnimationCurve fogDensityCurve = CreateDefaultFogDensityCurve();

        [Tooltip("雾距离曲线 (X: 0-1 归一化时间, Y: Mean Free Path)")]
        public AnimationCurve fogDistanceCurve = CreateDefaultFogDistanceCurve();

        [Tooltip("雾颜色渐变 (X: 0-1 归一化时间)")]
        public Gradient fogColorGradient = CreateDefaultFogColorGradient();

        // ========== 时间控制曲线 ==========
        [Tooltip("时间重映射曲线 (X: 实际流逝时间 0-1, Y: 映射后 TOD 时间 0-1)，必须单调递增")]
        public AnimationCurve timeRemapCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

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

        private static AnimationCurve CreateDefaultSunIntensityCurve()
        {
            // 太阳强度：夜间为0，正午最强
            return new AnimationCurve(
                new Keyframe(0f, 0f),       // 00:00
                new Keyframe(0.2f, 0f),     // 04:48
                new Keyframe(0.25f, 20000f),// 06:00 日出
                new Keyframe(0.5f, 130000f),// 12:00 正午
                new Keyframe(0.75f, 20000f),// 18:00 日落
                new Keyframe(0.8f, 0f),     // 19:12
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

        private static AnimationCurve CreateDefaultMoonIntensityCurve()
        {
            // 月亮强度：白天为0，午夜最强
            return new AnimationCurve(
                new Keyframe(0f, 5000f),    // 00:00 午夜
                new Keyframe(0.2f, 5000f),  // 04:48
                new Keyframe(0.25f, 0f),    // 06:00 月落
                new Keyframe(0.75f, 0f),    // 18:00 月升
                new Keyframe(0.8f, 5000f),  // 19:12
                new Keyframe(1f, 5000f)     // 24:00
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

        private static AnimationCurve CreateDefaultSkyLightIntensityCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0.1f),     // 00:00 夜间微弱
                new Keyframe(0.25f, 0.5f),  // 06:00 日出
                new Keyframe(0.5f, 1f),     // 12:00 正午
                new Keyframe(0.75f, 0.5f),  // 18:00 日落
                new Keyframe(1f, 0.1f)      // 24:00 夜间
            );
        }

        private static Gradient CreateDefaultSkyLightColorGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.1f, 0.15f, 0.3f), 0f),    // 午夜蓝
                    new GradientColorKey(new Color(0.8f, 0.6f, 0.5f), 0.25f),  // 日出暖色
                    new GradientColorKey(new Color(0.5f, 0.7f, 1f), 0.5f),     // 正午天蓝
                    new GradientColorKey(new Color(0.8f, 0.5f, 0.4f), 0.75f),  // 日落暖色
                    new GradientColorKey(new Color(0.1f, 0.15f, 0.3f), 1f)     // 午夜蓝
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

        private static AnimationCurve CreateDefaultFogDensityCurve()
        {
            // 雾密度：清晨和傍晚较浓
            return new AnimationCurve(
                new Keyframe(0f, 0.02f),
                new Keyframe(0.25f, 0.05f), // 清晨雾浓
                new Keyframe(0.5f, 0.01f),  // 正午雾淡
                new Keyframe(0.75f, 0.04f), // 傍晚雾浓
                new Keyframe(1f, 0.02f)
            );
        }

        private static AnimationCurve CreateDefaultFogDistanceCurve()
        {
            // Mean Free Path：值越大雾越淡
            return new AnimationCurve(
                new Keyframe(0f, 500f),
                new Keyframe(0.25f, 300f),  // 清晨能见度低
                new Keyframe(0.5f, 1000f),  // 正午能见度高
                new Keyframe(0.75f, 400f),  // 傍晚能见度低
                new Keyframe(1f, 500f)
            );
        }

        private static Gradient CreateDefaultFogColorGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.1f, 0.1f, 0.15f), 0f),    // 午夜深蓝
                    new GradientColorKey(new Color(0.8f, 0.6f, 0.5f), 0.25f),  // 日出暖色
                    new GradientColorKey(new Color(0.7f, 0.8f, 0.9f), 0.5f),   // 正午淡蓝
                    new GradientColorKey(new Color(0.9f, 0.5f, 0.3f), 0.75f),  // 日落橙红
                    new GradientColorKey(new Color(0.1f, 0.1f, 0.15f), 1f)     // 午夜深蓝
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
            return gradient;
        }

        // ========== 辅助方法 ==========

        /// <summary>
        /// 获取重映射后的归一化时间
        /// </summary>
        /// <param name="rawNormalizedTime">原始归一化时间 (0-1)</param>
        /// <returns>重映射后的归一化时间 (0-1)</returns>
        public float GetRemappedTime(float rawNormalizedTime)
        {
            if (timeRemapCurve != null && timeRemapCurve.length > 0)
            {
                return Mathf.Clamp01(timeRemapCurve.Evaluate(rawNormalizedTime));
            }
            return rawNormalizedTime;
        }

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
