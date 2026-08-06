#if DAWNTOD_URP_AVAILABLE
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DawnTOD
{
    public sealed class DawnVolumetricCloudRendererFeature : ScriptableRendererFeature
    {
        private const string CloudShaderName = "Hidden/DawnTOD/VolumetricCloud";

        [SerializeField, HideInInspector] private Shader cloudShader;

        private Material cloudMaterial;
        private DawnVolumetricCloudRenderPass cloudPass;

        public bool IsReady => cloudMaterial != null;

        public override void Create()
        {
            CoreUtils.Destroy(cloudMaterial);
            cloudShader ??= Shader.Find(CloudShaderName);
            cloudMaterial = cloudShader != null
                ? CoreUtils.CreateEngineMaterial(cloudShader)
                : null;
            cloudPass = new DawnVolumetricCloudRenderPass(name)
            {
                // Clouds composite first so directional volumetric light and fog
                // can affect the already-composited cloud result in that order.
                renderPassEvent = (RenderPassEvent)(
                    (int)RenderPassEvent.BeforeRenderingPostProcessing - 2)
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            ref CameraData cameraData = ref renderingData.cameraData;
            if (cloudMaterial == null ||
                cameraData.cameraType == CameraType.Preview ||
                cameraData.cameraType == CameraType.Reflection ||
                cameraData.renderType == CameraRenderType.Overlay)
            {
                return;
            }

            VolumeStack stack = VolumeManager.instance.stack;
            DawnVolumetricCloudVolume cloud =
                stack?.GetComponent<DawnVolumetricCloudVolume>();
            if (cloud == null || !cloud.IsActive())
            {
                return;
            }

            Vector3 directionToLight = Vector3.up;
            Color mainLightColor = Color.black;
            int mainLightIndex = renderingData.lightData.mainLightIndex;
            if (mainLightIndex >= 0 &&
                mainLightIndex < renderingData.lightData.visibleLights.Length &&
                renderingData.lightData.visibleLights[mainLightIndex].lightType ==
                LightType.Directional)
            {
                VisibleLight mainLight =
                    renderingData.lightData.visibleLights[mainLightIndex];
                directionToLight = -mainLight.localToWorldMatrix.GetColumn(2);
                mainLightColor = mainLight.finalColor;
            }

            CloudSettings settings = CloudSettings.FromVolume(
                cloud,
                mainLightColor);
            if (!settings.HasRequiredTextures)
            {
                return;
            }

            cloudPass.Setup(cloudMaterial, settings, directionToLight.normalized);
            renderer.EnqueuePass(cloudPass);
        }

        protected override void Dispose(bool disposing)
        {
            cloudPass?.Dispose();
            cloudPass = null;
            CoreUtils.Destroy(cloudMaterial);
            cloudMaterial = null;
        }

        private readonly struct CloudSettings
        {
            private const int AmbientUpIndex = 0;
            private const int AmbientRightIndex = 1;
            private const int AmbientLeftIndex = 2;
            private const int AmbientForwardIndex = 3;
            private const int AmbientBackIndex = 4;
            private const int AmbientDownIndex = 5;

            private static readonly Vector3[] AmbientProbeDirections =
            {
                Vector3.up,
                Vector3.right,
                Vector3.left,
                Vector3.forward,
                Vector3.back,
                Vector3.down
            };

            private static readonly Color[] AmbientProbeResults = new Color[6];

            public readonly Vector3 BoundsMinimum;
            public readonly Vector3 BoundsMaximum;
            public readonly Texture3D ShapeNoise;
            public readonly Texture3D DetailNoise;
            public readonly Texture2D WeatherMap;
            public readonly Texture2D MaskNoise;
            public readonly Texture2D BlueNoise;
            public readonly int Downsample;
            public readonly int MaxRayMarchSteps;
            public readonly float RayStepExponent;
            public readonly float RayStepLength;
            public readonly float RayOffsetStrength;
            public readonly float TemporalAccumulation;
            public readonly float Coverage;
            public readonly float WeatherMapTiling;
            public readonly float ShapeTiling;
            public readonly float DetailTiling;
            public readonly float DensityOffset;
            public readonly float DensityMultiplier;
            public readonly Vector4 ShapeNoiseWeights;
            public readonly float DetailWeights;
            public readonly float DetailNoiseWeight;
            public readonly float HeightWeights;
            public readonly float HeightProfileBlend;
            public readonly Vector4 HeightProfileParameters;
            public readonly Color ColorA;
            public readonly Color ColorB;
            public readonly float ColorOffset1;
            public readonly float ColorOffset2;
            public readonly float ExtinctionScale;
            public readonly float LightAbsorptionTowardSun;
            public readonly float SelfShadowStrength;
            public readonly float LightAbsorptionThroughCloud;
            public readonly Vector4 PhaseParameters;
            public readonly float PowderEffectIntensity;
            public readonly Vector4 MultiScatterParameters;
            public readonly Vector4 DiffuseFieldParameters;
            public readonly Vector4 DiffuseFieldTransportParameters;
            public readonly float AmbientOcclusionStrength;
            public readonly Color AmbientSkyColor;
            public readonly Color AmbientEquatorColor;
            public readonly Color AmbientGroundColor;
            public readonly Vector4 SpeedWarp;

            public bool HasRequiredTextures => ShapeNoise != null && DetailNoise != null &&
                                               WeatherMap != null && MaskNoise != null &&
                                               BlueNoise != null;

            private CloudSettings(
                Vector3 boundsMinimum,
                Vector3 boundsMaximum,
                Texture3D shapeNoise,
                Texture3D detailNoise,
                Texture2D weatherMap,
                Texture2D maskNoise,
                Texture2D blueNoise,
                int downsample,
                int maxRayMarchSteps,
                float rayStepExponent,
                float rayStepLength,
                float rayOffsetStrength,
                float temporalAccumulation,
                float coverage,
                float weatherMapTiling,
                float shapeTiling,
                float detailTiling,
                float densityOffset,
                float densityMultiplier,
                Vector4 shapeNoiseWeights,
                float detailWeights,
                float detailNoiseWeight,
                float heightWeights,
                float heightProfileBlend,
                Vector4 heightProfileParameters,
                Color colorA,
                Color colorB,
                float colorOffset1,
                float colorOffset2,
                float extinctionScale,
                float lightAbsorptionTowardSun,
                float selfShadowStrength,
                float lightAbsorptionThroughCloud,
                Vector4 phaseParameters,
                float powderEffectIntensity,
                Vector4 multiScatterParameters,
                Vector4 diffuseFieldParameters,
                Vector4 diffuseFieldTransportParameters,
                float ambientOcclusionStrength,
                Color ambientSkyColor,
                Color ambientEquatorColor,
                Color ambientGroundColor,
                Vector4 speedWarp)
            {
                BoundsMinimum = boundsMinimum;
                BoundsMaximum = boundsMaximum;
                ShapeNoise = shapeNoise;
                DetailNoise = detailNoise;
                WeatherMap = weatherMap;
                MaskNoise = maskNoise;
                BlueNoise = blueNoise;
                Downsample = downsample;
                MaxRayMarchSteps = maxRayMarchSteps;
                RayStepExponent = rayStepExponent;
                RayStepLength = rayStepLength;
                RayOffsetStrength = rayOffsetStrength;
                TemporalAccumulation = Mathf.Clamp(
                    temporalAccumulation,
                    0f,
                    0.97f);
                Coverage = Mathf.Clamp01(coverage);
                WeatherMapTiling = weatherMapTiling;
                ShapeTiling = shapeTiling;
                DetailTiling = detailTiling;
                DensityOffset = densityOffset;
                DensityMultiplier = densityMultiplier;
                ShapeNoiseWeights = shapeNoiseWeights;
                DetailWeights = detailWeights;
                DetailNoiseWeight = detailNoiseWeight;
                HeightWeights = heightWeights;
                HeightProfileBlend = Mathf.Clamp01(heightProfileBlend);
                HeightProfileParameters = SanitizeHeightProfileParameters(
                    heightProfileParameters);
                ColorA = colorA;
                ColorB = colorB;
                ColorOffset1 = colorOffset1;
                ColorOffset2 = colorOffset2;
                ExtinctionScale = Mathf.Clamp(extinctionScale, 0.0001f, 0.25f);
                LightAbsorptionTowardSun = Mathf.Clamp(
                    lightAbsorptionTowardSun,
                    0f,
                    2f);
                SelfShadowStrength = Mathf.Clamp01(selfShadowStrength);
                LightAbsorptionThroughCloud = Mathf.Max(
                    0.05f,
                    lightAbsorptionThroughCloud);
                PhaseParameters = SanitizePhaseParameters(phaseParameters);
                PowderEffectIntensity = Mathf.Clamp01(powderEffectIntensity);
                MultiScatterParameters = SanitizeMultiScatterParameters(
                    multiScatterParameters);
                DiffuseFieldParameters = SanitizeDiffuseFieldParameters(
                    diffuseFieldParameters);
                DiffuseFieldTransportParameters =
                    SanitizeDiffuseFieldTransportParameters(
                        diffuseFieldTransportParameters);
                AmbientOcclusionStrength = Mathf.Clamp01(
                    ambientOcclusionStrength);
                AmbientSkyColor = ambientSkyColor;
                AmbientEquatorColor = ambientEquatorColor;
                AmbientGroundColor = ambientGroundColor;
                SpeedWarp = speedWarp;
            }

            private static Vector4 SanitizePhaseParameters(Vector4 parameters)
            {
                parameters.x = Mathf.Clamp(parameters.x, 0f, 0.9f);
                parameters.y = Mathf.Clamp(parameters.y, -0.75f, 0f);
                parameters.z = Mathf.Clamp01(parameters.z);
                parameters.w = Mathf.Clamp(parameters.w, 0f, 4f);
                return parameters;
            }

            private static Vector4 SanitizeMultiScatterParameters(Vector4 parameters)
            {
                parameters.x = Mathf.Clamp(parameters.x, 0.05f, 1f);
                parameters.y = Mathf.Clamp01(parameters.y);
                parameters.z = Mathf.Clamp01(parameters.z);
                parameters.w = 0f;
                return parameters;
            }

            private static Vector4 SanitizeDiffuseFieldParameters(Vector4 parameters)
            {
                parameters.x = Mathf.Clamp(parameters.x, 0f, 4f);
                parameters.y = Mathf.Clamp(parameters.y, 0f, 4f);
                parameters.z = Mathf.Clamp(parameters.z, -0.3f, 0.5f);
                parameters.w = Mathf.Clamp01(parameters.w);
                return parameters;
            }

            private static Vector4 SanitizeDiffuseFieldTransportParameters(
                Vector4 parameters)
            {
                parameters.x = Mathf.Clamp(parameters.x, 0f, 4f);
                parameters.y = Mathf.Clamp(parameters.y, 0f, 4f);
                parameters.z = 0f;
                parameters.w = 0f;
                return parameters;
            }

            private static Vector4 SanitizeHeightProfileParameters(Vector4 parameters)
            {
                parameters.x = Mathf.Clamp(parameters.x, 0.01f, 0.3f);
                parameters.y = Mathf.Clamp(parameters.y, 0.25f, 0.9f);
                parameters.z = Mathf.Clamp01(parameters.z);
                parameters.w = Mathf.Clamp(parameters.w, 0.02f, 0.5f);
                return parameters;
            }

            public static CloudSettings FromVolume(
                DawnVolumetricCloudVolume cloud,
                Color mainLightColor)
            {
                Vector3 size = cloud.boundsSize.value;
                size.x = Mathf.Max(size.x, 0.01f);
                size.y = Mathf.Max(size.y, 0.01f);
                size.z = Mathf.Max(size.z, 0.01f);
                Vector3 halfSize = size * 0.5f;
                Vector3 center = cloud.boundsCenter.value;
                EvaluateAmbientProbeBands(
                    mainLightColor,
                    out Color sceneAmbientSky,
                    out Color sceneAmbientEquator,
                    out Color sceneAmbientGround);

                Color ambientSkyColor = MultiplyRgb(
                    sceneAmbientSky,
                    cloud.skyLightTint.value,
                    cloud.skyLightIntensity.value);
                Color ambientEquatorColor = MultiplyRgb(
                    sceneAmbientEquator,
                    Color.Lerp(
                        cloud.groundLightTint.value,
                        cloud.skyLightTint.value,
                        0.5f),
                    Mathf.Lerp(
                        cloud.groundLightIntensity.value,
                        cloud.skyLightIntensity.value,
                        0.5f));
                Color ambientGroundColor = MultiplyRgb(
                    sceneAmbientGround,
                    cloud.groundLightTint.value,
                    cloud.groundLightIntensity.value);
                return new CloudSettings(
                    center - halfSize,
                    center + halfSize,
                    DawnVolumetricCloudResources.GetShapeNoise(cloud.shapeNoise.value),
                    DawnVolumetricCloudResources.GetDetailNoise(cloud.detailNoise.value),
                    DawnVolumetricCloudResources.GetWeatherMap(cloud.weatherMap.value),
                    DawnVolumetricCloudResources.GetMaskNoise(cloud.maskNoise.value),
                    DawnVolumetricCloudResources.GetBlueNoise(cloud.blueNoise.value),
                    Mathf.Max(1, cloud.downsample.value),
                    Mathf.Clamp(cloud.maxRayMarchSteps.value, 1, 512),
                    cloud.rayStepExponent.value,
                    Mathf.Max(0.0001f, cloud.rayStepLength.value),
                    Mathf.Max(0f, cloud.rayOffsetStrength.value),
                    cloud.temporalAccumulation.value,
                    cloud.coverage.value,
                    Mathf.Max(0.000001f, cloud.weatherMapTiling.value),
                    Mathf.Max(0.000001f, cloud.shapeTiling.value),
                    Mathf.Max(0.000001f, cloud.detailTiling.value),
                    cloud.densityOffset.value,
                    Mathf.Max(0f, cloud.densityMultiplier.value),
                    cloud.shapeNoiseWeights.value,
                    cloud.detailWeights.value,
                    Mathf.Max(0f, cloud.detailNoiseWeight.value),
                    Mathf.Clamp01(cloud.heightWeights.value),
                    cloud.heightProfileBlend.value,
                    new Vector4(
                        cloud.cloudBaseSoftness.value,
                        cloud.cloudBodyHeight.value,
                        cloud.verticalGrowth.value,
                        cloud.cloudTopSoftness.value),
                    cloud.colorA.value,
                    cloud.colorB.value,
                    cloud.colorOffset1.value,
                    cloud.colorOffset2.value,
                    cloud.extinctionScale.value,
                    cloud.lightAbsorptionTowardSun.value,
                    cloud.selfShadowStrength.value,
                    cloud.lightAbsorptionThroughCloud.value,
                    cloud.phaseParameters.value,
                    cloud.powderEffectIntensity.value,
                    new Vector4(
                        cloud.multiScatterExtinction.value,
                        cloud.multiScatterContribution.value,
                        cloud.multiScatterDirectionality.value,
                        0f),
                    new Vector4(
                        cloud.diffuseFieldIntensity.value,
                        cloud.diffuseFieldDepthPower.value,
                        cloud.diffuseFieldDepthBias.value,
                        cloud.diffuseFieldBoundaryInfluence.value),
                    new Vector4(
                        cloud.diffuseFieldBuildRate.value,
                        cloud.diffuseFieldCompression.value,
                        0f,
                        0f),
                    cloud.ambientOcclusionStrength.value,
                    ambientSkyColor,
                    ambientEquatorColor,
                    ambientGroundColor,
                    cloud.speedWarp.value);
            }

            private static void EvaluateAmbientProbeBands(
                Color mainLightColor,
                out Color sky,
                out Color equator,
                out Color ground)
            {
                RenderSettings.ambientProbe.Evaluate(
                    AmbientProbeDirections,
                    AmbientProbeResults);

                sky = SanitizeAmbientProbeColor(
                    AmbientProbeResults[AmbientUpIndex]);
                ground = SanitizeAmbientProbeColor(
                    AmbientProbeResults[AmbientDownIndex]);
                equator = AverageAmbientProbeColors(
                    AmbientProbeResults[AmbientRightIndex],
                    AmbientProbeResults[AmbientLeftIndex],
                    AmbientProbeResults[AmbientForwardIndex],
                    AmbientProbeResults[AmbientBackIndex]);

                Color ambientFallback = CreateAmbientFallback(mainLightColor);
                sky = MaxRgb(sky, ambientFallback);
                equator = MaxRgb(equator, ScaleRgb(ambientFallback, 0.65f));
                ground = MaxRgb(ground, ScaleRgb(ambientFallback, 0.25f));
            }

            private static Color CreateAmbientFallback(Color mainLightColor)
            {
                Color ambientColor = SanitizeAmbientProbeColor(
                    RenderSettings.ambientLight);
                if (MaxRgbComponent(ambientColor) > 0.0001f)
                {
                    return ambientColor;
                }

                Color directionalColor = SanitizeAmbientProbeColor(
                    mainLightColor);
                float directionalPeak = MaxRgbComponent(directionalColor);
                if (directionalPeak <= 0.0001f)
                {
                    return Color.black;
                }

                float fallbackScale = Mathf.Min(0.08f, 0.12f / directionalPeak);
                return ScaleRgb(directionalColor, fallbackScale);
            }

            private static Color AverageAmbientProbeColors(
                Color first,
                Color second,
                Color third,
                Color fourth)
            {
                return new Color(
                    SanitizeAmbientProbeChannel(
                        (first.r + second.r + third.r + fourth.r) * 0.25f),
                    SanitizeAmbientProbeChannel(
                        (first.g + second.g + third.g + fourth.g) * 0.25f),
                    SanitizeAmbientProbeChannel(
                        (first.b + second.b + third.b + fourth.b) * 0.25f),
                    1f);
            }

            private static Color SanitizeAmbientProbeColor(Color color)
            {
                return new Color(
                    SanitizeAmbientProbeChannel(color.r),
                    SanitizeAmbientProbeChannel(color.g),
                    SanitizeAmbientProbeChannel(color.b),
                    1f);
            }

            private static float SanitizeAmbientProbeChannel(float value)
            {
                return float.IsNaN(value) || float.IsInfinity(value)
                    ? 0f
                    : Mathf.Max(0f, value);
            }

            private static float MaxRgbComponent(Color color)
            {
                return Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            }

            private static Color MaxRgb(Color first, Color second)
            {
                return new Color(
                    Mathf.Max(first.r, second.r),
                    Mathf.Max(first.g, second.g),
                    Mathf.Max(first.b, second.b),
                    1f);
            }

            private static Color ScaleRgb(Color color, float scale)
            {
                float safeScale = Mathf.Max(0f, scale);
                return new Color(
                    color.r * safeScale,
                    color.g * safeScale,
                    color.b * safeScale,
                    1f);
            }

            private static Color MultiplyRgb(Color source, Color tint, float intensity)
            {
                float scale = Mathf.Max(0f, intensity);
                return new Color(
                    Mathf.Max(0f, source.r * tint.r * scale),
                    Mathf.Max(0f, source.g * tint.g * scale),
                    Mathf.Max(0f, source.b * tint.b * scale),
                    1f);
            }
        }

        private sealed class DawnVolumetricCloudRenderPass : ScriptableRenderPass
        {
            private const int CloudShadowResolution = 256;

            private static readonly int ShapeNoiseId =
                Shader.PropertyToID("_DawnCloudShapeNoise");
            private static readonly int DetailNoiseId =
                Shader.PropertyToID("_DawnCloudDetailNoise");
            private static readonly int WeatherMapId =
                Shader.PropertyToID("_DawnCloudWeatherMap");
            private static readonly int MaskNoiseId =
                Shader.PropertyToID("_DawnCloudMaskNoise");
            private static readonly int BlueNoiseId =
                Shader.PropertyToID("_DawnCloudBlueNoise");
            private static readonly int LowDepthTextureId =
                Shader.PropertyToID("_DawnCloudLowDepthTexture");
            private static readonly int CloudTextureId =
                Shader.PropertyToID("_DawnCloudTexture");
            private static readonly int CloudDistanceTextureId =
                Shader.PropertyToID("_DawnCloudDistanceTexture");
            private static readonly int CloudHistoryTextureId =
                Shader.PropertyToID("_DawnCloudHistoryTexture");
            private static readonly int CloudHistoryDistanceTextureId =
                Shader.PropertyToID("_DawnCloudHistoryDistanceTexture");
            private static readonly int CloudBufferSizeId =
                Shader.PropertyToID("_DawnCloudBufferSize");
            private static readonly int CloudPreviousViewProjectionId =
                Shader.PropertyToID("_DawnCloudPreviousViewProjection");
            private static readonly int CloudPreviousCameraPositionId =
                Shader.PropertyToID("_DawnCloudPreviousCameraPosition");
            private static readonly int CloudHistoryValidId =
                Shader.PropertyToID("_DawnCloudHistoryValid");
            private static readonly int CloudTemporalBlendId =
                Shader.PropertyToID("_DawnCloudTemporalBlend");
            private static readonly int CloudDepthDownsampleScaleId =
                Shader.PropertyToID("_DawnCloudDepthDownsampleScale");
            private static readonly int CloudShadowTextureId =
                Shader.PropertyToID("_DawnCloudShadowTexture");
            private static readonly int CloudWorldToShadowId =
                Shader.PropertyToID("_DawnCloudWorldToShadow");
            private static readonly int CloudShadowTexelSizeId =
                Shader.PropertyToID("_DawnCloudShadowTexelSize");
            private static readonly int CloudShadowRayOriginId =
                Shader.PropertyToID("_DawnCloudShadowRayOrigin");
            private static readonly int CloudShadowRightId =
                Shader.PropertyToID("_DawnCloudShadowRight");
            private static readonly int CloudShadowUpId =
                Shader.PropertyToID("_DawnCloudShadowUp");
            private static readonly int CloudShadowLightDirectionId =
                Shader.PropertyToID("_DawnCloudShadowLightDirection");
            private static readonly int BlitScaleBiasId =
                Shader.PropertyToID("_BlitScaleBias");
            private static readonly int BoundsMinimumId =
                Shader.PropertyToID("_DawnCloudBoundsMin");
            private static readonly int BoundsMaximumId =
                Shader.PropertyToID("_DawnCloudBoundsMax");
            private static readonly int ShapeNoiseWeightsId =
                Shader.PropertyToID("_DawnCloudShapeNoiseWeights");
            private static readonly int ColorAId =
                Shader.PropertyToID("_DawnCloudColorA");
            private static readonly int ColorBId =
                Shader.PropertyToID("_DawnCloudColorB");
            private static readonly int PhaseParametersId =
                Shader.PropertyToID("_DawnCloudPhaseParameters");
            private static readonly int PowderEffectIntensityId =
                Shader.PropertyToID("_DawnCloudPowderEffectIntensity");
            private static readonly int MultiScatterParametersId =
                Shader.PropertyToID("_DawnCloudMultiScatterParameters");
            private static readonly int DiffuseFieldParametersId =
                Shader.PropertyToID("_DawnCloudDiffuseFieldParameters");
            private static readonly int DiffuseFieldTransportParametersId =
                Shader.PropertyToID(
                    "_DawnCloudDiffuseFieldTransportParameters");
            private static readonly int AmbientOcclusionStrengthId =
                Shader.PropertyToID("_DawnCloudAmbientOcclusionStrength");
            private static readonly int AmbientSkyColorId =
                Shader.PropertyToID("_DawnCloudAmbientSkyColor");
            private static readonly int AmbientEquatorColorId =
                Shader.PropertyToID("_DawnCloudAmbientEquatorColor");
            private static readonly int AmbientGroundColorId =
                Shader.PropertyToID("_DawnCloudAmbientGroundColor");
            private static readonly int SpeedWarpId =
                Shader.PropertyToID("_DawnCloudSpeedWarp");
            private static readonly int BlueNoiseScaleId =
                Shader.PropertyToID("_DawnCloudBlueNoiseScale");
            private static readonly int CoverageId =
                Shader.PropertyToID("_DawnCloudCoverage");
            private static readonly int WeatherMapTilingId =
                Shader.PropertyToID("_DawnCloudWeatherMapTiling");
            private static readonly int ShapeTilingId =
                Shader.PropertyToID("_DawnCloudShapeTiling");
            private static readonly int DetailTilingId =
                Shader.PropertyToID("_DawnCloudDetailTiling");
            private static readonly int DensityOffsetId =
                Shader.PropertyToID("_DawnCloudDensityOffset");
            private static readonly int DensityMultiplierId =
                Shader.PropertyToID("_DawnCloudDensityMultiplier");
            private static readonly int DetailWeightsId =
                Shader.PropertyToID("_DawnCloudDetailWeights");
            private static readonly int DetailNoiseWeightId =
                Shader.PropertyToID("_DawnCloudDetailNoiseWeight");
            private static readonly int HeightWeightsId =
                Shader.PropertyToID("_DawnCloudHeightWeights");
            private static readonly int HeightProfileBlendId =
                Shader.PropertyToID("_DawnCloudHeightProfileBlend");
            private static readonly int HeightProfileParametersId =
                Shader.PropertyToID("_DawnCloudHeightProfileParameters");
            private static readonly int RayStepExponentId =
                Shader.PropertyToID("_DawnCloudRayStepExponent");
            private static readonly int RayStepLengthId =
                Shader.PropertyToID("_DawnCloudRayStepLength");
            private static readonly int RayOffsetStrengthId =
                Shader.PropertyToID("_DawnCloudRayOffsetStrength");
            private static readonly int ColorOffset1Id =
                Shader.PropertyToID("_DawnCloudColorOffset1");
            private static readonly int ColorOffset2Id =
                Shader.PropertyToID("_DawnCloudColorOffset2");
            private static readonly int ExtinctionScaleId =
                Shader.PropertyToID("_DawnCloudExtinctionScale");
            private static readonly int LightAbsorptionTowardSunId =
                Shader.PropertyToID("_DawnCloudLightAbsorptionTowardSun");
            private static readonly int SelfShadowStrengthId =
                Shader.PropertyToID("_DawnCloudSelfShadowStrength");
            private static readonly int LightAbsorptionThroughCloudId =
                Shader.PropertyToID("_DawnCloudLightAbsorptionThroughCloud");
            private static readonly int MaxRayMarchStepsId =
                Shader.PropertyToID("_DawnCloudMaxRayMarchSteps");
            private static readonly MaterialPropertyBlock PropertyBlock =
                new MaterialPropertyBlock();
            private static readonly Vector4 FullScreenScaleBias =
                new Vector4(1f, 1f, 0f, 0f);
            private readonly RenderTargetIdentifier[] cloudTargets =
                new RenderTargetIdentifier[2];
            private readonly RenderTargetIdentifier[] resolveTargets =
                new RenderTargetIdentifier[2];
            private readonly RenderTargetIdentifier[] upsampleTargets =
                new RenderTargetIdentifier[2];

            private Material material;
            private CloudSettings settings;
            private RTHandle lowDepthTexture;
            private RTHandle cloudTexture;
            private RTHandle cloudDistanceTexture;
            private RTHandle resolvedCloudTexture;
            private RTHandle resolvedCloudDistanceTexture;
            private RTHandle upsampledCloudTexture;
            private RTHandle upsampledCloudDistanceTexture;
            private RTHandle cloudShadowTexture;
            private readonly Dictionary<int, HistoryState> historyStates =
                new Dictionary<int, HistoryState>();
            private HistoryState currentHistory;
            private Matrix4x4 currentViewProjection;
            private Vector4 cloudBufferSize;
            private Vector4 blueNoiseScale;
            private Vector3 lightDirection;
            private Vector3 cloudShadowRayOrigin;
            private Vector3 cloudShadowRight;
            private Vector3 cloudShadowUp;
            private Matrix4x4 cloudWorldToShadow;

            private sealed class HistoryState
            {
                public RTHandle Cloud;
                public RTHandle Distance;
                public Matrix4x4 PreviousViewProjection;
                public Vector3 PreviousCameraPosition;
                public int Width;
                public int Height;
                public int VolumeDepth;
                public TextureDimension Dimension;
                public int LastFrame = -1;
                public bool Valid;

                public void Release()
                {
                    Cloud?.Release();
                    Cloud = null;
                    Distance?.Release();
                    Distance = null;
                    Valid = false;
                }
            }

            public DawnVolumetricCloudRenderPass(string passName)
            {
                profilingSampler = new ProfilingSampler(passName);
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public void Setup(
                Material passMaterial,
                CloudSettings cloudSettings,
                Vector3 directionToLight)
            {
                material = passMaterial;
                settings = cloudSettings;
                lightDirection = directionToLight.sqrMagnitude > 0.0001f
                    ? directionToLight.normalized
                    : Vector3.up;
            }

            public override void OnCameraSetup(
                CommandBuffer cmd,
                ref RenderingData renderingData)
            {
                ResetTarget();
                RenderTextureDescriptor fullDescriptor =
                    renderingData.cameraData.cameraTargetDescriptor;
                fullDescriptor.msaaSamples = 1;
                fullDescriptor.depthBufferBits = 0;
                fullDescriptor.useMipMap = false;
                fullDescriptor.autoGenerateMips = false;

                RenderTextureDescriptor descriptor = fullDescriptor;
                int downsample = settings.Downsample;
                descriptor.width = Mathf.Max(1, descriptor.width / downsample);
                descriptor.height = Mathf.Max(1, descriptor.height / downsample);
                cloudBufferSize = new Vector4(
                    descriptor.width,
                    descriptor.height,
                    1f / descriptor.width,
                    1f / descriptor.height);

                RenderTextureDescriptor depthDescriptor = descriptor;
                depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
                RenderingUtils.ReAllocateIfNeeded(
                    ref lowDepthTexture,
                    depthDescriptor,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "_DawnVolumetricCloudLowDepth");

                RenderTextureDescriptor cloudDescriptor = descriptor;
                cloudDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                RenderingUtils.ReAllocateIfNeeded(
                    ref cloudTexture,
                    cloudDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "_DawnVolumetricCloudTexture");

                RenderTextureDescriptor cloudDistanceDescriptor = descriptor;
                cloudDistanceDescriptor.graphicsFormat =
                    GraphicsFormat.R32_SFloat;
                RenderingUtils.ReAllocateIfNeeded(
                    ref cloudDistanceTexture,
                    cloudDistanceDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "_DawnVolumetricCloudDistance");

                RenderingUtils.ReAllocateIfNeeded(
                    ref resolvedCloudTexture,
                    cloudDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "_DawnVolumetricCloudResolved");
                RenderingUtils.ReAllocateIfNeeded(
                    ref resolvedCloudDistanceTexture,
                    cloudDistanceDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "_DawnVolumetricCloudResolvedDistance");

                RenderTextureDescriptor fullCloudDescriptor = fullDescriptor;
                fullCloudDescriptor.graphicsFormat =
                    GraphicsFormat.R16G16B16A16_SFloat;
                RenderingUtils.ReAllocateIfNeeded(
                    ref upsampledCloudTexture,
                    fullCloudDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "_DawnVolumetricCloudUpsampled");
                RenderTextureDescriptor fullDistanceDescriptor = fullDescriptor;
                fullDistanceDescriptor.graphicsFormat =
                    GraphicsFormat.R32_SFloat;
                RenderingUtils.ReAllocateIfNeeded(
                    ref upsampledCloudDistanceTexture,
                    fullDistanceDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "_DawnVolumetricCloudUpsampledDistance");

                PrepareHistory(
                    renderingData.cameraData.camera,
                    cloudDescriptor,
                    cloudDistanceDescriptor);

                var cloudShadowDescriptor = new RenderTextureDescriptor(
                    CloudShadowResolution,
                    CloudShadowResolution,
                    GraphicsFormat.R16_SFloat,
                    0)
                {
                    msaaSamples = 1,
                    dimension = TextureDimension.Tex2D,
                    volumeDepth = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    sRGB = false
                };
                RenderingUtils.ReAllocateIfNeeded(
                    ref cloudShadowTexture,
                    cloudShadowDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "_DawnVolumetricCloudShadow");

                UpdateCloudShadowProjection();

                Camera camera = renderingData.cameraData.camera;
                currentViewProjection =
                    GL.GetGPUProjectionMatrix(
                        camera.nonJitteredProjectionMatrix,
                        true) *
                    camera.worldToCameraMatrix;

                int temporalFrame = Time.frameCount & 63;
                blueNoiseScale = new Vector4(
                    renderingData.cameraData.cameraTargetDescriptor.width /
                    (float)Mathf.Max(1, settings.BlueNoise.width),
                    renderingData.cameraData.cameraTargetDescriptor.height /
                    (float)Mathf.Max(1, settings.BlueNoise.height),
                    Mathf.Repeat(temporalFrame * 0.754877666f, 1f),
                    Mathf.Repeat(temporalFrame * 0.569840296f, 1f));
            }

            public override void Execute(
                ScriptableRenderContext context,
                ref RenderingData renderingData)
            {
                if (material == null || lowDepthTexture == null ||
                    cloudTexture == null || cloudDistanceTexture == null ||
                    resolvedCloudTexture == null ||
                    resolvedCloudDistanceTexture == null ||
                    upsampledCloudTexture == null ||
                    upsampledCloudDistanceTexture == null ||
                    cloudShadowTexture == null || currentHistory == null ||
                    currentHistory.Cloud == null ||
                    currentHistory.Distance == null)
                {
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get();
                Camera camera = renderingData.cameraData.camera;

                using (new ProfilingScope(cmd, profilingSampler))
                {
                    PropertyBlock.Clear();
                    SetCloudProperties(PropertyBlock);
                    PropertyBlock.SetVector(
                        CloudShadowRayOriginId,
                        cloudShadowRayOrigin);
                    PropertyBlock.SetVector(
                        CloudShadowRightId,
                        cloudShadowRight);
                    PropertyBlock.SetVector(
                        CloudShadowUpId,
                        cloudShadowUp);
                    PropertyBlock.SetVector(
                        CloudShadowLightDirectionId,
                        lightDirection);
                    CoreUtils.SetRenderTarget(cmd, cloudShadowTexture);
                    CoreUtils.DrawFullScreen(cmd, material, PropertyBlock, 3);
                    cmd.SetGlobalTexture(
                        CloudShadowTextureId,
                        cloudShadowTexture.nameID);
                    cmd.SetGlobalMatrix(
                        CloudWorldToShadowId,
                        cloudWorldToShadow);
                    cmd.SetGlobalVector(
                        CloudShadowTexelSizeId,
                        new Vector4(
                            1f / CloudShadowResolution,
                            1f / CloudShadowResolution,
                            CloudShadowResolution,
                            CloudShadowResolution));

                    PropertyBlock.Clear();
                    PropertyBlock.SetVector(BlitScaleBiasId, FullScreenScaleBias);
                    PropertyBlock.SetFloat(
                        CloudDepthDownsampleScaleId,
                        settings.Downsample);
                    CoreUtils.SetRenderTarget(cmd, lowDepthTexture);
                    CoreUtils.DrawFullScreen(cmd, material, PropertyBlock, 0);

                    SetCloudProperties(PropertyBlock);
                    PropertyBlock.SetTexture(LowDepthTextureId, lowDepthTexture);
                    cloudTargets[0] = cloudTexture.nameID;
                    cloudTargets[1] = cloudDistanceTexture.nameID;
                    CoreUtils.SetRenderTarget(
                        cmd,
                        cloudTargets,
                        BuiltinRenderTextureType.None);
                    CoreUtils.SetViewport(cmd, cloudTexture);
                    CoreUtils.DrawFullScreen(cmd, material, PropertyBlock, 1);

                    bool historyValid =
                        currentHistory.Valid &&
                        settings.TemporalAccumulation > 0f &&
                        !camera.orthographic &&
                        !camera.stereoEnabled;

                    PropertyBlock.Clear();
                    SetCloudProperties(PropertyBlock);
                    PropertyBlock.SetTexture(CloudTextureId, cloudTexture);
                    PropertyBlock.SetTexture(
                        CloudDistanceTextureId,
                        cloudDistanceTexture);
                    PropertyBlock.SetTexture(
                        CloudHistoryTextureId,
                        currentHistory.Cloud);
                    PropertyBlock.SetTexture(
                        CloudHistoryDistanceTextureId,
                        currentHistory.Distance);
                    PropertyBlock.SetVector(
                        CloudBufferSizeId,
                        cloudBufferSize);
                    PropertyBlock.SetMatrix(
                        CloudPreviousViewProjectionId,
                        currentHistory.PreviousViewProjection);
                    PropertyBlock.SetVector(
                        CloudPreviousCameraPositionId,
                        currentHistory.PreviousCameraPosition);
                    PropertyBlock.SetFloat(
                        CloudHistoryValidId,
                        historyValid ? 1f : 0f);
                    PropertyBlock.SetFloat(
                        CloudTemporalBlendId,
                        settings.TemporalAccumulation);
                    resolveTargets[0] = resolvedCloudTexture.nameID;
                    resolveTargets[1] =
                        resolvedCloudDistanceTexture.nameID;
                    CoreUtils.SetRenderTarget(
                        cmd,
                        resolveTargets,
                        BuiltinRenderTextureType.None);
                    CoreUtils.SetViewport(cmd, resolvedCloudTexture);
                    CoreUtils.DrawFullScreen(cmd, material, PropertyBlock, 4);

                    Blitter.BlitCameraTexture(
                        cmd,
                        resolvedCloudTexture,
                        currentHistory.Cloud);
                    Blitter.BlitCameraTexture(
                        cmd,
                        resolvedCloudDistanceTexture,
                        currentHistory.Distance);

                    PropertyBlock.Clear();
                    SetCloudProperties(PropertyBlock);
                    PropertyBlock.SetTexture(
                        CloudTextureId,
                        resolvedCloudTexture);
                    PropertyBlock.SetTexture(
                        CloudDistanceTextureId,
                        resolvedCloudDistanceTexture);
                    PropertyBlock.SetTexture(
                        LowDepthTextureId,
                        lowDepthTexture);
                    PropertyBlock.SetVector(
                        CloudBufferSizeId,
                        cloudBufferSize);
                    upsampleTargets[0] = upsampledCloudTexture.nameID;
                    upsampleTargets[1] =
                        upsampledCloudDistanceTexture.nameID;
                    CoreUtils.SetRenderTarget(
                        cmd,
                        upsampleTargets,
                        BuiltinRenderTextureType.None);
                    CoreUtils.SetViewport(cmd, upsampledCloudTexture);
                    CoreUtils.DrawFullScreen(cmd, material, PropertyBlock, 5);

                    // Publish the full-resolution reconstruction so fog and
                    // screen-space light shafts consume the same stable result.
                    cmd.SetGlobalTexture(
                        CloudTextureId,
                        upsampledCloudTexture.nameID);
                    cmd.SetGlobalTexture(
                        CloudDistanceTextureId,
                        upsampledCloudDistanceTexture.nameID);

                    RTHandle cameraColor =
                        renderingData.cameraData.renderer.cameraColorTargetHandle;
                    PropertyBlock.Clear();
                    PropertyBlock.SetVector(BlitScaleBiasId, FullScreenScaleBias);
                    PropertyBlock.SetTexture(
                        CloudTextureId,
                        upsampledCloudTexture);
                    CoreUtils.SetRenderTarget(
                        cmd,
                        cameraColor);
                    CoreUtils.DrawFullScreen(cmd, material, PropertyBlock, 2);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                currentHistory.PreviousViewProjection =
                    currentViewProjection;
                currentHistory.PreviousCameraPosition =
                    camera.transform.position;
                currentHistory.LastFrame = Time.frameCount;
                currentHistory.Valid = true;
            }

            public void Dispose()
            {
                lowDepthTexture?.Release();
                lowDepthTexture = null;
                cloudTexture?.Release();
                cloudTexture = null;
                cloudDistanceTexture?.Release();
                cloudDistanceTexture = null;
                resolvedCloudTexture?.Release();
                resolvedCloudTexture = null;
                resolvedCloudDistanceTexture?.Release();
                resolvedCloudDistanceTexture = null;
                upsampledCloudTexture?.Release();
                upsampledCloudTexture = null;
                upsampledCloudDistanceTexture?.Release();
                upsampledCloudDistanceTexture = null;
                cloudShadowTexture?.Release();
                cloudShadowTexture = null;
                foreach (HistoryState history in historyStates.Values)
                {
                    history.Release();
                }
                historyStates.Clear();
                currentHistory = null;
            }

            private void PrepareHistory(
                Camera camera,
                RenderTextureDescriptor cloudDescriptor,
                RenderTextureDescriptor distanceDescriptor)
            {
                int cameraId = camera.GetInstanceID();
                if (!historyStates.TryGetValue(
                        cameraId,
                        out HistoryState history))
                {
                    history = new HistoryState();
                    historyStates.Add(cameraId, history);
                }

                bool descriptorChanged =
                    history.Width != cloudDescriptor.width ||
                    history.Height != cloudDescriptor.height ||
                    history.VolumeDepth != cloudDescriptor.volumeDepth ||
                    history.Dimension != cloudDescriptor.dimension;
                if (descriptorChanged)
                {
                    history.Valid = false;
                    history.Width = cloudDescriptor.width;
                    history.Height = cloudDescriptor.height;
                    history.VolumeDepth = cloudDescriptor.volumeDepth;
                    history.Dimension = cloudDescriptor.dimension;
                }

                RenderingUtils.ReAllocateIfNeeded(
                    ref history.Cloud,
                    cloudDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: $"_DawnVolumetricCloudHistory_{cameraId}");
                RenderingUtils.ReAllocateIfNeeded(
                    ref history.Distance,
                    distanceDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: $"_DawnVolumetricCloudHistoryDistance_{cameraId}");

                if (history.LastFrame >= 0 &&
                    Time.frameCount - history.LastFrame > 1)
                {
                    history.Valid = false;
                }

                float cameraCutDistance = Mathf.Max(
                    100f,
                    (settings.BoundsMaximum - settings.BoundsMinimum)
                        .magnitude * 0.25f);
                if (history.Valid && Vector3.Distance(
                        history.PreviousCameraPosition,
                        camera.transform.position) > cameraCutDistance)
                {
                    history.Valid = false;
                }

                currentHistory = history;
            }

            private void UpdateCloudShadowProjection()
            {
                Vector3 referenceAxis = Mathf.Abs(
                    Vector3.Dot(lightDirection, Vector3.up)) > 0.99f
                    ? Vector3.right
                    : Vector3.up;
                Vector3 right = Vector3.Cross(
                    referenceAxis,
                    lightDirection).normalized;
                Vector3 up = Vector3.Cross(
                    lightDirection,
                    right).normalized;

                float minimumRight = float.PositiveInfinity;
                float maximumRight = float.NegativeInfinity;
                float minimumUp = float.PositiveInfinity;
                float maximumUp = float.NegativeInfinity;
                float minimumLight = float.PositiveInfinity;
                float maximumLight = float.NegativeInfinity;

                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 corner = new Vector3(
                                x == 0
                                    ? settings.BoundsMinimum.x
                                    : settings.BoundsMaximum.x,
                                y == 0
                                    ? settings.BoundsMinimum.y
                                    : settings.BoundsMaximum.y,
                                z == 0
                                    ? settings.BoundsMinimum.z
                                    : settings.BoundsMaximum.z);
                            float projectedRight = Vector3.Dot(corner, right);
                            float projectedUp = Vector3.Dot(corner, up);
                            float projectedLight = Vector3.Dot(
                                corner,
                                lightDirection);
                            minimumRight = Mathf.Min(
                                minimumRight,
                                projectedRight);
                            maximumRight = Mathf.Max(
                                maximumRight,
                                projectedRight);
                            minimumUp = Mathf.Min(minimumUp, projectedUp);
                            maximumUp = Mathf.Max(maximumUp, projectedUp);
                            minimumLight = Mathf.Min(
                                minimumLight,
                                projectedLight);
                            maximumLight = Mathf.Max(
                                maximumLight,
                                projectedLight);
                        }
                    }
                }

                float rightExtent = Mathf.Max(
                    maximumRight - minimumRight,
                    0.01f);
                float upExtent = Mathf.Max(maximumUp - minimumUp, 0.01f);
                float lightExtent = Mathf.Max(
                    maximumLight - minimumLight,
                    0.01f);
                cloudShadowRayOrigin =
                    right * minimumRight +
                    up * minimumUp +
                    lightDirection * (minimumLight - 1f);
                cloudShadowRight = right * rightExtent;
                cloudShadowUp = up * upExtent;

                cloudWorldToShadow = Matrix4x4.identity;
                cloudWorldToShadow.SetRow(
                    0,
                    new Vector4(
                        right.x / rightExtent,
                        right.y / rightExtent,
                        right.z / rightExtent,
                        -minimumRight / rightExtent));
                cloudWorldToShadow.SetRow(
                    1,
                    new Vector4(
                        up.x / upExtent,
                        up.y / upExtent,
                        up.z / upExtent,
                        -minimumUp / upExtent));
                cloudWorldToShadow.SetRow(
                    2,
                    new Vector4(
                        lightDirection.x / lightExtent,
                        lightDirection.y / lightExtent,
                        lightDirection.z / lightExtent,
                        -minimumLight / lightExtent));
                cloudWorldToShadow.SetRow(
                    3,
                    new Vector4(0f, 0f, 0f, 1f));
            }

            private void SetCloudProperties(MaterialPropertyBlock properties)
            {
                properties.SetVector(BlitScaleBiasId, FullScreenScaleBias);
                properties.SetTexture(ShapeNoiseId, settings.ShapeNoise);
                properties.SetTexture(DetailNoiseId, settings.DetailNoise);
                properties.SetTexture(WeatherMapId, settings.WeatherMap);
                properties.SetTexture(MaskNoiseId, settings.MaskNoise);
                properties.SetTexture(BlueNoiseId, settings.BlueNoise);
                properties.SetVector(BoundsMinimumId, settings.BoundsMinimum);
                properties.SetVector(BoundsMaximumId, settings.BoundsMaximum);
                properties.SetVector(ShapeNoiseWeightsId, settings.ShapeNoiseWeights);
                properties.SetColor(ColorAId, settings.ColorA);
                properties.SetColor(ColorBId, settings.ColorB);
                properties.SetVector(PhaseParametersId, settings.PhaseParameters);
                properties.SetFloat(
                    PowderEffectIntensityId,
                    settings.PowderEffectIntensity);
                properties.SetVector(
                    MultiScatterParametersId,
                    settings.MultiScatterParameters);
                properties.SetVector(
                    DiffuseFieldParametersId,
                    settings.DiffuseFieldParameters);
                properties.SetVector(
                    DiffuseFieldTransportParametersId,
                    settings.DiffuseFieldTransportParameters);
                properties.SetFloat(
                    AmbientOcclusionStrengthId,
                    settings.AmbientOcclusionStrength);
                properties.SetColor(AmbientSkyColorId, settings.AmbientSkyColor);
                properties.SetColor(
                    AmbientEquatorColorId,
                    settings.AmbientEquatorColor);
                properties.SetColor(
                    AmbientGroundColorId,
                    settings.AmbientGroundColor);
                properties.SetVector(SpeedWarpId, settings.SpeedWarp);
                properties.SetVector(BlueNoiseScaleId, blueNoiseScale);
                properties.SetFloat(CoverageId, settings.Coverage);
                properties.SetFloat(
                    WeatherMapTilingId,
                    settings.WeatherMapTiling);
                properties.SetFloat(ShapeTilingId, settings.ShapeTiling);
                properties.SetFloat(DetailTilingId, settings.DetailTiling);
                properties.SetFloat(DensityOffsetId, settings.DensityOffset);
                properties.SetFloat(DensityMultiplierId, settings.DensityMultiplier);
                properties.SetFloat(DetailWeightsId, settings.DetailWeights);
                properties.SetFloat(DetailNoiseWeightId, settings.DetailNoiseWeight);
                properties.SetFloat(HeightWeightsId, settings.HeightWeights);
                properties.SetFloat(
                    HeightProfileBlendId,
                    settings.HeightProfileBlend);
                properties.SetVector(
                    HeightProfileParametersId,
                    settings.HeightProfileParameters);
                properties.SetFloat(RayStepExponentId, settings.RayStepExponent);
                properties.SetFloat(RayStepLengthId, settings.RayStepLength);
                properties.SetFloat(RayOffsetStrengthId, settings.RayOffsetStrength);
                properties.SetFloat(ColorOffset1Id, settings.ColorOffset1);
                properties.SetFloat(ColorOffset2Id, settings.ColorOffset2);
                properties.SetFloat(ExtinctionScaleId, settings.ExtinctionScale);
                properties.SetFloat(
                    LightAbsorptionTowardSunId,
                    settings.LightAbsorptionTowardSun);
                properties.SetFloat(
                    SelfShadowStrengthId,
                    settings.SelfShadowStrength);
                properties.SetFloat(
                    LightAbsorptionThroughCloudId,
                    settings.LightAbsorptionThroughCloud);
                properties.SetInt(MaxRayMarchStepsId, settings.MaxRayMarchSteps);
            }
        }
    }
}
#endif
