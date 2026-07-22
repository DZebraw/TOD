#if USING_URP
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

            CloudSettings settings = CloudSettings.FromVolume(cloud);
            if (!settings.HasRequiredTextures)
            {
                return;
            }

            Vector3 directionToLight = Vector3.up;
            int mainLightIndex = renderingData.lightData.mainLightIndex;
            if (mainLightIndex >= 0 &&
                mainLightIndex < renderingData.lightData.visibleLights.Length &&
                renderingData.lightData.visibleLights[mainLightIndex].lightType ==
                LightType.Directional)
            {
                directionToLight =
                    -renderingData.lightData.visibleLights[mainLightIndex]
                        .localToWorldMatrix.GetColumn(2);
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
            public readonly float Coverage;
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
            public readonly float LightAbsorptionTowardSun;
            public readonly float LightAbsorptionThroughCloud;
            public readonly Vector4 PhaseParameters;
            public readonly float PhaseMinimum;
            public readonly Vector4 MultiScatterParameters;
            public readonly Color AmbientSkyColor;
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
                float coverage,
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
                float lightAbsorptionTowardSun,
                float lightAbsorptionThroughCloud,
                Vector4 phaseParameters,
                float phaseMinimum,
                Vector4 multiScatterParameters,
                Color ambientSkyColor,
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
                Coverage = Mathf.Clamp01(coverage);
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
                LightAbsorptionTowardSun = lightAbsorptionTowardSun;
                LightAbsorptionThroughCloud = lightAbsorptionThroughCloud;
                PhaseParameters = SanitizePhaseParameters(phaseParameters);
                PhaseMinimum = Mathf.Clamp01(phaseMinimum);
                MultiScatterParameters = SanitizeMultiScatterParameters(
                    multiScatterParameters);
                AmbientSkyColor = ambientSkyColor;
                AmbientGroundColor = ambientGroundColor;
                SpeedWarp = speedWarp;
            }

            private static Vector4 SanitizePhaseParameters(Vector4 parameters)
            {
                parameters.x = Mathf.Clamp(parameters.x, 0f, 0.9f);
                parameters.y = Mathf.Clamp(parameters.y, -0.75f, 0f);
                parameters.z = Mathf.Clamp01(parameters.z);
                parameters.w = Mathf.Max(0f, parameters.w);
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

            private static Vector4 SanitizeHeightProfileParameters(Vector4 parameters)
            {
                parameters.x = Mathf.Clamp(parameters.x, 0.01f, 0.3f);
                parameters.y = Mathf.Clamp(parameters.y, 0.25f, 0.9f);
                parameters.z = Mathf.Clamp01(parameters.z);
                parameters.w = Mathf.Clamp(parameters.w, 0.02f, 0.5f);
                return parameters;
            }

            public static CloudSettings FromVolume(DawnVolumetricCloudVolume cloud)
            {
                Vector3 size = cloud.boundsSize.value;
                size.x = Mathf.Max(size.x, 0.01f);
                size.y = Mathf.Max(size.y, 0.01f);
                size.z = Mathf.Max(size.z, 0.01f);
                Vector3 halfSize = size * 0.5f;
                Vector3 center = cloud.boundsCenter.value;
                Color sceneAmbient = RenderSettings.ambientLight;
                Color ambientSkyColor = MultiplyRgb(
                    sceneAmbient,
                    cloud.skyLightTint.value,
                    cloud.skyLightIntensity.value);
                Color ambientGroundColor = MultiplyRgb(
                    sceneAmbient,
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
                    cloud.coverage.value,
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
                    cloud.lightAbsorptionTowardSun.value,
                    cloud.lightAbsorptionThroughCloud.value,
                    cloud.phaseParameters.value,
                    cloud.phaseMinimum.value,
                    new Vector4(
                        cloud.multiScatterExtinction.value,
                        cloud.multiScatterContribution.value,
                        cloud.multiScatterDirectionality.value,
                        0f),
                    ambientSkyColor,
                    ambientGroundColor,
                    cloud.speedWarp.value);
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
            private static readonly int CloudShadowTextureId =
                Shader.PropertyToID("_DawnCloudShadowTexture");
            private static readonly int CloudWorldToShadowId =
                Shader.PropertyToID("_DawnCloudWorldToShadow");
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
            private static readonly int PhaseMinimumId =
                Shader.PropertyToID("_DawnCloudPhaseMinimum");
            private static readonly int MultiScatterParametersId =
                Shader.PropertyToID("_DawnCloudMultiScatterParameters");
            private static readonly int AmbientSkyColorId =
                Shader.PropertyToID("_DawnCloudAmbientSkyColor");
            private static readonly int AmbientGroundColorId =
                Shader.PropertyToID("_DawnCloudAmbientGroundColor");
            private static readonly int SpeedWarpId =
                Shader.PropertyToID("_DawnCloudSpeedWarp");
            private static readonly int BlueNoiseScaleId =
                Shader.PropertyToID("_DawnCloudBlueNoiseScale");
            private static readonly int CoverageId =
                Shader.PropertyToID("_DawnCloudCoverage");
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
            private static readonly int LightAbsorptionTowardSunId =
                Shader.PropertyToID("_DawnCloudLightAbsorptionTowardSun");
            private static readonly int LightAbsorptionThroughCloudId =
                Shader.PropertyToID("_DawnCloudLightAbsorptionThroughCloud");
            private static readonly int MaxRayMarchStepsId =
                Shader.PropertyToID("_DawnCloudMaxRayMarchSteps");
            private static readonly MaterialPropertyBlock PropertyBlock =
                new MaterialPropertyBlock();
            private static readonly Vector4 FullScreenScaleBias =
                new Vector4(1f, 1f, 0f, 0f);

            private Material material;
            private CloudSettings settings;
            private RTHandle lowDepthTexture;
            private RTHandle cloudTexture;
            private RTHandle cloudShadowTexture;
            private Vector4 blueNoiseScale;
            private Vector3 lightDirection;
            private Vector3 cloudShadowRayOrigin;
            private Vector3 cloudShadowRight;
            private Vector3 cloudShadowUp;
            private Matrix4x4 cloudWorldToShadow;

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
                RenderTextureDescriptor descriptor =
                    renderingData.cameraData.cameraTargetDescriptor;
                int downsample = settings.Downsample;
                descriptor.width = Mathf.Max(1, descriptor.width / downsample);
                descriptor.height = Mathf.Max(1, descriptor.height / downsample);
                descriptor.msaaSamples = 1;
                descriptor.depthBufferBits = 0;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;

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

                blueNoiseScale = new Vector4(
                    renderingData.cameraData.cameraTargetDescriptor.width /
                    (float)Mathf.Max(1, settings.BlueNoise.width),
                    renderingData.cameraData.cameraTargetDescriptor.height /
                    (float)Mathf.Max(1, settings.BlueNoise.height),
                    0f,
                    0f);
            }

            public override void Execute(
                ScriptableRenderContext context,
                ref RenderingData renderingData)
            {
                if (material == null || lowDepthTexture == null ||
                    cloudTexture == null || cloudShadowTexture == null)
                {
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get();
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

                    PropertyBlock.Clear();
                    PropertyBlock.SetVector(BlitScaleBiasId, FullScreenScaleBias);
                    CoreUtils.SetRenderTarget(cmd, lowDepthTexture);
                    CoreUtils.DrawFullScreen(cmd, material, PropertyBlock, 0);

                    SetCloudProperties(PropertyBlock);
                    PropertyBlock.SetTexture(LowDepthTextureId, lowDepthTexture);
                    CoreUtils.SetRenderTarget(cmd, cloudTexture);
                    CoreUtils.DrawFullScreen(cmd, material, PropertyBlock, 1);

                    // The directional-light pass runs immediately after clouds.
                    // Publish alpha (camera-ray cloud transmittance) so cloud gaps
                    // can shape screen-space crepuscular rays.
                    cmd.SetGlobalTexture(CloudTextureId, cloudTexture.nameID);

                    PropertyBlock.Clear();
                    PropertyBlock.SetVector(BlitScaleBiasId, FullScreenScaleBias);
                    PropertyBlock.SetTexture(CloudTextureId, cloudTexture);
                    CoreUtils.SetRenderTarget(
                        cmd,
                        renderingData.cameraData.renderer.cameraColorTargetHandle);
                    CoreUtils.DrawFullScreen(cmd, material, PropertyBlock, 2);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                lowDepthTexture?.Release();
                lowDepthTexture = null;
                cloudTexture?.Release();
                cloudTexture = null;
                cloudShadowTexture?.Release();
                cloudShadowTexture = null;
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
                properties.SetFloat(PhaseMinimumId, settings.PhaseMinimum);
                properties.SetVector(
                    MultiScatterParametersId,
                    settings.MultiScatterParameters);
                properties.SetColor(AmbientSkyColorId, settings.AmbientSkyColor);
                properties.SetColor(
                    AmbientGroundColorId,
                    settings.AmbientGroundColor);
                properties.SetVector(SpeedWarpId, settings.SpeedWarp);
                properties.SetVector(BlueNoiseScaleId, blueNoiseScale);
                properties.SetFloat(CoverageId, settings.Coverage);
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
                properties.SetFloat(
                    LightAbsorptionTowardSunId,
                    settings.LightAbsorptionTowardSun);
                properties.SetFloat(
                    LightAbsorptionThroughCloudId,
                    settings.LightAbsorptionThroughCloud);
                properties.SetInt(MaxRayMarchStepsId, settings.MaxRayMarchSteps);
            }
        }
    }
}
#endif
