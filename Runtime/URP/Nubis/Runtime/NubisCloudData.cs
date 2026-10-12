using UnityEngine;

namespace NubisStorm
{
    [CreateAssetMenu(menuName = "Nubis/Cloud data", fileName = "NubisCloudData")]
    public sealed class NubisCloudData : ScriptableObject
    {
        [Tooltip("Linear RGBAHalf: min height, max height (normalized to layer), coverage, top type.")]
        public Texture2D weather;
        [Tooltip("Linear R channel: independent bottom type.")]
        public Texture2D bottomType;
        [Tooltip("Linear LUT: horizontal = type, vertical = normalized height.")]
        public Texture2D topProfile;
        public Texture2D bottomProfile;
        [Tooltip("128 cubed linear RGBA: Perlin-Worley, inverted Worley at increasing frequencies.")]
        public Texture3D shapeNoise;
        [Min(1000)] public float fieldSize = 16000;
        public int generationVersion;
        [Header("Authored, world-anchored Influence NDF")]
        public NubisInfluenceData authoredNdf;
        public NubisInfluenceData superstormNdf;
        [Header("Independent Cirrus / 2.5D sub-layer")]
        public NubisCirrusData cirrus;
        [Header("Procedural NDF generator")]
        public int weatherSeed = 193;
        [Range(2,24)] public int weatherFrequency = 5;
        [Range(-0.25f,0.25f)] public float weatherCoverageBias;
        public bool IsReady => weather && bottomType && topProfile && bottomProfile && shapeNoise;
    }

    public enum NubisDebugView
    {
        Lit, DimensionalProfile, Density, Coverage, TopType, BottomType, HeightRange,
        StormMask, NoiseComposite, AuthoredMask, CirrusCoverage, CirrusType, CirrusDensity
    }
    public enum NubisTemporalDebug
    {
        Off, CurrentSamples, RepresentativeDistance, MotionVectors, HistoryUV, HistoryRejection
    }
    public enum NubisLightingDebug
    {
        Off, Direct, PrimaryPhase, MultipleScattering, Ambient, InternalLight, Transmittance, FlashMask
    }
}
