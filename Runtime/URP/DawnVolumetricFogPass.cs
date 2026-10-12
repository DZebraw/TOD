#if DAWNTOD_URP_AVAILABLE
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DawnTOD
{
    internal sealed class DawnVolumetricFogPass : ScriptableRenderPass, IDisposable
    {
        internal struct Statistics
        {
            public int width, height, depth, frames, resets, lights;
            public bool historyUsed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FogLight
        {
            public Vector4 positionType, colorShadow, directionRange, parameters;
        }

        private sealed class CameraHistory : IDisposable
        {
            public Camera camera;
            public RenderTexture[] scattering = new RenderTexture[2];
            public RenderTexture integrated;
            public ComputeBuffer staticSH;
            public int width, height, depth, read, sequence, resets, signature;
            public bool valid;
            public float lastSeen, probeTime;
            public Matrix4x4 vp, projection;
            public Vector3 origin, forward, probeOrigin;
            public Quaternion rotation, probeRotation;
            public Vector4 gridZ;
            public Statistics statistics;
            public void Dispose()
            {
                foreach (var texture in scattering) CoreUtils.Destroy(texture);
                CoreUtils.Destroy(integrated);
                scattering[0] = scattering[1] = integrated = null;
                staticSH?.Dispose(); staticSH = null;
                valid = false;
            }
        }

        private const int MaxLights = 256, ProbeSize = 8;
        private static readonly Vector3[] SHDirections = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private static readonly Color[] SHColors = new Color[6];
        private readonly Dictionary<int, CameraHistory> histories = new Dictionary<int, CameraHistory>();
        private readonly List<int> expired = new List<int>();
        private readonly ComputeShader shader;
        private readonly int scatterKernel, integrateKernel;
        private readonly FogLight[] lights = new FogLight[MaxLights];
        private readonly Vector4[] skySH = new Vector4[3], jitter = new Vector4[4];
        private readonly Vector3[] probePositions = new Vector3[ProbeSize * ProbeSize * ProbeSize];
        private readonly SphericalHarmonicsL2[] probes = new SphericalHarmonicsL2[ProbeSize * ProbeSize * ProbeSize];
        private readonly Vector4[] probeSH = new Vector4[ProbeSize * ProbeSize * ProbeSize * 3];
        private ComputeBuffer lightBuffer, emptySH;
        private DawnVolumetricFogSettings settings;
        private Vector4 primary, second, atmosphere;
        private Color fogColor, skyScale, directionalColor;

        public bool IsSupported => shader && SystemInfo.supportsComputeShaders && SystemInfo.supports3DTextures &&
            SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.LoadStore);

        public DawnVolumetricFogPass()
        {
            renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 2);
            profilingSampler = new ProfilingSampler("Dawn / volumetric fog");
            shader = Resources.Load<ComputeShader>("DawnTOD/DawnVolumetricFog");
            if (shader)
            {
                scatterKernel = shader.FindKernel("LightScatteringCS");
                integrateKernel = shader.FindKernel("FinalIntegrationCS");
            }
        }

        public void Setup(DawnVolumetricFogSettings input, Vector4 layer, Vector4 layer2,
            Color inscattering, Vector4 atmosphereSettings, Color ambientScale, Color directional)
        {
            settings = input.Sanitized(); primary = layer; second = layer2;
            fogColor = inscattering; atmosphere = atmosphereSettings; skyScale = ambientScale; directionalColor = directional;
        }

        public bool TryGetStatistics(Camera camera, out Statistics statistics)
        {
            if (camera && histories.TryGetValue(camera.GetInstanceID(), out var history))
            { statistics = history.statistics; return true; }
            statistics = default; return false;
        }

        public void Forget(Camera camera)
        {
            if (!camera || !histories.TryGetValue(camera.GetInstanceID(), out var history)) return;
            history.Dispose(); histories.Remove(camera.GetInstanceID());
        }

        // RenderUtils.h::CalculateGridZParams, with the 9.5 cm near offset converted to meters.
        internal static Vector4 GridZParams(float near, float far, int depth)
        {
            const double scale = 32;
            double n = near + 0.095, f = Math.Max(far, n + 0.001);
            double o = (f - n * Math.Pow(2, depth / scale)) / (f - n);
            return new Vector4((float)((1 - o) / n), (float)o, (float)scale, 0);
        }

        internal static void PackSH(SphericalHarmonicsL2 probe, Vector4[] destination, int offset)
        {
            // Recover the first two bands without depending on Unity's internal SH packing.
            probe.Evaluate(SHDirections, SHColors);
            Color l0 = Color.clear;
            foreach (var color in SHColors) l0 += color / 6f;
            Color x = (SHColors[0] - SHColors[1]) * 0.5f;
            Color y = (SHColors[2] - SHColors[3]) * 0.5f;
            Color z = (SHColors[4] - SHColors[5]) * 0.5f;
            for (int channel = 0; channel < 3; channel++)
                destination[offset + channel] = new Vector4(x[channel], y[channel], z[channel], l0[channel]) / Mathf.PI;
        }

        private static RenderTexture CreateVolume(int width, int height, int depth, string name)
        {
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                dimension = TextureDimension.Tex3D, volumeDepth = depth, depthBufferBits = 0,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite = true,
                msaaSamples = 1, useMipMap = false, autoGenerateMips = false, sRGB = false
            };
            var texture = new RenderTexture(descriptor)
            { name = name, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!texture.Create()) { CoreUtils.Destroy(texture); return null; }
            return texture;
        }

        private int Signature()
        {
            unchecked
            {
                int hash = primary.GetHashCode() * 31 + second.GetHashCode();
                foreach (float value in new[] { settings.scatteringDistribution, settings.extinctionScale,
                    settings.viewDistance, settings.startDistance, settings.nearFadeInDistance,
                    settings.staticLightingScatteringIntensity, settings.historyWeight }) hash = hash * 31 + value.GetHashCode();
                hash = hash * 31 + settings.albedo.GetHashCode(); hash = hash * 31 + settings.emissive.GetHashCode();
                hash = hash * 31 + settings.temporalReprojection.GetHashCode();
                hash = hash * 31 + settings.overrideLightColorsWithFogInscatteringColors.GetHashCode();
                if (settings.overrideLightColorsWithFogInscatteringColors)
                {
                    hash = hash * 31 + fogColor.GetHashCode(); hash = hash * 31 + directionalColor.GetHashCode();
                    hash = hash * 31 + skyScale.GetHashCode(); hash = hash * 31 + atmosphere.GetHashCode();
                }
                return hash;
            }
        }

        private int GatherLights(ref RenderingData data)
        {
            int count = 0, additionalIndex = 0;
            var visible = data.lightData.visibleLights;
            var tod = DawnTODSystem.Instance;
            for (int i = 0; i < visible.Length && count < MaxLights; i++)
            {
                var source = visible[i]; var light = source.light;
                bool isMain = i == data.lightData.mainLightIndex;
                int shadowIndex = isMain ? -2 : additionalIndex++;
                if (shadowIndex >= UniversalRenderPipeline.maxVisibleAdditionalLights) shadowIndex = -1;
                if (!light || !light.isActiveAndEnabled || (source.lightType != LightType.Directional &&
                    source.lightType != LightType.Point && source.lightType != LightType.Spot) ||
                    light.bakingOutput.lightmapBakeType == LightmapBakeType.Baked) continue;
                var options = light.GetComponent<DawnVolumetricFogLight>();
                bool hasOptions = options && options.isActiveAndEnabled;
                float intensity = hasOptions ? Mathf.Max(0, options.scatteringIntensity) : 1f;
                if (intensity <= 0) continue;
                if (light.shadows == LightShadows.None || (hasOptions && !options.castVolumetricShadow)) shadowIndex = -1;
                // URP does not allocate additional directional light shadows.
                if (!isMain && source.lightType == LightType.Directional) shadowIndex = -1;
                Color color = source.finalColor;
                float rawDirectionalLuminance = 0;
                Vector3 direction = -light.transform.forward;
                if (source.lightType == LightType.Directional)
                {
                    // Read unmodified components: surface-light adaptation has already divided visible colors by pi.
                    color = (QualitySettings.activeColorSpace == ColorSpace.Linear ? light.color.linear : light.color) * Mathf.Max(0, light.intensity);
                    if (light.useColorTemperature && GraphicsSettings.lightsUseColorTemperature)
                        color *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
                    rawDirectionalLuminance = color.r * 0.212639f + color.g * 0.715169f + color.b * 0.072192f;
                    if (Shader.GetGlobalFloat("_DawnAtmosphereAvailable") > 0.5f &&
                        (light == (tod ? tod.sunLight : RenderSettings.sun) || (tod && light == tod.moonLight)))
                        color *= DawnAtmosphereLightingRendererFeature.GroundTransmittance(direction);
                }
                Vector3 position = source.lightType == LightType.Directional ? direction : light.transform.position;
                float outer = Mathf.Cos(light.spotAngle * Mathf.Deg2Rad * 0.5f);
                float inner = Mathf.Cos(light.innerSpotAngle * Mathf.Deg2Rad * 0.5f);
                float spotScale = 1f / Mathf.Max(0.001f, inner - outer);
                lights[count++] = new FogLight
                {
                    positionType = new Vector4(position.x, position.y, position.z,
                        source.lightType == LightType.Directional ? 0 : source.lightType == LightType.Point ? 1 : 2),
                    colorShadow = new Vector4(color.r, color.g, color.b, shadowIndex),
                    directionRange = new Vector4(direction.x, direction.y, direction.z, 1f / Mathf.Max(0.0001f, light.range * light.range)),
                    parameters = new Vector4(spotScale, -outer * spotScale, intensity, rawDirectionalLuminance)
                };
            }
            lightBuffer ??= new ComputeBuffer(MaxLights, Marshal.SizeOf<FogLight>());
            if (count > 0) lightBuffer.SetData(lights, 0, 0, count);
            return count;
        }

        private bool UpdateStaticProbes(CameraHistory history, Vector4 gridZ)
        {
            var lightProbes = LightmapSettings.lightProbes;
            if (!lightProbes || lightProbes.count == 0 || settings.staticLightingScatteringIntensity <= 0) return false;
            var camera = history.camera;
            if (history.staticSH == null || Time.realtimeSinceStartup - history.probeTime > 0.25f ||
                Vector3.Distance(camera.transform.position, history.probeOrigin) > 1f || Quaternion.Angle(camera.transform.rotation, history.probeRotation) > 1f)
            {
                for (int z = 0; z < ProbeSize; z++)
                {
                    float depth = (Mathf.Pow(2, (z + 0.5f) * settings.gridSizeZ / (ProbeSize * gridZ.z)) - gridZ.y) / gridZ.x;
                    for (int y = 0; y < ProbeSize; y++) for (int x = 0; x < ProbeSize; x++)
                    {
                        var ray = camera.ViewportPointToRay(new Vector3((x + 0.5f) / ProbeSize, (y + 0.5f) / ProbeSize, 0));
                        float currentDepth = Vector3.Dot(ray.origin - camera.transform.position, camera.transform.forward);
                        probePositions[x + ProbeSize * (y + ProbeSize * z)] = ray.origin + ray.direction *
                            ((depth - currentDepth) / Mathf.Max(0.000001f, Vector3.Dot(ray.direction, camera.transform.forward)));
                    }
                }
                LightProbes.CalculateInterpolatedLightAndOcclusionProbes(probePositions, probes, null);
                for (int i = 0; i < probes.Length; i++) PackSH(probes[i], probeSH, i * 3);
                history.staticSH ??= new ComputeBuffer(probeSH.Length, 16);
                history.staticSH.SetData(probeSH);
                history.probeTime = Time.realtimeSinceStartup; history.probeOrigin = camera.transform.position;
                history.probeRotation = camera.transform.rotation;
            }
            return true;
        }

        private static float Halton(int index, int radix)
        {
            float result = 0, factor = 1;
            for (; index > 0; index /= radix) { factor /= radix; result += factor * (index % radix); }
            return result;
        }

        private void Vector(CommandBuffer cmd, string name, Vector4 value) => cmd.SetComputeVectorParam(shader, Shader.PropertyToID(name), value);
        private void Matrix(CommandBuffer cmd, string name, Matrix4x4 value) => cmd.SetComputeMatrixParam(shader, Shader.PropertyToID(name), value);

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            float near = Mathf.Max(camera.nearClipPlane, settings.startDistance);
            if (!IsSupported || settings.viewDistance <= near + 0.095f) { Forget(camera); return; }
            expired.Clear();
            foreach (var pair in histories)
                if (!pair.Value.camera || Time.realtimeSinceStartup - pair.Value.lastSeen > 10f) expired.Add(pair.Key);
            foreach (int key in expired) { histories[key].Dispose(); histories.Remove(key); }
            int width = Mathf.Max(1, Mathf.CeilToInt(renderingData.cameraData.cameraTargetDescriptor.width / (float)settings.gridPixelSize));
            int height = Mathf.Max(1, Mathf.CeilToInt(renderingData.cameraData.cameraTargetDescriptor.height / (float)settings.gridPixelSize));
            if (!histories.TryGetValue(camera.GetInstanceID(), out var history))
                histories.Add(camera.GetInstanceID(), history = new CameraHistory { camera = camera });
            int signature = Signature();
            var projection = camera.nonJitteredProjectionMatrix;
            bool reset = !history.valid || history.width != width || history.height != height || history.depth != settings.gridSizeZ ||
                history.signature != signature || history.projection != projection || Time.realtimeSinceStartup - history.lastSeen > 0.5f ||
                Vector3.Distance(history.origin, camera.transform.position) > Mathf.Max(1f, settings.viewDistance * 0.2f) ||
                Quaternion.Angle(history.rotation, camera.transform.rotation) > 45f;
            if (history.width != width || history.height != height || history.depth != settings.gridSizeZ || !history.integrated)
            {
                history.Dispose(); history.width = width; history.height = height; history.depth = settings.gridSizeZ;
                history.scattering[0] = CreateVolume(width, height, history.depth, "Dawn fog scattering A");
                history.scattering[1] = CreateVolume(width, height, history.depth, "Dawn fog scattering B");
                history.integrated = CreateVolume(width, height, history.depth, "Dawn integrated volumetric fog");
                if (!history.integrated || !history.scattering[0] || !history.scattering[1]) { Forget(camera); return; }
            }
            if (reset) { history.valid = false; history.resets++; history.probeTime = float.NegativeInfinity; }
            var vp = GL.GetGPUProjectionMatrix(projection, true) * camera.worldToCameraMatrix;
            var gridZ = GridZParams(near, settings.viewDistance, history.depth);
            var gridSize = new Vector4(width, height, history.depth, 1f / history.depth);
            var uvScale = new Vector4(renderingData.cameraData.cameraTargetDescriptor.width / (float)(width * settings.gridPixelSize),
                renderingData.cameraData.cameraTargetDescriptor.height / (float)(height * settings.gridPixelSize),
                (width - 0.5f) / width, (height - 0.5f) / height);
            var range = new Vector4(settings.viewDistance, settings.startDistance,
                settings.nearFadeInDistance > 0 ? 1f / settings.nearFadeInDistance : 1.0e10f, camera.orthographic ? 1 : 0);
            var origin = new Vector4(camera.transform.position.x, camera.transform.position.y, camera.transform.position.z, camera.nearClipPlane);
            int count = GatherLights(ref renderingData), write = 1 - history.read;
            bool useProbes = UpdateStaticProbes(history, gridZ);
            if (emptySH == null) { emptySH = new ComputeBuffer(3, 16); emptySH.SetData(new Vector4[3]); }
            PackSH(RenderSettings.ambientProbe, skySH, 0);
            for (int i = 0; i < 4; i++)
            {
                int index = (history.sequence + 1 + i * 8) & 1023;
                jitter[i] = settings.temporalReprojection ? new Vector4(Halton(index, 2), Halton(index, 3), Halton(index, 5), 0)
                    : new Vector4(0.5f, 0.5f, 0.5f, 0);
            }
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                foreach (var pair in new[] {
                    new KeyValuePair<string, Vector4>("_DawnFogGridSize",gridSize), new KeyValuePair<string, Vector4>("_DawnFogGridZ",gridZ),
                    new KeyValuePair<string, Vector4>("_DawnFogGridUVScale",uvScale), new KeyValuePair<string, Vector4>("_DawnFogVolumetricRange",range),
                    new KeyValuePair<string, Vector4>("_DawnFogCameraOrigin",origin),
                    new KeyValuePair<string, Vector4>("_DawnFogCameraForward",camera.transform.forward) })
                { Vector(cmd, pair.Key, pair.Value); cmd.SetGlobalVector(pair.Key, pair.Value); }
                Matrix(cmd,"_DawnFogInvViewProj",vp.inverse); Matrix(cmd,"_DawnFogWorldToClip",vp);
                cmd.SetGlobalMatrix("_DawnFogInvViewProj",vp.inverse); cmd.SetGlobalMatrix("_DawnFogWorldToClip",vp);
                Matrix(cmd,"_DawnFogPrevWorldToClip",history.valid ? history.vp : vp);
                Vector(cmd,"_DawnFogPrevOrigin",history.valid ? (Vector4)history.origin : origin);
                Vector(cmd,"_DawnFogPrevForward",history.valid ? history.forward : camera.transform.forward);
                Vector(cmd,"_DawnFogPrevGridZ",history.valid ? history.gridZ : gridZ);
                Vector(cmd,"_DawnExponentialFogParameters",primary); Vector(cmd,"_DawnExponentialFogParameters2",second);
                Vector(cmd,"_DawnFogInscattering",fogColor); Vector(cmd,"_DawnFogAtmosphere",atmosphere);
                Vector(cmd,"_DawnFogSkyAmbientScale",skyScale); Vector(cmd,"_DawnFogDirectionalColor",directionalColor);
                Vector(cmd,"_DawnFogAlbedo",QualitySettings.activeColorSpace == ColorSpace.Linear ? settings.albedo.linear : settings.albedo);
                Vector(cmd,"_DawnFogEmissive",settings.emissive);
                Vector(cmd,"_DawnFogMedium",new Vector4(settings.extinctionScale,settings.scatteringDistribution,
                    settings.staticLightingScatteringIntensity,settings.overrideLightColorsWithFogInscatteringColors ? 1 : 0));
                cmd.SetComputeVectorArrayParam(shader,Shader.PropertyToID("_DawnFogSkySH"),skySH);
                cmd.SetComputeVectorArrayParam(shader,Shader.PropertyToID("_DawnFogJitter"),jitter);
                cmd.SetComputeIntParam(shader,Shader.PropertyToID("_DawnFogLightCount"),count);
                cmd.SetComputeIntParam(shader,Shader.PropertyToID("_DawnFogStaticProbeSize"),useProbes ? ProbeSize : 0);
                cmd.SetComputeFloatParam(shader,Shader.PropertyToID("_DawnFogHistoryWeight"),history.valid && settings.temporalReprojection ? settings.historyWeight : 0);
                cmd.SetComputeBufferParam(shader,scatterKernel,"_DawnFogLights",lightBuffer);
                cmd.SetComputeBufferParam(shader,scatterKernel,"_DawnFogStaticSH",useProbes ? history.staticSH : emptySH);
                cmd.SetComputeTextureParam(shader,scatterKernel,"_DawnFogHistory",history.scattering[history.read]);
                cmd.SetComputeTextureParam(shader,scatterKernel,"_DawnFogScatteringWrite",history.scattering[write]);
                cmd.DispatchCompute(shader,scatterKernel,(width+3)/4,(height+3)/4,(history.depth+3)/4);
                cmd.SetComputeTextureParam(shader,integrateKernel,"_DawnFogScattering",history.scattering[write]);
                cmd.SetComputeTextureParam(shader,integrateKernel,"_DawnFogIntegratedWrite",history.integrated);
                cmd.DispatchCompute(shader,integrateKernel,(width+7)/8,(height+7)/8,1);
                cmd.SetGlobalTexture("_DawnIntegratedVolumetricFog",history.integrated);
                cmd.SetGlobalFloat("_DawnVolumetricFogEnabled",1);
            }
            context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            history.statistics = new Statistics { width=width,height=height,depth=history.depth,frames=++history.sequence,
                resets=history.resets,lights=count,historyUsed=history.valid && settings.temporalReprojection };
            history.read=write; history.valid=true; history.signature=signature; history.vp=vp; history.projection=projection;
            history.origin=camera.transform.position; history.forward=camera.transform.forward; history.rotation=camera.transform.rotation;
            history.gridZ=gridZ; history.lastSeen=Time.realtimeSinceStartup;
        }

        public void Dispose()
        {
            foreach (var history in histories.Values) history.Dispose(); histories.Clear();
            lightBuffer?.Dispose(); lightBuffer=null; emptySH?.Dispose(); emptySH=null;
        }
    }
}
#endif
