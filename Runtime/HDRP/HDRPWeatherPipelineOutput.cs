#if DAWNTOD_HDRP_AVAILABLE
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DawnTOD
{
    internal sealed class HDRPWeatherPipelineOutput : IWeatherPipelineOutput
    {
        private readonly DawnTODSystem owner;

        private Volume cachedVolume;
        private VolumeProfile cachedProfile;
        private PhysicallyBasedSky physicalSky;
        private Fog fog;
        private Exposure exposure;

        internal HDRPWeatherPipelineOutput(DawnTODSystem owner)
        {
            this.owner = owner;
        }

        public WeatherPipelineCapabilities Capabilities =>
            WeatherPipelineCapabilities.HighDefinition;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterBeforeSceneLoad()
        {
            Register();
        }

        internal static void Register()
        {
            WeatherPipelineOutputRegistry.Register(
                WeatherRenderPipelineKind.HighDefinition,
                typeof(HDRPWeatherPipelineOutput),
                system => new HDRPWeatherPipelineOutput(system));
        }

        public void Prepare()
        {
            Volume currentVolume = owner != null
                ? owner.hdrpVolume
                : null;
            VolumeProfile currentProfile = currentVolume != null
                ? currentVolume.profile
                : null;
            if (currentVolume == cachedVolume &&
                currentProfile == cachedProfile &&
                HasCurrentOverrides())
            {
                return;
            }

            cachedVolume = currentVolume;
            cachedProfile = currentProfile;
            physicalSky = null;
            fog = null;
            exposure = null;
            if (cachedProfile == null)
            {
                return;
            }

            cachedProfile.TryGet(out physicalSky);
            cachedProfile.TryGet(out fog);
            cachedProfile.TryGet(out exposure);
        }

        private bool HasCurrentOverrides()
        {
            return cachedProfile != null &&
                   physicalSky != null &&
                   fog != null &&
                   exposure != null &&
                   cachedProfile.components.Contains(physicalSky) &&
                   cachedProfile.components.Contains(fog) &&
                   cachedProfile.components.Contains(exposure);
        }

        public void Apply(in WeatherPipelineOutputState state)
        {
            Prepare();
            if (physicalSky != null)
            {
                SetOverride(
                    physicalSky.spaceEmissionMultiplier,
                    state.StarEmission);
            }

            if (fog != null)
            {
                SetOverride(fog.enabled, state.FogEnabled);
                SetOverride(fog.meanFreePath, state.FogDistance);
                SetOverride(fog.baseHeight, state.FogHeight);
                SetOverride(
                    fog.enableVolumetricFog,
                    state.FogEnabled);
                SetOverride(fog.albedo, state.FogColor);
            }

            if (exposure != null)
            {
                SetOverride(exposure.mode, ExposureMode.Automatic);
                SetOverride(
                    exposure.compensation,
                    state.ExposureCompensation);
            }
        }

        public void Release()
        {
            cachedVolume = null;
            cachedProfile = null;
            physicalSky = null;
            fog = null;
            exposure = null;
        }

        public bool IsConfigured(out string errorMessage)
        {
            Prepare();
            if (owner == null || owner.hdrpVolume == null)
            {
                errorMessage = "HDRP environment output requires a Volume.";
                return false;
            }

            if (owner.hdrpVolume.profile == null)
            {
                errorMessage =
                    "HDRP environment output requires a Volume Profile.";
                return false;
            }

            if (physicalSky == null || fog == null || exposure == null)
            {
                errorMessage =
                    "The HDRP Volume Profile must contain Physically Based " +
                    "Sky, Fog and Exposure overrides.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private static void SetOverride<T>(
            VolumeParameter<T> parameter,
            T value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }
    }
}
#endif
