#if USING_URP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DawnTOD
{
    public sealed class DawnDirectionalVolumetricLightRendererFeature :
        ScriptableRendererFeature
    {
        private const string VolumetricLightShaderName =
            "Hidden/DawnTOD/DirectionalVolumetricLight";

        [SerializeField, HideInInspector] private Shader volumetricLightShader;

        private Material volumetricLightMaterial;
        private DawnDirectionalVolumetricLightRenderPass volumetricLightPass;

        public bool IsReady => volumetricLightMaterial != null;

        public override void Create()
        {
            CoreUtils.Destroy(volumetricLightMaterial);
            volumetricLightShader ??= Shader.Find(VolumetricLightShaderName);
            volumetricLightMaterial = volumetricLightShader != null
                ? CoreUtils.CreateEngineMaterial(volumetricLightShader)
                : null;
            volumetricLightPass = new DawnDirectionalVolumetricLightRenderPass(name)
            {
                // Composite after Dawn clouds and before Dawn fog/post processing.
                renderPassEvent = (RenderPassEvent)(
                    (int)RenderPassEvent.BeforeRenderingPostProcessing - 1)
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            ref CameraData cameraData = ref renderingData.cameraData;
            if (volumetricLightMaterial == null ||
                cameraData.cameraType == CameraType.Preview ||
                cameraData.cameraType == CameraType.Reflection ||
                cameraData.renderType == CameraRenderType.Overlay)
            {
                return;
            }

            int mainLightIndex = renderingData.lightData.mainLightIndex;
            if (mainLightIndex < 0 ||
                mainLightIndex >= renderingData.lightData.visibleLights.Length ||
                renderingData.lightData.visibleLights[mainLightIndex].lightType !=
                LightType.Directional)
            {
                return;
            }

            VolumeStack stack = VolumeManager.instance.stack;
            DawnDirectionalVolumetricLightVolume volumetricLight =
                stack?.GetComponent<DawnDirectionalVolumetricLightVolume>();
            if (volumetricLight == null || !volumetricLight.IsActive())
            {
                return;
            }

            DawnVolumetricCloudVolume cloud =
                stack?.GetComponent<DawnVolumetricCloudVolume>();
            bool hasActiveClouds = cloud != null && cloud.IsActive();
            VisibleLight mainLight =
                renderingData.lightData.visibleLights[mainLightIndex];

            volumetricLightPass.Setup(
                volumetricLightMaterial,
                VolumetricLightSettings.FromVolume(
                    volumetricLight,
                    hasActiveClouds,
                    cameraData.camera,
                    mainLight));
            renderer.EnqueuePass(volumetricLightPass);
        }

        protected override void Dispose(bool disposing)
        {
            volumetricLightPass?.Dispose();
            volumetricLightPass = null;
            CoreUtils.Destroy(volumetricLightMaterial);
            volumetricLightMaterial = null;
        }

        private readonly struct VolumetricLightSettings
        {
            public readonly float Intensity;
            public readonly Color ScatteringTint;
            public readonly float MeanFreePath;
            public readonly float Anisotropy;
            public readonly float ShadowStrength;
            public readonly float MaximumDistance;
            public readonly int StepCount;
            public readonly float Jitter;
            public readonly bool AffectSky;
            public readonly float CloudShaftIntensity;
            public readonly float CloudShaftLength;
            public readonly float CloudShaftDecay;
            public readonly int CloudShaftSampleCount;
            public readonly Vector3 SunViewportPosition;

            private VolumetricLightSettings(
                float intensity,
                Color scatteringTint,
                float meanFreePath,
                float anisotropy,
                float shadowStrength,
                float maximumDistance,
                int stepCount,
                float jitter,
                bool affectSky,
                float cloudShaftIntensity,
                float cloudShaftLength,
                float cloudShaftDecay,
                int cloudShaftSampleCount,
                Vector3 sunViewportPosition)
            {
                Intensity = intensity;
                ScatteringTint = scatteringTint;
                MeanFreePath = meanFreePath;
                Anisotropy = anisotropy;
                ShadowStrength = shadowStrength;
                MaximumDistance = maximumDistance;
                StepCount = stepCount;
                Jitter = jitter;
                AffectSky = affectSky;
                CloudShaftIntensity = cloudShaftIntensity;
                CloudShaftLength = cloudShaftLength;
                CloudShaftDecay = cloudShaftDecay;
                CloudShaftSampleCount = cloudShaftSampleCount;
                SunViewportPosition = sunViewportPosition;
            }

            public static VolumetricLightSettings FromVolume(
                DawnDirectionalVolumetricLightVolume volumetricLight,
                bool hasActiveClouds,
                Camera camera,
                VisibleLight mainLight)
            {
                Vector3 directionToLight =
                    -mainLight.localToWorldMatrix.GetColumn(2);
                Vector3 sunViewportPosition = camera.WorldToViewportPoint(
                    camera.transform.position +
                    directionToLight * Mathf.Max(camera.farClipPlane, 1f));
                bool enableCloudShafts =
                    hasActiveClouds &&
                    volumetricLight.enableCloudShafts.value &&
                    sunViewportPosition.z > 0f;

                return new VolumetricLightSettings(
                    Mathf.Max(0f, volumetricLight.intensity.value),
                    volumetricLight.scatteringTint.value,
                    Mathf.Max(0.01f, volumetricLight.meanFreePath.value),
                    Mathf.Clamp(volumetricLight.anisotropy.value, -0.9f, 0.9f),
                    Mathf.Clamp01(volumetricLight.shadowStrength.value),
                    Mathf.Max(0.01f, volumetricLight.maximumDistance.value),
                    Mathf.Clamp(volumetricLight.stepCount.value, 8, 128),
                    Mathf.Clamp01(volumetricLight.jitter.value),
                    volumetricLight.affectSky.value,
                    enableCloudShafts
                        ? Mathf.Max(0f, volumetricLight.cloudShaftIntensity.value)
                        : 0f,
                    Mathf.Clamp(volumetricLight.cloudShaftLength.value, 0.05f, 1f),
                    Mathf.Clamp(volumetricLight.cloudShaftDecay.value, 0.8f, 1f),
                    Mathf.Clamp(volumetricLight.cloudShaftSampleCount.value, 4, 64),
                    sunViewportPosition);
            }
        }

        private sealed class DawnDirectionalVolumetricLightRenderPass :
            ScriptableRenderPass
        {
            private static readonly int BlitTextureId =
                Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBiasId =
                Shader.PropertyToID("_BlitScaleBias");
            private static readonly int ScatteringParametersId =
                Shader.PropertyToID("_DawnDirectionalLightScatteringParameters");
            private static readonly int QualityParametersId =
                Shader.PropertyToID("_DawnDirectionalLightQualityParameters");
            private static readonly int ScatteringTintId =
                Shader.PropertyToID("_DawnDirectionalLightScatteringTint");
            private static readonly int CloudShaftParametersId =
                Shader.PropertyToID("_DawnDirectionalLightCloudShaftParameters");
            private static readonly int SunScreenPositionId =
                Shader.PropertyToID("_DawnDirectionalLightSunScreenPosition");
            private static readonly Vector4 FullScreenScaleBias =
                new Vector4(1f, 1f, 0f, 0f);
            private static readonly MaterialPropertyBlock PropertyBlock =
                new MaterialPropertyBlock();

            private Material material;
            private VolumetricLightSettings settings;
            private RTHandle copiedColor;

            public DawnDirectionalVolumetricLightRenderPass(string passName)
            {
                profilingSampler = new ProfilingSampler(passName);
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public void Setup(
                Material passMaterial,
                VolumetricLightSettings volumetricLightSettings)
            {
                material = passMaterial;
                settings = volumetricLightSettings;
            }

            public override void OnCameraSetup(
                CommandBuffer cmd,
                ref RenderingData renderingData)
            {
                ResetTarget();
                RenderTextureDescriptor descriptor =
                    renderingData.cameraData.cameraTargetDescriptor;
                descriptor.msaaSamples = 1;
                descriptor.depthBufferBits = 0;
                RenderingUtils.ReAllocateIfNeeded(
                    ref copiedColor,
                    descriptor,
                    name: "_DawnDirectionalVolumetricLightColorCopy");
            }

            public override void Execute(
                ScriptableRenderContext context,
                ref RenderingData renderingData)
            {
                if (material == null || copiedColor == null)
                {
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, profilingSampler))
                {
                    RTHandle cameraColor =
                        renderingData.cameraData.renderer.cameraColorTargetHandle;

                    CoreUtils.SetRenderTarget(cmd, copiedColor);
                    Blitter.BlitTexture(
                        cmd,
                        cameraColor,
                        FullScreenScaleBias,
                        0f,
                        false);

                    PropertyBlock.Clear();
                    PropertyBlock.SetTexture(BlitTextureId, copiedColor);
                    PropertyBlock.SetVector(BlitScaleBiasId, FullScreenScaleBias);
                    PropertyBlock.SetVector(
                        ScatteringParametersId,
                        new Vector4(
                            settings.Intensity,
                            settings.MeanFreePath,
                            settings.Anisotropy,
                            settings.ShadowStrength));
                    PropertyBlock.SetVector(
                        QualityParametersId,
                        new Vector4(
                            settings.MaximumDistance,
                            settings.StepCount,
                            settings.Jitter,
                            settings.AffectSky ? 1f : 0f));
                    PropertyBlock.SetColor(
                        ScatteringTintId,
                        settings.ScatteringTint);
                    PropertyBlock.SetVector(
                        CloudShaftParametersId,
                        new Vector4(
                            settings.CloudShaftIntensity,
                            settings.CloudShaftSampleCount,
                            settings.CloudShaftLength,
                            settings.CloudShaftDecay));
                    PropertyBlock.SetVector(
                        SunScreenPositionId,
                        new Vector4(
                            settings.SunViewportPosition.x,
                            settings.SunViewportPosition.y,
                            settings.SunViewportPosition.z > 0f ? 1f : 0f,
                            0f));

                    CoreUtils.SetRenderTarget(cmd, cameraColor);
                    cmd.DrawProcedural(
                        Matrix4x4.identity,
                        material,
                        0,
                        MeshTopology.Triangles,
                        3,
                        1,
                        PropertyBlock);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                copiedColor?.Release();
                copiedColor = null;
            }
        }
    }
}
#endif
