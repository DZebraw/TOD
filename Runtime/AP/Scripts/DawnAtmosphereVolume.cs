using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DawnTOD
{
#if USING_URP
    [VolumeComponentMenu("Dawn TOD/Atmosphere")]
#else
    [HideInInspector]
#endif
    [Serializable]
    public sealed class DawnAtmosphereVolume : VolumeComponent
    {
        public const float SpaceEmissionTrackMaximum = 1000f;

        [Header("Rayleigh Scattering")]
        [Tooltip("Rayleigh scattering coefficients for the red, green, and blue channels.")]
        public Vector3Parameter rayleighCoefficients =
            new Vector3Parameter(new Vector3(5.8f, 13.5f, 33.1f));

        [Tooltip("Multiplier applied to Rayleigh in-scattering.")]
        public MinFloatParameter rayleighScatterStrength = new MinFloatParameter(1f, 0f);

        [Tooltip("Multiplier applied to Rayleigh extinction.")]
        public MinFloatParameter rayleighExtinctionStrength = new MinFloatParameter(1f, 0f);

        [Header("Mie Scattering")]
        [Tooltip("Mie scattering coefficients for the red, green, and blue channels.")]
        public Vector3Parameter mieCoefficients =
            new Vector3Parameter(new Vector3(2f, 2f, 2f));

        [Tooltip("Multiplier applied to Mie in-scattering.")]
        public MinFloatParameter mieScatterStrength = new MinFloatParameter(1f, 0f);

        [Tooltip("Multiplier applied to Mie extinction.")]
        public MinFloatParameter mieExtinctionStrength = new MinFloatParameter(1f, 0f);

        [Tooltip("Mie phase anisotropy. Higher values concentrate scattering around the light direction.")]
        public ClampedFloatParameter mieAnisotropy =
            new ClampedFloatParameter(0.625f, -1f, 1f);

        [Header("Environment")]
        [Tooltip("Color behind atmosphere rays that intersect the planet surface.")]
        public ColorParameter groundColor =
            new ColorParameter(Color.black, true, false, true);

        [Header("Space")]
        [Tooltip("HDR cubemap rendered behind the atmosphere.")]
        public CubemapParameter spaceEmissionTexture = new CubemapParameter(null);

        [Tooltip("Star emission track value. URP maps 0-1000 to a 0-1 shader multiplier.")]
        public ClampedFloatParameter spaceEmission =
            new ClampedFloatParameter(SpaceEmissionTrackMaximum, 0f, SpaceEmissionTrackMaximum);

        [Tooltip("Euler rotation applied to the space emission cubemap.")]
        public Vector3Parameter spaceRotation = new Vector3Parameter(Vector3.zero);

        [Header("Density")]
        [Tooltip("Rayleigh density scale height in meters.")]
        public MinFloatParameter rayleighDensityScale = new MinFloatParameter(7994f, 0f);

        [Tooltip("Mie density scale height in meters.")]
        public MinFloatParameter mieDensityScale = new MinFloatParameter(1200f, 0f);

        [Header("Sun Disk")]
        [Tooltip("Sun disk intensity multiplier.")]
        public MinFloatParameter sunDiskScale = new MinFloatParameter(0.75f, 0f);

        [Tooltip("Mie phase anisotropy used by the sun disk.")]
        public ClampedFloatParameter sunMieAnisotropy =
            new ClampedFloatParameter(0.99f, -1f, 1f);
    }
}
