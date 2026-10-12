using UnityEngine;
using UnityEngine.Rendering;

namespace NubisStorm
{
    // Clear fog state per camera before the height-fog pass publishes its settings.
    internal sealed class NubisDawnResetPass : UnityEngine.Rendering.Universal.ScriptableRenderPass
    {
        public NubisDawnResetPass()
        {
            renderPassEvent = UnityEngine.Rendering.Universal.RenderPassEvent.BeforeRendering;
        }

        public override void Execute(ScriptableRenderContext context,
            ref UnityEngine.Rendering.Universal.RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get("Nubis / reset camera fog");
            cmd.SetGlobalFloat("_DawnFogEnabled", 0);
            cmd.SetGlobalFloat("_DawnVolumetricFogEnabled", 0);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
