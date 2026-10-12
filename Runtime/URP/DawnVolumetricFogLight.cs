#if DAWNTOD_URP_AVAILABLE
using UnityEngine;

namespace DawnTOD
{
    [DisallowMultipleComponent, RequireComponent(typeof(Light))]
    [AddComponentMenu("MagicDawn/Volumetric Fog Light")]
    public sealed class DawnVolumetricFogLight : MonoBehaviour
    {
        [Tooltip("UE Volumetric Scattering Intensity. Lights without this component use 1.")]
        [Min(0f)] public float scatteringIntensity = 1f;
        [Tooltip("Enable this light's shadows in volumetric fog and directional height-fog scattering.")]
        public bool castVolumetricShadow = true;
    }
}
#endif
