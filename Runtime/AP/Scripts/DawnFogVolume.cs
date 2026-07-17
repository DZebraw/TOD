using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DawnTOD
{
#if USING_URP
    [VolumeComponentMenu("Dawn TOD/Fog")]
#else
    [HideInInspector]
#endif
    [Serializable]
    public sealed class DawnFogVolume : VolumeComponent
    {
        [Header("Fog")]
        [Tooltip("Enables the Dawn TOD screen-space fog effect.")]
        public BoolParameter enabled = new BoolParameter(false);

        [Tooltip("Average distance, in meters, that light travels through the fog before scattering. Lower values produce denser fog.")]
        public MinFloatParameter meanFreePath = new MinFloatParameter(1000f, 0.01f);

        [Tooltip("World-space height, in meters, below which the fog reaches its full density.")]
        public FloatParameter baseHeight = new FloatParameter(0f);

        [Tooltip("Fog scattering color, equivalent to HDRP Fog Albedo.")]
        public ColorParameter albedo =
            new ColorParameter(Color.white, true, false, true);

        [Header("Advanced")]
        [Tooltip("World-space height, in meters, at which the fog density reaches zero.")]
        public FloatParameter maximumHeight = new FloatParameter(100f);

        [Tooltip("Maximum camera distance, in meters, evaluated by the post-process fog.")]
        public MinFloatParameter maximumFogDistance =
            new MinFloatParameter(5000f, 0.01f);

        [Tooltip("Applies fog to pixels that contain only the sky background.")]
        public BoolParameter affectSky = new BoolParameter(true);

        public bool IsActive()
        {
            return active && enabled.value && meanFreePath.value > 0f &&
                   maximumFogDistance.value > 0f;
        }
    }
}
