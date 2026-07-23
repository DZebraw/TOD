using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DawnTOD
{
#if USING_URP
    [VolumeComponentMenu("Dawn TOD/Volumetric Cloud")]
#else
    [HideInInspector]
#endif
    [Serializable]
    public sealed class DawnVolumetricCloudVolume : VolumeComponent
    {
        [Header("Volumetric Cloud")]
        [Tooltip("Enables the Dawn TOD screen-space volumetric cloud effect.")]
        public BoolParameter enabled = new BoolParameter(false);

        [Header("Bounds")]
        [Tooltip("World-space center of the fixed cloud AABB.")]
        public Vector3Parameter boundsCenter =
            new Vector3Parameter(new Vector3(0f, 0f, 0.002f));

        [Tooltip("World-space size of the fixed cloud AABB.")]
        public Vector3Parameter boundsSize =
            new Vector3Parameter(new Vector3(1500f, 50f, 1500f));

        [Header("Textures")]
        [Tooltip("Low-frequency shape noise. Defaults to the migrated source scene texture.")]
        public Texture3DParameter shapeNoise = new Texture3DParameter(null);

        [Tooltip("High-frequency detail noise. Defaults to the texture referenced by the source scene.")]
        public Texture3DParameter detailNoise = new Texture3DParameter(null);

        [Tooltip("Weather coverage and height-gradient texture.")]
        public Texture2DParameter weatherMap = new Texture2DParameter(null);

        [Tooltip("Mask texture used to warp the shape noise.")]
        public Texture2DParameter maskNoise = new Texture2DParameter(null);

        [Tooltip("Blue-noise texture used to jitter the ray-march start position.")]
        public Texture2DParameter blueNoise = new Texture2DParameter(null);

        [Header("Quality")]
        [Tooltip("Cloud render resolution divisor. Higher values cost less but soften the result.")]
        public ClampedIntParameter downsample = new ClampedIntParameter(3, 1, 8);

        [Tooltip("Maximum view-ray samples per pixel.")]
        public ClampedIntParameter maxRayMarchSteps =
            new ClampedIntParameter(512, 1, 512);

        [Tooltip("Exponent applied before rayStepLength. Kept to preserve the source scene tuning.")]
        public ClampedFloatParameter rayStepExponent =
            new ClampedFloatParameter(3.57f, 0.1f, 4f);

        [Tooltip("Base world-space view-ray step length.")]
        public MinFloatParameter rayStepLength = new MinFloatParameter(0.06f, 0.0001f);

        [Tooltip("Blue-noise ray-march start offset strength.")]
        public MinFloatParameter rayOffsetStrength = new MinFloatParameter(1.7f, 0f);

        [Header("Cloud Shape")]
        [Tooltip("Controls horizontal cloud coverage without changing the density of fully covered regions. Zero is clear sky; one restores the unmasked cloud layer.")]
        public ClampedFloatParameter coverage =
            new ClampedFloatParameter(0.65f, 0f, 1f);

        [Tooltip("World-space tiling of the weather and coverage maps. This remains independent from Bounds Size so enlarging the bounds reveals more clouds instead of stretching them.")]
        public MinFloatParameter weatherMapTiling =
            new MinFloatParameter(1f / 1500f, 0.000001f);

        [Header("Density")]
        public MinFloatParameter shapeTiling = new MinFloatParameter(0.002f, 0.000001f);

        public MinFloatParameter detailTiling = new MinFloatParameter(0.01f, 0.000001f);

        public FloatParameter densityOffset = new FloatParameter(-13f);

        public MinFloatParameter densityMultiplier = new MinFloatParameter(1.39f, 0f);

        public Vector4Parameter shapeNoiseWeights =
            new Vector4Parameter(new Vector4(3.53f, 19.9f, 1.7f, -15f));

        public FloatParameter detailWeights = new FloatParameter(1.5f);

        public MinFloatParameter detailNoiseWeight = new MinFloatParameter(6.4f, 0f);

        [Tooltip("Legacy height-gradient blend retained for exact rollback when Height Profile Blend is zero.")]
        public ClampedFloatParameter heightWeights = new ClampedFloatParameter(0.4f, 0f, 1f);

        [Header("Height Profile")]
        [Tooltip("Blends from the legacy height gradient to the art-directed base, body, and top profile. Zero restores the previous stage.")]
        public ClampedFloatParameter heightProfileBlend =
            new ClampedFloatParameter(1f, 0f, 1f);

        [Tooltip("Normalized vertical distance used to soften the otherwise level cloud base.")]
        public ClampedFloatParameter cloudBaseSoftness =
            new ClampedFloatParameter(0.08f, 0.01f, 0.3f);

        [Tooltip("Minimum normalized height of the full cloud body before local top growth is applied.")]
        public ClampedFloatParameter cloudBodyHeight =
            new ClampedFloatParameter(0.55f, 0.25f, 0.9f);

        [Tooltip("Uses the weather map to grow selected cloud regions above the main body. Zero creates a layer; one allows full towers.")]
        public ClampedFloatParameter verticalGrowth =
            new ClampedFloatParameter(0.65f, 0f, 1f);

        [Tooltip("Normalized vertical distance over which the cloud top fades and breaks up.")]
        public ClampedFloatParameter cloudTopSoftness =
            new ClampedFloatParameter(0.2f, 0.02f, 0.5f);

        [Header("Lighting")]
        public ColorParameter colorA =
            new ColorParameter(Color.white, true, false, true);

        public ColorParameter colorB =
            new ColorParameter(Color.white, true, false, true);

        public ClampedFloatParameter colorOffset1 = new ClampedFloatParameter(0f, 0f, 2f);

        public ClampedFloatParameter colorOffset2 = new ClampedFloatParameter(0.55f, 0f, 2f);

        public ClampedFloatParameter lightAbsorptionTowardSun =
            new ClampedFloatParameter(0.473f, 0f, 1f);

        public ClampedFloatParameter lightAbsorptionThroughCloud =
            new ClampedFloatParameter(1.5f, 0f, 5f);

        [Tooltip("X forward anisotropy, Y backward anisotropy, Z forward blend, W phase intensity.")]
        public Vector4Parameter phaseParameters =
            new Vector4Parameter(new Vector4(0.7f, -0.2f, 0.7f, 1f));

        [Tooltip("Minimum directional-light response away from the main forward-scattering highlight.")]
        public ClampedFloatParameter phaseMinimum =
            new ClampedFloatParameter(0.18f, 0f, 1f);

        [Header("Multiple Scattering")]
        [Tooltip("Extinction retained by the internal-light approximation. Lower values let sunlight reach deeper into thick clouds.")]
        public ClampedFloatParameter multiScatterExtinction =
            new ClampedFloatParameter(0.5f, 0.05f, 1f);

        [Tooltip("Strength of the internal-light approximation. Set to zero to recover the single-scattering result.")]
        public ClampedFloatParameter multiScatterContribution =
            new ClampedFloatParameter(0.35f, 0f, 1f);

        [Tooltip("Directional character retained by internal light. Zero is isotropic; one matches direct sunlight.")]
        public ClampedFloatParameter multiScatterDirectionality =
            new ClampedFloatParameter(0.35f, 0f, 1f);

        [Header("Environment Lighting")]
        [Tooltip("Tint applied to the current scene ambient color for light entering from the sky.")]
        public ColorParameter skyLightTint =
            new ColorParameter(new Color(0.8f, 0.9f, 1f, 1f), true, false, true);

        [Tooltip("Strength of ambient light entering the cloud from above. Set to zero to disable sky fill.")]
        public ClampedFloatParameter skyLightIntensity =
            new ClampedFloatParameter(0.35f, 0f, 2f);

        [Tooltip("Ground albedo tint applied to the current scene ambient color.")]
        public ColorParameter groundLightTint =
            new ColorParameter(new Color(0.65f, 0.5f, 0.35f, 1f), true, false, true);

        [Tooltip("Strength of light reflected from the ground into the cloud base. Set to zero to disable ground bounce.")]
        public ClampedFloatParameter groundLightIntensity =
            new ClampedFloatParameter(0.12f, 0f, 2f);

        [Header("Animation")]
        [Tooltip("XY are shape/detail speeds; ZW are shape/detail warp strengths.")]
        public Vector4Parameter speedWarp =
            new Vector4Parameter(new Vector4(0.05f, 1.8f, -1.22f, 10f));

        public bool IsActive()
        {
            Vector3 size = boundsSize.value;
            return active && enabled.value && size.x > 0.01f && size.y > 0.01f &&
                   size.z > 0.01f && maxRayMarchSteps.value > 0;
        }
    }

    internal static class DawnVolumetricCloudResources
    {
        private const string ResourceRoot = "DawnTOD/VolumetricCloud/";

        private static Texture3D defaultShapeNoise;
        private static Texture3D defaultDetailNoise;
        private static Texture2D defaultWeatherMap;
        private static Texture2D defaultMaskNoise;
        private static Texture2D defaultBlueNoise;

        internal static Texture3D GetShapeNoise(Texture configuredTexture)
        {
            return configuredTexture as Texture3D ??
                   (defaultShapeNoise ??= Resources.Load<Texture3D>(
                       ResourceRoot + "ExampleNoise13D"));
        }

        internal static Texture3D GetDetailNoise(Texture configuredTexture)
        {
            return configuredTexture as Texture3D ??
                   (defaultDetailNoise ??= Resources.Load<Texture3D>(
                       ResourceRoot + "BunnySDF_WithoutNormal"));
        }

        internal static Texture2D GetWeatherMap(Texture configuredTexture)
        {
            return configuredTexture as Texture2D ??
                   (defaultWeatherMap ??= Resources.Load<Texture2D>(
                       ResourceRoot + "Substance_graph_output"));
        }

        internal static Texture2D GetMaskNoise(Texture configuredTexture)
        {
            return configuredTexture as Texture2D ??
                   (defaultMaskNoise ??= Resources.Load<Texture2D>(
                       ResourceRoot + "Sky_NoiseSmooth"));
        }

        internal static Texture2D GetBlueNoise(Texture configuredTexture)
        {
            return configuredTexture as Texture2D ??
                   (defaultBlueNoise ??= Resources.Load<Texture2D>(
                       ResourceRoot + "blueNoise"));
        }
    }
}
