using UnityEngine;

namespace NubisStorm
{
    // Procedural instructions only. Profiles, shape noise and authored influences stay shared.
    [CreateAssetMenu(menuName="Nubis/Weather preset",fileName="WeatherPreset")]
    public sealed class NubisWeatherPreset : ScriptableObject
    {
        [Tooltip("Linear RGBA: normalized MinHeight / MaxHeight / Coverage / TopType.")]
        public Texture2D weather;
        [Tooltip("Linear R: BottomType, sampled with the same world extent as weather.")]
        public Texture2D bottomType;
        [Min(1000)] public float fieldSize=16000;
        [Tooltip("Optional Coverage/Type NDF. An empty slot uses the shared cirrus weather.")]
        public Texture2D cirrusWeather;
        [Min(1000)] public float cirrusFieldSize=48000;
        [Header("Procedural generation")]
        public int seed=193;
        [Range(2,24)] public int frequency=5;
        [Range(-.25f,.25f)] public float coverageBias;
        [Range(-.5f,.5f)] public float cirrusCoverageBias;
        public bool IsReady => weather && bottomType;

        public int Signature()
        {
            unchecked
            {
                int hash=GetInstanceID();
                hash=hash*31+fieldSize.GetHashCode(); hash=hash*31+cirrusFieldSize.GetHashCode();
                foreach(var t in new[]{weather,bottomType,cirrusWeather})
                    if(t) {hash=hash*31+t.GetInstanceID(); hash=hash*31+(int)t.updateCount;}
                return hash;
            }
        }
    }
}
