using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DawnTOD
{
#if USING_URP
    [VolumeComponentMenu("Dawn TOD/Directional Volumetric Light")]
#else
    [HideInInspector]
#endif
    [Serializable]
    public sealed class DawnDirectionalVolumetricLightVolume : VolumeComponent
    {
        [Header("Directional Volumetric Light")]
        [Tooltip("Enables volumetric in-scattering from the URP main directional light.")]
        public BoolParameter enabled = new BoolParameter(false);

        [Tooltip("Overall brightness of the directional-light scattering.")]
        public MinFloatParameter intensity = new MinFloatParameter(1f, 0f);

        [Tooltip("HDR tint applied to the main directional light while it scatters through the medium.")]
        public ColorParameter scatteringTint =
            new ColorParameter(Color.white, true, false, true);

        [Tooltip("Average distance, in meters, that light travels before scattering. Lower values create denser shafts.")]
        public MinFloatParameter meanFreePath =
            new MinFloatParameter(200f, 0.01f);

        [Tooltip("Scattering directionality. Positive values concentrate light around the sun direction; zero is isotropic.")]
        public ClampedFloatParameter anisotropy =
            new ClampedFloatParameter(0.65f, -0.9f, 0.9f);

        [Tooltip("How strongly the main directional-light shadow map shapes the volumetric light shafts.")]
        public ClampedFloatParameter shadowStrength =
            new ClampedFloatParameter(1f, 0f, 1f);

        [Header("Distance")]
        [Tooltip("Maximum world-space distance, in meters, integrated along each camera ray. Keep this near the main-light shadow distance for distinct shafts.")]
        public MinFloatParameter maximumDistance =
            new MinFloatParameter(150f, 0.01f);

        [Tooltip("Evaluates volumetric light against the sky background as well as scene geometry.")]
        public BoolParameter affectSky = new BoolParameter(true);

        [Header("Cloud Tyndall Shafts")]
        [Tooltip("Uses the Dawn volumetric-cloud transmittance mask to create screen-space crepuscular rays aimed at the main directional light.")]
        public BoolParameter enableCloudShafts = new BoolParameter(true);

        [Tooltip("Brightness of the striped shafts produced by gaps in the volumetric clouds.")]
        public MinFloatParameter cloudShaftIntensity =
            new MinFloatParameter(3f, 0f);

        [Tooltip("Where radial cloud-shaft attenuation begins between the pixel and the sun. Larger values retain long shafts; sampling always reaches the sun to avoid a hard terrain cutoff.")]
        public ClampedFloatParameter cloudShaftLength =
            new ClampedFloatParameter(1f, 0.05f, 1f);

        [Tooltip("Brightness retained by each radial sample. Values near one create longer, more continuous shafts.")]
        public ClampedFloatParameter cloudShaftDecay =
            new ClampedFloatParameter(0.96f, 0.8f, 1f);

        [Tooltip("Radial samples used for cloud shafts. Higher values make the stripes smoother at a proportional GPU cost.")]
        public ClampedIntParameter cloudShaftSampleCount =
            new ClampedIntParameter(32, 4, 64);

        [Header("Quality")]
        [Tooltip("Shadow-map samples per pixel. Higher values reduce stepping at a proportional GPU cost.")]
        public ClampedIntParameter stepCount =
            new ClampedIntParameter(48, 8, 128);

        [Tooltip("Offsets samples with stable screen-space noise to hide ray-march banding.")]
        public ClampedFloatParameter jitter =
            new ClampedFloatParameter(1f, 0f, 1f);

        public bool IsActive()
        {
            return active && enabled.value && intensity.value > 0f &&
                   meanFreePath.value > 0f && maximumDistance.value > 0f &&
                   stepCount.value > 0;
        }
    }
}
