using System;
#if DAWNTOD_URP_AVAILABLE
using UnityEngine;
using UnityEngine.Rendering;

namespace DawnTOD
{
    [HideInInspector, Serializable]
    public sealed class DawnFogVolume : VolumeComponent
    {
        [Serializable]
        public sealed class VolumetricSettingsParameter : VolumeParameter<DawnVolumetricFogSettings>
        {
            public VolumetricSettingsParameter(DawnVolumetricFogSettings value) : base(value, false) { }
        }

        public VolumetricSettingsParameter volumetricFog = new VolumetricSettingsParameter(DawnVolumetricFogSettings.Default);
        public BoolParameter enabled = new BoolParameter(false);
        [Tooltip("UE Fog Density artist value. Internally divided by 1000 per centimeter.")]
        public ClampedFloatParameter density = new ClampedFloatParameter(0.02f, 0f, 10f);
        [Tooltip("UE Fog Height Falloff artist value. Density halves every 10 / value meters.")]
        public ClampedFloatParameter heightFalloff = new ClampedFloatParameter(0.2f, 0f, 2f);
        [Tooltip("Fog actor world height in meters; supplied by DawnTODSystem.transform.position.y.")]
        public FloatParameter height = new FloatParameter(0f);
        public BoolParameter secondFogEnabled = new BoolParameter(false);
        public ClampedFloatParameter secondFogDensity = new ClampedFloatParameter(0f, 0f, 10f);
        public ClampedFloatParameter secondFogHeightFalloff = new ClampedFloatParameter(0.2f, 0f, 2f);
        [Tooltip("Second fog layer height offset in meters, relative to the primary fog height.")]
        public FloatParameter secondFogHeightOffset = new FloatParameter(0f);
        public ColorParameter inscatteringColor = new ColorParameter(Color.black, true, false, true);
        [Tooltip("Add sky ambient and atmospheric Sun/Moon scattering independently of Fog Inscattering Color, like UE.")]
        public BoolParameter atmosphereLighting = new BoolParameter(true);
        public ColorParameter skyAtmosphereAmbientContributionColorScale = new ColorParameter(Color.white, true, false, true);
        public MinFloatParameter heightFogContribution = new MinFloatParameter(1f, 0f);
        public ColorParameter directionalInscatteringColor = new ColorParameter(Color.black, true, false, true);
        public ClampedFloatParameter directionalInscatteringExponent = new ClampedFloatParameter(4f, 0.000001f, 1000f);
        public MinFloatParameter directionalInscatteringStartDistance = new MinFloatParameter(100f, 0f);
        public ClampedFloatParameter maxOpacity = new ClampedFloatParameter(1f, 0f, 1f);
        public MinFloatParameter startDistance = new MinFloatParameter(0f, 0f);
        public MinFloatParameter endDistance = new MinFloatParameter(0f, 0f);
        public MinFloatParameter cutoffDistance = new MinFloatParameter(0f, 0f);

        public bool IsActive() => active && enabled.value &&
            (density.value > 0f || (secondFogEnabled.value && secondFogDensity.value > 0f));
    }
}
#endif
