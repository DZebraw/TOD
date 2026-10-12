using UnityEngine;
using UnityEngine.Rendering;

namespace NubisStorm
{
    // Kept as the existing scene component type; both sky and storm now use this model.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class NubisStormVolume : MonoBehaviour
    {
        static NubisStormVolume active;
        public static NubisStormVolume Active
        {
            get
            {
                // Saving/importing assets in edit mode can invalidate an ExecuteAlways registration.
                if (!active || !active.isActiveAndEnabled)
                {
                    active = null;
                    foreach (var candidate in FindObjectsOfType<NubisStormVolume>())
                        if (candidate.isActiveAndEnabled && candidate.gameObject.scene.isLoaded) { active = candidate; break; }
                }
                return active;
            }
        }
        [Header("Shared sky model · NDF + profiles + Perlin-Worley")]
        public NubisCloudData cloudData;
        [Tooltip("Lower cloud layer thickness in metres. Larger values also lift cirrus when needed to keep the layers separated.")]
        [Min(100)] public float height = 4200;
        [Range(0, 2)] public float coverage = 1;
        [Range(-1, 1)] public float topTypeBias;
        [Range(-1, 1)] public float bottomTypeBias;
        [Range(0, 3)] public float density = 1;
        [Min(100)] public float noiseScale = 2600;
        [Range(0, 1)] public float erosion = 0.23f;
        public Vector3 worleyWeights = new Vector3(0.625f, 0.25f, 0.125f);
        public Vector2 windVelocity = new Vector2(12, 4);
        public Vector2 fieldVelocity = Vector2.zero;
        [Range(0,1)] public float authoredInfluenceStrength = 1;
        [Header("Weather fields (empty preset = shared base weather)")]
        public NubisWeatherPreset weatherFrom, weatherTo;
        [Range(0,1)] public float weatherBlend;
        [Min(0)] public float weatherTransitionSeconds=12;
        bool transitioning, hasQueuedWeather;
        NubisWeatherPreset queuedWeather;
        float transitionStart, transitionStartBlend, transitionDuration, queuedDuration;
        public float EffectiveWeatherBlend => Mathf.Clamp01(weatherBlend);
        public bool IsWeatherTransitioning => transitioning;
        [Header("Independent Cirrus / 2.5D sub-layer")]
        public bool enableCirrus=true;
        [Tooltip("Altitude above planet ground. Kept at least 256 m above the lower cloud layer.")]
        [Min(100)] public float cirrusAltitude=7600;
        [Range(0,2)] public float cirrusCoverage=1;
        [Range(-1,1)] public float cirrusTypeBias;
        [Range(0,3)] public float cirrusOpticalDepth=.65f;
        [Range(0,1)] public float cirrusInfluenceStrength=1;
        public Vector2 cirrusFieldVelocity=new Vector2(9,-3);
        public Vector2 cirrusWindVelocity=new Vector2(24,7);
        public float EffectiveCirrusAltitude => Mathf.Max(cirrusAltitude,transform.position.y+height+256);
        [Header("Spherical sky (metres)")]
        public bool sphericalSky = true;
        [Min(10000)] public float planetRadius = 6371000;
        [Min(1000)] public float maxTraceDistance = 240000;
        [Tooltip("Slide 39 distance step is capped here before the finite iteration budget is applied.")]
        [Min(10)] public float maxStepLength = 800;
        [Header("Local superstorm NDF influence (0 = original sky)")]
        [Range(0, 1)] public float stormStrength = 1;
        public NubisInfluenceData stormNdf;
        public NubisInfluenceData StormNdf => stormNdf ? stormNdf : (cloudData ? cloudData.superstormNdf : null);
        public Vector2 stormOffset;
        [Min(100)] public float radius = 3400;
        [Range(0, 1)] public float anvilStrength = 0.7f;
        [Header("Six overlapping noise bands")]
        [Range(-3, 3)] public float rotationSpeed = 0.45f;
        [Range(0, 2)] public float spiralStrength = 0.35f;
        public bool animate = true;
        public float frozenTime = 18;
        [Header("Shared lighting")]
        public Light sun;
        public Light moon;
        [Tooltip("Use Dawn TOD's Sun and Moon when references are not assigned. Both lights contribute during a twilight overlap.")]
        public bool useDawnLights = true;
        [Tooltip("Apply Dawn atmospheric transmittance to both lights, including authored clouds and cirrus.")]
        public bool atmosphereTransmittance = true;
        [Tooltip("UE default: off, using shared near-ground atmospheric light color. On: integrate transmittance at each cloud sample altitude; higher clouds can be less red.")]
        public bool perSampleAtmosphereTransmittance;
        [Tooltip("Use atmospheric sky irradiance for cloud ambient lighting. Ambient Color remains the fallback without Dawn atmosphere.")]
        public bool atmosphereAmbient = true;
        public Color ambientColor = new Color(0.27f, 0.37f, 0.49f);
        [Min(1)] public float sunTraceDistance=256;
        [Range(-.95f,.95f)] public float primaryAnisotropy=.2f;
        [Range(-.95f,.95f)] public float silverAnisotropy=.75f;
        [Range(0,1)] public float silverWeight=.15f;
        [Range(-.95f,.95f)] public float secondaryAnisotropy;
        [Min(0)] public float directIntensity=1;
        [Min(0)] public float multipleScatteringIntensity=.65f;
        [Min(0)] public float ambientIntensity=1;
        [Tooltip("P54 profile probability uses this reference length in metres, independent of the ray iteration budget.")]
        [Min(.01f)] public float multipleScatteringProfileLength=3;
        [Min(0)] public float multipleScatteringDepthPower=.22f;
        [Min(0)] public float multipleScatteringHeightPower=.5f;
        [Min(1)] public float stormExtinctionScale=1.5f;
        [Range(0,1)] public float stormAmbientDimmer=.42f;
        [ColorUsage(false, true)] public Color coreColor = new Color(1, 0.21f, 0.055f);
        [Range(0, 12)] public float coreIntensity = 4;
        [Range(0, 6)] public float lightningIntensity = 1.5f;
        [Header("Rendering / diagnostic stages")]
        public NubisDebugView debugView;
        public NubisLightingDebug lightingDebug;
        [Range(64, 512)] public int viewSteps = 320;
        [Range(2, 10)] public int lightSteps = 10;
        [Range(0.25f, 1)] public float resolutionScale = 1;
        [Range(0.2f, 3)] public float extinction = 1;
        [Header("4 x 4 temporal reconstruction")]
        public bool temporalReconstruction = true;
        [Range(0, 0.99f)] public float historyWeight = 0.97f;
        public NubisTemporalDebug temporalDebug;
        [Tooltip("Explicitly increment when modifying weather textures in-place without changing their identity.")]
        public int historyRevision;

        public void InvalidateHistory() => historyRevision++;

        // Requests during a fade are queued; changing the destination mid-blend would pop the fields.
        // Null is a valid destination and returns to the shared base weather.
        public bool TransitionToWeather(NubisWeatherPreset target, float seconds)
        {
            if(target && !target.IsReady) return false;
            seconds=Mathf.Max(0,seconds);
            if(EffectiveWeatherBlend>0 && EffectiveWeatherBlend<1 && target!=weatherTo)
            {
                queuedWeather=target; queuedDuration=seconds; hasQueuedWeather=true;
                if(!transitioning) StartWeatherFade(weatherTransitionSeconds);
                return true;
            }
            hasQueuedWeather=false;
            if(EffectiveWeatherBlend>=1) {weatherFrom=weatherTo; weatherBlend=0;}
            weatherTo=target;
            StartWeatherFade(seconds);
            return true;
        }
        void StartWeatherFade(float seconds)
        {
            transitionStart=Time.time; transitionStartBlend=EffectiveWeatherBlend;
            transitionDuration=Mathf.Max(0,seconds); transitioning=transitionDuration>0;
            if(!transitioning) {weatherBlend=1; FinishWeatherFade();}
        }
        void Update()
        {
            if(!Application.isPlaying || !transitioning) return;
            AdvanceWeatherTransition(Time.time-transitionStart);
        }
        // Elapsed seconds are relative to the current fade; useful for deterministic simulation.
        public void AdvanceWeatherTransition(float elapsedSeconds)
        {
            if(!transitioning) return;
            float progress=Mathf.Clamp01(elapsedSeconds/Mathf.Max(.0001f,transitionDuration));
            weatherBlend=Mathf.Lerp(transitionStartBlend,1,Mathf.SmoothStep(0,1,progress));
            if(progress<1) return;
            FinishWeatherFade();
        }
        void FinishWeatherFade()
        {
            transitioning=false;
            if(hasQueuedWeather)
            {
                var next=queuedWeather; float seconds=queuedDuration; hasQueuedWeather=false;
                TransitionToWeather(next,seconds);
            }
        }

        public int WeatherSignature()
        {
            unchecked {return (weatherFrom && weatherFrom.IsReady ? weatherFrom.Signature() : 0)*397
                ^(weatherTo && weatherTo.IsReady ? weatherTo.Signature() : 0);}
        }

        // Cirrus is rendered outside the lower-layer history; this signature refreshes editor maps only.
        public int CirrusSignature()
        {
            unchecked
            {
                int hash=cloudData && cloudData.cirrus ? cloudData.cirrus.Signature() : 0;
                foreach(float v in new[]{EffectiveCirrusAltitude,cirrusCoverage,cirrusTypeBias,cirrusOpticalDepth,cirrusInfluenceStrength}) hash=hash*31+v.GetHashCode();
                return hash^cirrusFieldVelocity.GetHashCode()^cirrusWindVelocity.GetHashCode()^(enableCirrus ? 397 : 0)
                    ^WeatherSignature()^EffectiveWeatherBlend.GetHashCode();
            }
        }

        public int HistorySignature()
        {
            unchecked
            {
                int hash = 17;
                hash = hash*31 + GetInstanceID();
                hash = hash*31 + historyRevision;
                hash = hash*31 + WeatherSignature();
                hash = hash*31 + transform.position.GetHashCode();
                hash = hash*31 + (cloudData ? cloudData.GetInstanceID() : 0);
                hash = hash*31 + (cloudData && cloudData.authoredNdf ? cloudData.authoredNdf.Signature() : 0);
                hash = hash*31 + (StormNdf ? StormNdf.Signature() : 0);
                hash = hash*31 + authoredInfluenceStrength.GetHashCode();
                if (cloudData)
                {
                    hash = hash*31+cloudData.generationVersion;
                    hash = hash*31+cloudData.fieldSize.GetHashCode();
                    foreach (var texture in new Texture[] { cloudData.weather,cloudData.bottomType,cloudData.topProfile,cloudData.bottomProfile,cloudData.shapeNoise })
                        if (texture) { hash=hash*31+texture.GetInstanceID(); hash=hash*31+(int)texture.updateCount; }
                }
                foreach (float value in new[] { height,coverage,topTypeBias,bottomTypeBias,density,noiseScale,erosion,
                    planetRadius,maxTraceDistance,maxStepLength,stormStrength,radius,anvilStrength,
                    rotationSpeed,spiralStrength,extinction,coreIntensity,lightningIntensity,historyWeight }) hash=hash*31+value.GetHashCode();
                foreach(float value in new[]{sunTraceDistance,primaryAnisotropy,silverAnisotropy,silverWeight,
                    secondaryAnisotropy,directIntensity,multipleScatteringIntensity,ambientIntensity,
                    multipleScatteringProfileLength,multipleScatteringDepthPower,multipleScatteringHeightPower,
                    stormExtinctionScale,stormAmbientDimmer}) hash=hash*31+value.GetHashCode();
                hash=hash*31+worleyWeights.GetHashCode(); hash=hash*31+windVelocity.GetHashCode();
                hash=hash*31+fieldVelocity.GetHashCode(); hash=hash*31+stormOffset.GetHashCode();
                hash=hash*31+ambientColor.GetHashCode(); hash=hash*31+coreColor.GetHashCode();
                hash=hash*31+(sphericalSky ? 1 : 0); hash=hash*31+(int)debugView;
                hash=hash*31+viewSteps; hash=hash*31+lightSteps;
                hash=hash*31+(int)lightingDebug;
                ResolveLights(out var sunlight, out var moonlight);
                hash=hash*31+LightSignature(sunlight); hash=hash*31+LightSignature(moonlight);
                hash=hash*31+atmosphereTransmittance.GetHashCode();
                hash=hash*31+perSampleAtmosphereTransmittance.GetHashCode();
                hash=hash*31+atmosphereAmbient.GetHashCode();
                hash=hash*31+Shader.GetGlobalVector("_ExtinctionR").GetHashCode();
                hash=hash*31+Shader.GetGlobalVector("_ExtinctionM").GetHashCode();
                hash=hash*31+Shader.GetGlobalVector("_DensityScaleHeight").GetHashCode();
                hash=hash*31+Shader.GetGlobalFloat("_DawnAtmosphereAvailable").GetHashCode();
                return hash;
            }
        }

        public float AnimationTime => animate && Application.isPlaying ? Time.time + frozenTime : frozenTime;
        public bool IsReady => cloudData && cloudData.IsReady;
        void Reset()
        {
            cloudData = Resources.Load<NubisCloudData>("DawnTOD/NubisCloud/Data/SharedCloudData");
            var tod = DawnTOD.DawnTODSystem.Instance;
            sun = tod ? tod.sunLight : RenderSettings.sun;
            moon = tod ? tod.moonLight : null;
            stormStrength = 0;
        }
        void OnEnable() => active = this;
        void OnDisable() { if (active == this) active = null; }

        public void ResolveLights(out Light sunlight, out Light moonlight)
        {
            var tod = useDawnLights ? DawnTOD.DawnTODSystem.Instance : null;
            sunlight = sun ? sun : tod ? tod.sunLight : RenderSettings.sun;
            moonlight = moon ? moon : tod ? tod.moonLight : null;
            if (moonlight == sunlight) moonlight = null;
        }

        public Light MainLight
        {
            get
            {
                ResolveLights(out var sunlight, out var moonlight);
                var tod=useDawnLights ? DawnTOD.DawnTODSystem.Instance : null;
                var main=tod ? tod.GetMainDirectionalLight() : RenderSettings.sun;
                if (main && main.isActiveAndEnabled && (main==sunlight || main==moonlight)) return main;
                return LightRadiance(sunlight).maxColorComponent >= LightRadiance(moonlight).maxColorComponent
                    ? sunlight : moonlight;
            }
        }

        internal static Color LightRadiance(Light light)
        {
            if (!light || !light.isActiveAndEnabled || light.type != LightType.Directional) return new Color(0,0,0,0);
            Color color = QualitySettings.activeColorSpace == ColorSpace.Linear ? light.color.linear : light.color;
            if (light.useColorTemperature && GraphicsSettings.lightsUseColorTemperature)
                color *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
            color *= Mathf.Max(0,light.intensity);
            color.a = 0;
            return color;
        }

        static int LightSignature(Light light)
        {
            if (!light) return 0;
            // Continuous TOD motion must not invalidate all 16 temporal tiles every frame.
            // The renderer separately rejects abrupt direction/radiance changes.
            unchecked { return light.GetInstanceID()*397 ^ light.isActiveAndEnabled.GetHashCode(); }
        }

        public void Apply(Material material)
        {
            if (!IsReady) return;
            var from=weatherFrom && weatherFrom.IsReady ? weatherFrom : null;
            var to=weatherTo && weatherTo.IsReady ? weatherTo : null;
            material.SetTexture("_CloudWeather",from ? from.weather : cloudData.weather);
            material.SetTexture("_CloudBottomType",from ? from.bottomType : cloudData.bottomType);
            material.SetTexture("_CloudWeatherTo",to ? to.weather : cloudData.weather);
            material.SetTexture("_CloudBottomTypeTo",to ? to.bottomType : cloudData.bottomType);
            material.SetVector("_CloudWeatherBlend",new Vector4(EffectiveWeatherBlend,
                Mathf.Max(1000,from ? from.fieldSize : cloudData.fieldSize),
                Mathf.Max(1000,to ? to.fieldSize : cloudData.fieldSize),0));
            material.SetTexture("_CloudTopProfile", cloudData.topProfile);
            material.SetTexture("_CloudBottomProfile", cloudData.bottomProfile);
            material.SetTexture("_StormNoise", cloudData.shapeNoise);
            var authored=cloudData.authoredNdf;
            var stormData=StormNdf;
            bool hasAuthored=authored && authored.IsReady, hasStorm=stormData && stormData.IsReady;
            material.SetTexture("_AuthoredFields",hasAuthored ? authored.fields : Texture2D.blackTexture);
            material.SetTexture("_AuthoredBottomMask",hasAuthored ? authored.bottomMask : Texture2D.blackTexture);
            material.SetTexture("_StormFields",hasStorm ? stormData.fields : Texture2D.blackTexture);
            material.SetTexture("_StormBottomMask",hasStorm ? stormData.bottomMask : Texture2D.blackTexture);
            Vector2 authoredCenter=hasAuthored ? authored.center : Vector2.zero;
            Vector2 authoredSize=hasAuthored ? authored.worldSize : Vector2.one;
            material.SetVector("_AuthoredRegion",new Vector4(transform.position.x+authoredCenter.x,transform.position.z+authoredCenter.y,Mathf.Max(100,authoredSize.x),Mathf.Max(100,authoredSize.y)));
            material.SetVector("_NdfInfluences",new Vector4(hasAuthored ? authoredInfluenceStrength : 0,hasStorm ? 1 : 0,0,0));
            material.SetVector("_CloudField", new Vector4(transform.position.x, transform.position.z, cloudData.fieldSize, transform.position.y));
            material.SetVector("_CloudControls", new Vector4(coverage, topTypeBias, bottomTypeBias, noiseScale));
            material.SetVector("_CloudWind", new Vector4(windVelocity.x, windVelocity.y, fieldVelocity.x, fieldVelocity.y));
            material.SetVector("_CloudPlanet", new Vector4(transform.position.x,-Mathf.Max(10000,planetRadius),transform.position.z,Mathf.Max(10000,planetRadius)));
            material.SetVector("_CloudSphere", new Vector4(sphericalSky ? 1 : 0,Mathf.Max(1000,maxTraceDistance),Mathf.Max(10,maxStepLength),0));
            Vector3 weights = Vector3.Max(worleyWeights, Vector3.zero);
            float sum = weights.x + weights.y + weights.z;
            material.SetVector("_CloudNoiseWeights", sum > 0.0001f ? weights / sum : new Vector3(1, 0, 0));
            material.SetVector("_StormCenter", transform.position + new Vector3(stormOffset.x, 0, stormOffset.y));
            material.SetVector("_StormSize", new Vector4(radius, height, density, erosion));
            material.SetVector("_StormInfluence", new Vector4(stormStrength, anvilStrength, (float)debugView, 0));
            material.SetVector("_StormMotion", new Vector4(AnimationTime, rotationSpeed, spiralStrength, 0));
            material.SetVector("_StormQuality", new Vector4(viewSteps, lightSteps, extinction, 0));
            ResolveLights(out var sunlight, out var moonlight);
            material.SetVector("_StormSunDirection", sunlight ? -sunlight.transform.forward : Vector3.up);
            material.SetVector("_StormMoonDirection", moonlight ? -moonlight.transform.forward : Vector3.up);
            material.SetColor("_StormSunColor", LightRadiance(sunlight));
            material.SetColor("_StormMoonColor", LightRadiance(moonlight));
            material.SetFloat("_CloudAtmosphereTransmittance", atmosphereTransmittance ? 1 : 0);
            material.SetFloat("_CloudPerSampleAtmosphereTransmittance", perSampleAtmosphereTransmittance ? 1 : 0);
            material.SetFloat("_CloudAtmosphereAmbient", atmosphereAmbient ? 1 : 0);
            material.SetColor("_StormAmbient", ambientColor.linear);
            material.SetVector("_CloudPhaseParameters",new Vector4(Mathf.Clamp(primaryAnisotropy,-.95f,.95f),
                Mathf.Clamp(silverAnisotropy,-.95f,.95f),Mathf.Clamp01(silverWeight),Mathf.Clamp(secondaryAnisotropy,-.95f,.95f)));
            material.SetVector("_CloudLightingStrength",new Vector4(Mathf.Max(0,directIntensity),
                Mathf.Max(0,multipleScatteringIntensity),Mathf.Max(0,ambientIntensity),Mathf.Max(1,sunTraceDistance)));
            material.SetVector("_CloudMultipleScattering",new Vector4(Mathf.Max(.01f,multipleScatteringProfileLength),
                Mathf.Max(0,multipleScatteringDepthPower),Mathf.Max(0,multipleScatteringHeightPower),0));
            material.SetVector("_CloudStormLighting",new Vector4(Mathf.Max(1,stormExtinctionScale),Mathf.Clamp01(stormAmbientDimmer),0,0));
            material.SetFloat("_CloudLightingDebug",(int)lightingDebug);
            material.SetColor("_StormCoreColor", coreColor.linear);
            material.SetVector("_StormEmission", new Vector4(coreIntensity, lightningIntensity, 0, 0));
            var cirrus=cloudData.cirrus;
            bool hasCirrus=cirrus && cirrus.IsReady;
            material.SetTexture("_CirrusWeather",from && from.cirrusWeather ? from.cirrusWeather : hasCirrus ? cirrus.weather : Texture2D.blackTexture);
            material.SetTexture("_CirrusWeatherTo",to && to.cirrusWeather ? to.cirrusWeather : hasCirrus ? cirrus.weather : Texture2D.blackTexture);
            material.SetVector("_CirrusWeatherBlend",new Vector4(EffectiveWeatherBlend,
                Mathf.Max(1000,from && from.cirrusWeather ? from.cirrusFieldSize : hasCirrus ? cirrus.fieldSize : 48000),
                Mathf.Max(1000,to && to.cirrusWeather ? to.cirrusFieldSize : hasCirrus ? cirrus.fieldSize : 48000),0));
            material.SetTexture("_CirrusShapes",hasCirrus ? cirrus.shapes : Texture2D.blackTexture);
            material.SetTexture("_CirrusAuthored",hasCirrus ? cirrus.authored : Texture2D.blackTexture);
            material.SetVector("_CirrusLayer",new Vector4(hasCirrus && enableCirrus ? 1 : 0,EffectiveCirrusAltitude,cirrusOpticalDepth,256));
            material.SetVector("_CirrusControl",new Vector4(cirrusCoverage,cirrusTypeBias,hasCirrus ? cirrusInfluenceStrength : 0,0));
            material.SetVector("_CirrusScale",new Vector4(hasCirrus ? Mathf.Max(1000,cirrus.fieldSize) : 48000,hasCirrus ? Mathf.Max(1000,cirrus.shapeSize) : 18000,hasCirrus ? cirrus.shapes.width : 512,0));
            material.SetVector("_CirrusWind",new Vector4(cirrusWindVelocity.x,cirrusWindVelocity.y,cirrusFieldVelocity.x,cirrusFieldVelocity.y));
            Vector2 c=hasCirrus ? cirrus.authoredCenter : Vector2.zero,s=hasCirrus ? Vector2.Max(cirrus.authoredSize,Vector2.one*100) : Vector2.one;
            material.SetVector("_CirrusRegion",new Vector4(transform.position.x+c.x,transform.position.z+c.y,s.x,s.y));
        }

        void OnDrawGizmosSelected()
        {
            float size = cloudData ? cloudData.fieldSize : 16000;
            Gizmos.color = new Color(0.6f, 0.8f, 1, 0.65f);
            Gizmos.DrawWireCube(transform.position + Vector3.up*height*0.5f, new Vector3(size, height, size));
            Gizmos.color = new Color(1, 0.3f, 0.15f, 0.6f);
            Gizmos.DrawWireSphere(transform.position + new Vector3(stormOffset.x,height*0.4f,stormOffset.y), radius);
        }
    }
}
