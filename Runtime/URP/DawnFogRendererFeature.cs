#if DAWNTOD_URP_AVAILABLE
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DawnTOD
{
    public sealed class DawnFogRendererFeature : ScriptableRendererFeature
    {
        private const string FogShaderName = "Hidden/DawnTOD/PostProcessFog";

        [SerializeField, HideInInspector] private Shader fogShader;
        private Material fogMaterial;
        private DawnFogRenderPass fogPass;
        private DawnVolumetricFogPass volumetricPass;
        private bool warnedVolumetricSupport;
        private readonly NubisStorm.NubisDawnResetPass cloudResetPass = new NubisStorm.NubisDawnResetPass();

        public bool IsReady => fogMaterial != null;

        public override void Create()
        {
            fogPass?.Dispose();
            volumetricPass?.Dispose();
            volumetricPass = new DawnVolumetricFogPass();
            CoreUtils.Destroy(fogMaterial);
            fogShader ??= Shader.Find(FogShaderName);
            fogMaterial = fogShader != null
                ? CoreUtils.CreateEngineMaterial(fogShader)
                : null;
            fogPass = new DawnFogRenderPass(name)
            {
                // Fog the opaque scene and sky before Nubis composites its fogged layers.
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 1)
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            renderer.EnqueuePass(cloudResetPass);
            ref CameraData cameraData = ref renderingData.cameraData;
#if UNITY_EDITOR
            // URP 14 forces Scene View AA to None even when the Game camera uses SMAA/FXAA.
            // Keep the Scene View FX toggle, while matching the authored camera's output.
            if (cameraData.isSceneViewCamera && Camera.main != null &&
                Camera.main.TryGetComponent<UniversalAdditionalCameraData>(out var mainCameraData))
            {
                cameraData.postProcessEnabled &= mainCameraData.renderPostProcessing;
                renderingData.postProcessingEnabled &= mainCameraData.renderPostProcessing;
                if (mainCameraData.antialiasing != AntialiasingMode.TemporalAntiAliasing)
                {
                    cameraData.antialiasing = mainCameraData.antialiasing;
                    cameraData.antialiasingQuality = mainCameraData.antialiasingQuality;
                }
                cameraData.isDitheringEnabled = mainCameraData.dithering;
                cameraData.isStopNaNEnabled = mainCameraData.stopNaN && SystemInfo.graphicsShaderLevel >= 35;
            }
#endif
            if (fogMaterial == null ||
                cameraData.cameraType == CameraType.Preview ||
                cameraData.cameraType == CameraType.Reflection ||
                cameraData.renderType == CameraRenderType.Overlay)
            {
                return;
            }

            VolumeStack stack = VolumeManager.instance.stack;
            DawnFogVolume fog = stack?.GetComponent<DawnFogVolume>();
            if (fog == null || !fog.IsActive())
            {
                volumetricPass?.Forget(cameraData.camera);
                return;
            }

            var settings = FogSettings.FromVolume(fog);
            if (fog.volumetricFog.value.enabled && !cameraData.camera.stereoEnabled && volumetricPass != null && volumetricPass.IsSupported)
            {
                volumetricPass.Setup(fog.volumetricFog.value, settings.Parameters, settings.Parameters2,
                    settings.Inscattering, settings.Atmosphere, settings.SkyAmbientScale, settings.DirectionalColor);
                renderer.EnqueuePass(volumetricPass);
            }
            else
            {
                volumetricPass?.Forget(cameraData.camera);
                if (fog.volumetricFog.value.enabled && !warnedVolumetricSupport)
                {
                    warnedVolumetricSupport = true;
                    Debug.LogWarning("Dawn volumetric fog requires a non-stereo camera, compute shaders and 3D RGBAHalf random-write textures; using analytic height fog.");
                }
            }
            fogPass.Setup(fogMaterial, settings);
            renderer.EnqueuePass(fogPass);
        }

        protected override void Dispose(bool disposing)
        {
            fogPass?.Dispose();
            fogPass = null;
            volumetricPass?.Dispose();
            volumetricPass = null;
            CoreUtils.Destroy(fogMaterial);
            fogMaterial = null;
        }

        private readonly struct FogSettings
        {
            // UE stores density/falloff / 1000 in centimeter space. Unity uses meters.
            public readonly Vector4 Parameters;
            public readonly Vector4 Parameters2;
            public readonly Vector4 Distances;
            public readonly Color Inscattering;
            public readonly Vector4 Atmosphere;
            public readonly Color SkyAmbientScale;
            public readonly Color DirectionalColor;

            private FogSettings(Vector4 parameters, Vector4 parameters2, Vector4 distances, Color inscattering, Vector4 atmosphere, Color skyAmbientScale, Color directionalColor)
            {
                Parameters = parameters;
                Parameters2 = parameters2;
                Distances = distances;
                Inscattering = inscattering;
                Atmosphere = atmosphere;
                SkyAmbientScale = skyAmbientScale;
                DirectionalColor = directionalColor;
            }

            public static FogSettings FromVolume(DawnFogVolume fog)
            {
                Color color = fog.inscatteringColor.value;
                color.a = 1f - Mathf.Clamp01(fog.maxOpacity.value);
                return new FogSettings(
                    new Vector4(Mathf.Clamp(fog.density.value, 0f, 10f) * 0.1f,
                        Mathf.Clamp(fog.heightFalloff.value, 0f, 2f) * 0.1f,
                        fog.height.value, Mathf.Max(0f, fog.startDistance.value)),
                    new Vector4(fog.secondFogEnabled.value ? Mathf.Clamp(fog.secondFogDensity.value, 0f, 10f) * 0.1f : 0f,
                        Mathf.Clamp(fog.secondFogHeightFalloff.value, 0f, 2f) * 0.1f,
                        fog.height.value + fog.secondFogHeightOffset.value, 0f),
                    new Vector4(Mathf.Max(0f, fog.endDistance.value),
                        Mathf.Max(0f, fog.cutoffDistance.value), 0f, 0f),
                    color,
                    new Vector4(fog.atmosphereLighting.value ? fog.heightFogContribution.value : 0f,
                        fog.directionalInscatteringExponent.value, fog.directionalInscatteringStartDistance.value, 0f),
                    fog.skyAtmosphereAmbientContributionColorScale.value, fog.directionalInscatteringColor.value);
            }
        }

        private sealed class DawnFogRenderPass : ScriptableRenderPass
        {
            private static readonly int BlitTextureId =
                Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBiasId =
                Shader.PropertyToID("_BlitScaleBias");
            private static readonly int FogParametersId =
                Shader.PropertyToID("_DawnExponentialFogParameters");
            private static readonly int FogInscatteringId =
                Shader.PropertyToID("_DawnFogInscattering");
            private static readonly int FogParameters2Id =
                Shader.PropertyToID("_DawnExponentialFogParameters2");
            private static readonly int FogDistancesId =
                Shader.PropertyToID("_DawnExponentialFogDistances");
            private static readonly int FogEnabledId =
                Shader.PropertyToID("_DawnFogEnabled");
            private static readonly Vector4 FullScreenScaleBias =
                new Vector4(1f, 1f, 0f, 0f);
            private static readonly MaterialPropertyBlock PropertyBlock =
                new MaterialPropertyBlock();

            private Material material;
            private FogSettings settings;
            private RTHandle copiedColor;

            public DawnFogRenderPass(string passName)
            {
                profilingSampler = new ProfilingSampler(passName);
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public void Setup(Material passMaterial, FogSettings fogSettings)
            {
                material = passMaterial;
                settings = fogSettings;
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
                    name: "_DawnFogColorCopy");
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

                    // Scene and Game views can use different portions of an RTHandle.
                    Blitter.BlitCameraTexture(cmd, cameraColor, copiedColor);

                    PropertyBlock.Clear();
                    PropertyBlock.SetTexture(BlitTextureId, copiedColor);
                    PropertyBlock.SetVector(BlitScaleBiasId, FullScreenScaleBias);
                    cmd.SetGlobalVector(FogParametersId, settings.Parameters);
                    cmd.SetGlobalVector(FogParameters2Id, settings.Parameters2);
                    cmd.SetGlobalVector(FogDistancesId, settings.Distances);
                    cmd.SetGlobalColor(FogInscatteringId, settings.Inscattering);
                    cmd.SetGlobalVector("_DawnFogAtmosphere", settings.Atmosphere);
                    cmd.SetGlobalColor("_DawnFogSkyAmbientScale", settings.SkyAmbientScale);
                    cmd.SetGlobalColor("_DawnFogDirectionalColor", settings.DirectionalColor);
                    cmd.SetGlobalFloat(FogEnabledId, 1f);

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
