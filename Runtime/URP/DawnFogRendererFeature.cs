#if USING_URP
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

        public bool IsReady => fogMaterial != null;

        public override void Create()
        {
            CoreUtils.Destroy(fogMaterial);
            fogShader ??= Shader.Find(FogShaderName);
            fogMaterial = fogShader != null
                ? CoreUtils.CreateEngineMaterial(fogShader)
                : null;
            fogPass = new DawnFogRenderPass(name)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            ref CameraData cameraData = ref renderingData.cameraData;
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
                return;
            }

            fogPass.Setup(fogMaterial, FogSettings.FromVolume(fog));
            renderer.EnqueuePass(fogPass);
        }

        protected override void Dispose(bool disposing)
        {
            fogPass?.Dispose();
            fogPass = null;
            CoreUtils.Destroy(fogMaterial);
            fogMaterial = null;
        }

        private readonly struct FogSettings
        {
            public readonly float MeanFreePath;
            public readonly float BaseHeight;
            public readonly float MaximumHeight;
            public readonly float MaximumFogDistance;
            public readonly Color Albedo;
            public readonly bool AffectSky;

            private FogSettings(
                float meanFreePath,
                float baseHeight,
                float maximumHeight,
                float maximumFogDistance,
                Color albedo,
                bool affectSky)
            {
                MeanFreePath = meanFreePath;
                BaseHeight = baseHeight;
                MaximumHeight = maximumHeight;
                MaximumFogDistance = maximumFogDistance;
                Albedo = albedo;
                AffectSky = affectSky;
            }

            public static FogSettings FromVolume(DawnFogVolume fog)
            {
                float baseHeight = fog.baseHeight.value;
                return new FogSettings(
                    Mathf.Max(0.01f, fog.meanFreePath.value),
                    baseHeight,
                    Mathf.Max(baseHeight + 0.01f, fog.maximumHeight.value),
                    Mathf.Max(0.01f, fog.maximumFogDistance.value),
                    fog.albedo.value,
                    fog.affectSky.value);
            }
        }

        private sealed class DawnFogRenderPass : ScriptableRenderPass
        {
            private static readonly int BlitTextureId =
                Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBiasId =
                Shader.PropertyToID("_BlitScaleBias");
            private static readonly int FogParametersId =
                Shader.PropertyToID("_DawnFogParameters");
            private static readonly int FogAlbedoId =
                Shader.PropertyToID("_DawnFogAlbedo");
            private static readonly int FogAffectSkyId =
                Shader.PropertyToID("_DawnFogAffectSky");
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
                        FogParametersId,
                        new Vector4(
                            settings.MeanFreePath,
                            settings.BaseHeight,
                            settings.MaximumHeight,
                            settings.MaximumFogDistance));
                    PropertyBlock.SetColor(FogAlbedoId, settings.Albedo);
                    PropertyBlock.SetFloat(
                        FogAffectSkyId,
                        settings.AffectSky ? 1f : 0f);

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
