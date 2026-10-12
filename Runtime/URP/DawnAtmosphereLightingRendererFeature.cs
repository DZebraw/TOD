#if DAWNTOD_URP_AVAILABLE
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DawnTOD
{
    // Adapt outer-space illuminance to URP's unnormalised surface BRDF before
    // ForwardLights uploads main/additional light buffers. Light components stay
    // unchanged so sky, clouds and fog continue to consume outer-space values.
    public sealed class DawnAtmosphereLightingRendererFeature : ScriptableRendererFeature
    {
        public override void Create() { }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (Shader.GetGlobalFloat("_DawnAtmosphereAvailable") < .5f ||
                renderingData.cameraData.cameraType == CameraType.Preview) return;
#if UNITY_EDITOR
            if (renderingData.cameraData.isSceneViewCamera && UnityEditor.SceneView.currentDrawingSceneView != null &&
                !UnityEditor.SceneView.currentDrawingSceneView.sceneLighting) return;
#endif
            var tod = DawnTODSystem.Instance;
            Light sun = tod ? tod.sunLight : RenderSettings.sun;
            Light moon = tod ? tod.moonLight : null;
            var lights = renderingData.lightData.visibleLights;
            for (int i = 0; i < lights.Length; i++)
            {
                var visible = lights[i];
                if (visible.lightType != LightType.Directional || !visible.light ||
                    (visible.light != sun && visible.light != moon)) continue;
                Color transmission = GroundTransmittance(-visible.light.transform.forward);
                Color color = visible.finalColor;
                // UE's Lambert BRDF includes 1/pi; URP's BRDFData.diffuse does not.
                // Its specular approximation uses the same light-unit convention.
                color.r *= transmission.r / Mathf.PI;
                color.g *= transmission.g / Mathf.PI;
                color.b *= transmission.b / Mathf.PI;
                visible.finalColor = color;
                lights[i] = visible;
            }
        }

        // Same Beer-Lambert density integral and 500 m reference as the cloud
        // ground-transmittance mode. CPU evaluation avoids GPU readback latency.
        public static Color GroundTransmittance(Vector3 direction)
        {
            double radius = Shader.GetGlobalFloat("_PlanetRadius");
            double topHeight = Shader.GetGlobalFloat("_AtmosphereHeight");
            Vector4 scale = Shader.GetGlobalVector("_DensityScaleHeight");
            Vector4 rayleigh = Shader.GetGlobalVector("_ExtinctionR");
            Vector4 mie = Shader.GetGlobalVector("_ExtinctionM");
            if (radius <= 0 || topHeight <= 0) return Color.white;
            const double height = 500;
            double r = radius + height, mu = Math.Max(-1, Math.Min(1,direction.normalized.y));
            if (mu < -Math.Sqrt(height*(2*radius+height))/r) return Color.black;
            double top = radius + topHeight;
            double length = -r*mu + Math.Sqrt(r*r*mu*mu + top*top-r*r);
            if (length <= 0) return Color.white;
            const int samples = 256;
            double step = length / samples, opticalR = 0, opticalM = 0;
            for (int i = 0; i < samples; i++)
            {
                double t = (i+.5)*step;
                double altitude = Math.Max(0,Math.Sqrt(r*r+t*t+2*r*t*mu)-radius);
                opticalR += Math.Exp(-altitude/Math.Max(.001,scale.x))*step;
                opticalM += Math.Exp(-altitude/Math.Max(.001,scale.y))*step;
            }
            return new Color((float)Math.Exp(-opticalR*rayleigh.x-opticalM*mie.x),
                (float)Math.Exp(-opticalR*rayleigh.y-opticalM*mie.y),
                (float)Math.Exp(-opticalR*rayleigh.z-opticalM*mie.z),1);
        }
    }
}
#endif
