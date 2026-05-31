#if USING_HDRP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DawnTOD
{
    /// <summary>
    /// HDRP 集成模块 - 封装所有 HDRP 特定功能的控制
    /// </summary>
    [ExecuteAlways]
    public class HDRPIntegration : MonoBehaviour
    {
        [Header("Volume Reference")]
        [Tooltip("HDRP Volume")]
        [SerializeField]
        private Volume hdrpVolume;

        [Header("Sky Settings")]
        [Tooltip("是否控制天空参数")]
        [SerializeField]
        private bool controlSky = true;

        [Header("Fog Settings")]
        [Tooltip("是否控制雾效参数")]
        [SerializeField]
        private bool controlFog = true;

        [Header("Exposure Settings")]
        [Tooltip("是否控制曝光参数")]
        [SerializeField]
        private bool controlExposure = false;

        // ========== 缓存的 Volume 组件 ==========

        private PhysicallyBasedSky physicalSky;
        private Fog fog;
        private Exposure exposure;
        private IndirectLightingController indirectLighting;
        private CloudLayer cloudLayer;

        // ========== 属性 ==========

        public Volume HDRPVolume
        {
            get => hdrpVolume;
            set
            {
                hdrpVolume = value;
                CacheVolumeComponents();
            }
        }

        public bool ControlSky
        {
            get => controlSky;
            set => controlSky = value;
        }

        public bool ControlFog
        {
            get => controlFog;
            set => controlFog = value;
        }

        public bool ControlExposure
        {
            get => controlExposure;
            set => controlExposure = value;
        }

        // ========== 生命周期 ==========

        private void Awake()
        {
            CacheVolumeComponents();
        }

        private void OnValidate()
        {
            CacheVolumeComponents();
        }

        // ========== 初始化 ==========

        /// <summary>
        /// 缓存 Volume 组件引用
        /// </summary>
        public void CacheVolumeComponents()
        {
            if (hdrpVolume == null || hdrpVolume.profile == null)
            {
                physicalSky = null;
                fog = null;
                exposure = null;
                indirectLighting = null;
                cloudLayer = null;
                return;
            }

            hdrpVolume.profile.TryGet(out physicalSky);
            hdrpVolume.profile.TryGet(out fog);
            hdrpVolume.profile.TryGet(out exposure);
            hdrpVolume.profile.TryGet(out indirectLighting);
            hdrpVolume.profile.TryGet(out cloudLayer);
        }

        // ========== 更新方法 ==========

        /// <summary>
        /// 更新所有 HDRP 参数
        /// </summary>
        public void UpdateAll(float normalizedTime, DawnWeatherPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            if (controlSky)
            {
                UpdateSkyParameters(normalizedTime, preset);
            }

            if (controlFog)
            {
                UpdateFogParameters(normalizedTime, preset);
            }

            if (controlExposure)
            {
                UpdateExposureParameters(normalizedTime, preset);
            }
        }

        /// <summary>
        /// 更新天空参数
        /// </summary>
        public void UpdateSkyParameters(float normalizedTime, DawnWeatherPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            // 更新星空发射强度
            if (physicalSky != null)
            {
                physicalSky.spaceEmissionMultiplier.value = preset.starEmissionCurve.Evaluate(normalizedTime);
            }
            
        }

        /// <summary>
        /// 更新雾效参数
        /// </summary>
        public void UpdateFogParameters(float normalizedTime, DawnWeatherPreset preset)
        {
            if (fog == null || preset == null)
            {
                return;
            }

            // 更新雾距离 (Mean Free Path)
            fog.meanFreePath.value = preset.fogDistanceCurve.Evaluate(normalizedTime);

            // 更新雾颜色
            fog.albedo.value = preset.fogColorGradient.Evaluate(normalizedTime);

            // 如果有雾密度曲线，也可以用于控制其他参数
            // fog.globalLightProbeDimmer.value = 1f - preset.fogDensityCurve.Evaluate(normalizedTime);
        }

        /// <summary>
        /// 更新曝光参数
        /// </summary>
        public void UpdateExposureParameters(float normalizedTime, DawnWeatherPreset preset)
        {
            if (exposure == null || preset == null)
            {
                return;
            }

            // 可以根据时间调整曝光补偿
            // 例如：夜间增加曝光，白天减少
            // exposure.compensation.value = preset.exposureCompensationCurve?.Evaluate(normalizedTime) ?? 0f;
        }

        // ========== 辅助方法 ==========

        /// <summary>
        /// 设置星空发射强度
        /// </summary>
        public void SetStarEmission(float intensity)
        {
            if (physicalSky != null)
            {
                physicalSky.spaceEmissionMultiplier.value = intensity;
            }
        }

        /// <summary>
        /// 设置雾距离
        /// </summary>
        public void SetFogDistance(float meanFreePath)
        {
            if (fog != null)
            {
                fog.meanFreePath.value = meanFreePath;
            }
        }

        /// <summary>
        /// 设置雾颜色
        /// </summary>
        public void SetFogColor(Color color)
        {
            if (fog != null)
            {
                fog.albedo.value = color;
            }
        }

        /// <summary>
        /// 设置雾启用状态
        /// </summary>
        public void SetFogEnabled(bool enabled)
        {
            if (fog != null)
            {
                fog.enabled.value = enabled;
            }
        }

        /// <summary>
        /// 设置间接光照强度
        /// </summary>
        public void SetIndirectLightingIntensity(float intensity)
        {
            if (indirectLighting != null)
            {
                indirectLighting.indirectDiffuseLightingMultiplier.value = intensity;
            }
        }

        /// <summary>
        /// 设置云层启用状态
        /// </summary>
        public void SetCloudLayerEnabled(bool enabled)
        {
            if (cloudLayer != null)
            {
                cloudLayer.active = enabled;
            }
        }

        // ========== 诊断方法 ==========

        /// <summary>
        /// 检查 Volume 组件是否正确配置
        /// </summary>
        public bool ValidateSetup(out string errorMessage)
        {
            if (hdrpVolume == null)
            {
                errorMessage = "HDRP Volume 未设置";
                return false;
            }

            if (hdrpVolume.profile == null)
            {
                errorMessage = "HDRP Volume Profile 未设置";
                return false;
            }

            if (controlSky && physicalSky == null)
            {
                errorMessage = "Volume Profile 中未找到 PhysicallyBasedSky 组件";
                return false;
            }

            if (controlFog && fog == null)
            {
                errorMessage = "Volume Profile 中未找到 Fog 组件";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
#endif