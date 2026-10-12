using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NubisStorm
{
    public sealed class NubisStormRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] Shader stormShader;
        StormPass pass;
        Material material;
        readonly NubisDawnResetPass resetPass = new NubisDawnResetPass();

        public struct TemporalStatistics
        {
            public int width, height, workWidth, workHeight, phase, frames, resets;
            public bool historyUsed;
            public string resetReason;
        }
        static readonly Dictionary<int, TemporalStatistics> statistics = new Dictionary<int, TemporalStatistics>();
        public static bool TryGetStatistics(Camera camera, out TemporalStatistics value) => statistics.TryGetValue(camera.GetInstanceID(),out value);

        public override void Create()
        {
            pass?.Dispose();
            pass = null;
            CoreUtils.Destroy(material);
            if (!stormShader) stormShader = Shader.Find("Hidden/NubisStorm/Volume");
            if (!stormShader) return;
            material = CoreUtils.CreateEngineMaterial(stormShader);
            pass = new StormPass(material) { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(resetPass);
            var cloud = NubisStormVolume.Active;
            var camera = renderingData.cameraData;
            if (pass == null || !cloud || !cloud.IsReady || camera.isPreviewCamera ||
                camera.cameraType == CameraType.Reflection || camera.renderType == CameraRenderType.Overlay) return;
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
        }

        sealed class CameraHistory
        {
            public Camera camera;
            public RTHandle workColor, workMeta, debug;
            public readonly RTHandle[] color = new RTHandle[2], meta = new RTHandle[2];
            public int width, height, read, phase, sequence, frames, resets, signature;
            public bool valid, temporal;
            public float time, lastSeen, weatherBlend;
            public Matrix4x4 vp, projection;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 sunDirection, moonDirection;
            public Color sunRadiance, moonRadiance;
            public void Dispose()
            {
                workColor?.Release(); workMeta?.Release(); debug?.Release();
                for (int i=0;i<2;i++) { color[i]?.Release(); meta[i]?.Release(); }
                workColor=workMeta=debug=null;
                color[0]=color[1]=meta[0]=meta[1]=null;
            }
        }

        sealed class StormPass : ScriptableRenderPass
        {
            readonly Material material;
            readonly Dictionary<int,CameraHistory> histories = new Dictionary<int,CameraHistory>();
            readonly List<int> expired = new List<int>();
            readonly ProfilingSampler integration = new ProfilingSampler("Nubis / shell integration");
            readonly ProfilingSampler reconstruction = new ProfilingSampler("Nubis / temporal resolve");
            readonly ProfilingSampler composition = new ProfilingSampler("Nubis / composite");
            // Every offset in a 4x4 tile occurs exactly once. Bayer order spreads early samples spatially.
            static readonly Vector2Int[] phases = {
                new Vector2Int(0,0),new Vector2Int(2,2),new Vector2Int(2,0),new Vector2Int(0,2),
                new Vector2Int(1,1),new Vector2Int(3,3),new Vector2Int(3,1),new Vector2Int(1,3),
                new Vector2Int(1,0),new Vector2Int(3,2),new Vector2Int(3,0),new Vector2Int(1,2),
                new Vector2Int(0,1),new Vector2Int(2,3),new Vector2Int(2,1),new Vector2Int(0,3)
            };
            public StormPass(Material material) => this.material=material;

            static void Allocate(ref RTHandle handle, RenderTextureDescriptor descriptor, int width, int height, GraphicsFormat format, string name)
            {
                descriptor.width=width; descriptor.height=height;
                descriptor.msaaSamples=1; descriptor.depthBufferBits=0; descriptor.depthStencilFormat=GraphicsFormat.None;
                descriptor.graphicsFormat=format; descriptor.useMipMap=false; descriptor.autoGenerateMips=false;
                descriptor.enableRandomWrite=false; descriptor.bindMS=false;
                RenderingUtils.ReAllocateIfNeeded(ref handle,descriptor,FilterMode.Point,TextureWrapMode.Clamp,name:name);
            }
            CameraHistory GetHistory(ref RenderingData data, NubisStormVolume cloud)
            {
                var camera=data.cameraData.camera;
                int id=camera.GetInstanceID();
                if (!histories.TryGetValue(id,out var h) || h.camera!=camera)
                {
                    h?.Dispose();
                    h=new CameraHistory {camera=camera};
                    histories[id]=h;
                }
                var descriptor=data.cameraData.cameraTargetDescriptor;
                int width=Mathf.Max(1,Mathf.RoundToInt(descriptor.width*Mathf.Clamp(cloud.resolutionScale,0.25f,1)));
                int height=Mathf.Max(1,Mathf.RoundToInt(descriptor.height*Mathf.Clamp(cloud.resolutionScale,0.25f,1)));
                bool temporal=cloud.temporalReconstruction &&
                    (camera.cameraType==CameraType.Game || camera.cameraType==CameraType.SceneView) && !camera.orthographic
                    && cloud.debugView==NubisDebugView.Lit && cloud.lightingDebug==NubisLightingDebug.Off;
                if (h.width!=width || h.height!=height || h.temporal!=temporal)
                {
                    h.Dispose(); h.valid=false; h.phase=0;
                    h.width=width; h.height=height; h.temporal=temporal;
                }
                for(int i=0;i<(temporal ? 2 : 1);i++)
                {
                    Allocate(ref h.color[i],descriptor,width,height,GraphicsFormat.R16G16B16A16_SFloat,"Nubis history color "+i);
                    // Distances up to 240 km cannot be stored in FP16.
                    Allocate(ref h.meta[i],descriptor,width,height,GraphicsFormat.R32G32B32A32_SFloat,"Nubis history distance/confidence/flash "+i);
                }
                Allocate(ref h.debug,descriptor,width,height,GraphicsFormat.R8G8B8A8_UNorm,"Nubis temporal diagnostics");
                if (temporal)
                {
                    Allocate(ref h.workColor,descriptor,(width+3)/4,(height+3)/4,GraphicsFormat.R16G16B16A16_SFloat,"Nubis sparse color");
                    Allocate(ref h.workMeta,descriptor,(width+3)/4,(height+3)/4,GraphicsFormat.R32G32B32A32_SFloat,"Nubis sparse metadata");
                }
                return h;
            }
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
            {
                GetHistory(ref data,NubisStormVolume.Active);
            }
            static bool ProjectionChanged(Matrix4x4 a, Matrix4x4 b)
            {
                for(int i=0;i<16;i++) if (Mathf.Abs(a[i]-b[i])>0.0001f) return true;
                return false;
            }
            static bool LightChanged(Light light, Vector3 previousDirection, Color previousRadiance)
            {
                Vector3 direction=light ? -light.transform.forward : Vector3.up;
                Color radiance=NubisStormVolume.LightRadiance(light);
                Color delta=radiance-previousRadiance;
                float difference=Mathf.Max(Mathf.Abs(delta.r),Mathf.Abs(delta.g),Mathf.Abs(delta.b));
                return Vector3.Angle(direction,previousDirection)>1f ||
                    difference>Mathf.Max(.001f,previousRadiance.maxColorComponent*.05f);
            }
            static void Targets(CommandBuffer cmd, RTHandle color, RTHandle meta, RTHandle debug=null)
            {
                var targets=debug!=null ? new[] {color.nameID,meta.nameID,debug.nameID} : new[] {color.nameID,meta.nameID};
                cmd.SetRenderTarget(targets,new RenderTargetIdentifier(BuiltinRenderTextureType.None));
                cmd.SetViewport(new Rect(0,0,color.rt.width,color.rt.height));
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var cloud=NubisStormVolume.Active;
                if (!cloud || !cloud.IsReady) return;
                var camera=data.cameraData.camera;
                var h=GetHistory(ref data,cloud);
                var vp=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix;
                float time=cloud.AnimationTime, delta=time-h.time, now=Time.realtimeSinceStartup;
                int signature=cloud.HistorySignature();
                cloud.ResolveLights(out var sunlight,out var moonlight);
                string reason=null;
                if (!h.temporal) reason="Temporal disabled, unsupported/orthographic camera or density diagnostic";
                else if (!h.valid) reason="New camera, resized target or mode change";
                else if (h.signature!=signature) reason="Cloud data, weather or lighting changed";
                else if (LightChanged(sunlight,h.sunDirection,h.sunRadiance) || LightChanged(moonlight,h.moonDirection,h.moonRadiance))
                    reason="Directional light jump";
                // A changed field can create a cloud where an untraced history pixel is clear.
                // Refresh all rays during a fade, then resume sparse updates as soon as it settles.
                else if (Mathf.Abs(h.weatherBlend-cloud.EffectiveWeatherBlend)>0.000001f) reason="Weather field blend changed";
                else if (delta<0 || delta>0.25f) reason="Time discontinuity";
                else if (Vector3.Distance(camera.transform.position,h.position)>250 ||
                    Quaternion.Angle(camera.transform.rotation,h.rotation)>25 || ProjectionChanged(camera.projectionMatrix,h.projection))
                    reason="Camera cut or projection changed";
                bool reuse=reason==null;
                if (!reuse) { h.phase=0; h.sequence=0; h.resets++; }
                int phase=h.phase;
                var offset=phases[phase];
                cloud.Apply(material);
                material.SetMatrix("_StormInvVP",vp.inverse);
                material.SetMatrix("_CloudPreviousVP",reuse ? h.vp : vp);
                material.SetVector("_StormCameraPosition",camera.transform.position);
                material.SetVector("_CloudPreviousCameraPosition",reuse ? h.position : camera.transform.position);
                material.SetVector("_StormCameraForward",camera.transform.forward);
                material.SetFloat("_StormOrthographic",camera.orthographic ? 1 : 0);
                material.SetVector("_CloudTargetSize",new Vector4(h.width,h.height,1f/h.width,1f/h.height));
                material.SetVector("_CloudFootprint",new Vector4(2f/(Mathf.Max(0.0001f,Mathf.Abs(camera.projectionMatrix.m11))*h.height),0,0,0));
                material.SetFloat("_CirrusPixelSize",2f/(Mathf.Max(.0001f,Mathf.Abs(camera.projectionMatrix.m11))*Mathf.Max(1,data.cameraData.cameraTargetDescriptor.height)));
                material.SetVector("_CloudWorkSize",new Vector4((h.width+3)/4,(h.height+3)/4,4f/h.width,4f/h.height));
                material.SetVector("_CloudPhase",new Vector4(offset.x,offset.y,h.sequence,reuse ? 4 : 1));
                material.SetVector("_CloudTemporal",new Vector4(reuse ? delta : 0,cloud.historyWeight,(int)cloud.temporalDebug,reuse ? 1 : 0));

                int write=reuse ? 1-h.read : 0;
                var cmd=CommandBufferPool.Get("Nubis spherical sky");
                using(new ProfilingScope(cmd,integration))
                {
                    if (reuse) Targets(cmd,h.workColor,h.workMeta);
                    else Targets(cmd,h.color[write],h.meta[write],h.debug);
                    CoreUtils.DrawFullScreen(cmd,material,shaderPassId:0);
                }
                if (reuse)
                {
                    using(new ProfilingScope(cmd,reconstruction))
                    {
                        cmd.SetGlobalTexture("_CloudCurrent",h.workColor.nameID);
                        cmd.SetGlobalTexture("_CloudCurrentMeta",h.workMeta.nameID);
                        cmd.SetGlobalTexture("_CloudHistory",h.color[h.read].nameID);
                        cmd.SetGlobalTexture("_CloudHistoryMeta",h.meta[h.read].nameID);
                        Targets(cmd,h.color[write],h.meta[write],h.debug);
                        CoreUtils.DrawFullScreen(cmd,material,shaderPassId:1);
                    }
                }
                using(new ProfilingScope(cmd,composition))
                {
                    cmd.SetGlobalTexture("_CloudResolved",h.color[write].nameID);
                    cmd.SetGlobalTexture("_CloudResolvedMeta",h.meta[write].nameID);
                    cmd.SetGlobalTexture("_CloudDiagnostic",h.debug.nameID);
                    CoreUtils.SetRenderTarget(cmd,data.cameraData.renderer.cameraColorTargetHandle);
                    CoreUtils.DrawFullScreen(cmd,material,shaderPassId:2);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                h.valid=h.temporal; h.read=write; h.signature=signature;
                h.vp=vp; h.projection=camera.projectionMatrix;
                h.position=camera.transform.position; h.rotation=camera.transform.rotation;
                h.sunDirection=sunlight ? -sunlight.transform.forward : Vector3.up;
                h.moonDirection=moonlight ? -moonlight.transform.forward : Vector3.up;
                h.sunRadiance=NubisStormVolume.LightRadiance(sunlight);
                h.moonRadiance=NubisStormVolume.LightRadiance(moonlight);
                h.time=time; h.lastSeen=now; h.weatherBlend=cloud.EffectiveWeatherBlend;
                h.phase=reuse ? (phase+1)%16 : 0; h.frames++; h.sequence++;
                statistics[camera.GetInstanceID()]=new TemporalStatistics {
                    width=h.width,height=h.height,workWidth=reuse ? (h.width+3)/4 : h.width,
                    workHeight=reuse ? (h.height+3)/4 : h.height,phase=phase,frames=h.frames,resets=h.resets,
                    historyUsed=reuse,resetReason=reason ?? "None"
                };
                expired.Clear();
                foreach(var pair in histories)
                    if (!pair.Value.camera || now-pair.Value.lastSeen>5) expired.Add(pair.Key);
                foreach(int id in expired) { histories[id].Dispose(); histories.Remove(id); statistics.Remove(id); }
            }
            public void Dispose()
            {
                foreach(var pair in histories) { pair.Value.Dispose(); statistics.Remove(pair.Key); }
                histories.Clear();
            }
        }
    }
}
