#if DAWNTOD_URP_AVAILABLE
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DawnTOD
{
    internal sealed class URPWeatherPipelineOutput : IWeatherPipelineOutput
    {
        private const float RuntimeFogVolumePriority = 10000f;

        private readonly DawnTODSystem owner;

        private RuntimeSkySetting runtimeSkySetting;
        private GameObject runtimeFogVolumeObject;
        private Volume runtimeFogVolume;
        private VolumeProfile runtimeFogProfile;
        private DawnFogVolume runtimeFogSettings;
#if UNITY_EDITOR
        private WeatherPipelineOutputState pendingState;
        private bool hasPendingState;
#endif
        private bool isReleased;
#if UNITY_EDITOR
        private bool editorPreparationScheduled;
#endif

        internal URPWeatherPipelineOutput(DawnTODSystem owner)
        {
            this.owner = owner;
        }

        public WeatherPipelineCapabilities Capabilities =>
            WeatherPipelineCapabilities.Universal;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterBeforeSceneLoad()
        {
            Register();
        }

        internal static void Register()
        {
            WeatherPipelineOutputRegistry.Register(
                WeatherRenderPipelineKind.Universal,
                typeof(URPWeatherPipelineOutput),
                system => new URPWeatherPipelineOutput(system),
                typeof(RuntimeSkySetting));
        }

        public void Prepare()
        {
            if (isReleased)
            {
                return;
            }

            if (owner == null)
            {
                Release();
                return;
            }

            if (runtimeSkySetting == null)
            {
                runtimeSkySetting =
                    owner.GetComponent<RuntimeSkySetting>();
                if (runtimeSkySetting == null && Application.isPlaying)
                {
                    runtimeSkySetting =
                        owner.gameObject.AddComponent<RuntimeSkySetting>();
                }
            }

            if (runtimeSkySetting != null)
            {
                runtimeSkySetting.SetPipelineOutputActive(true);
            }

            if (runtimeFogVolumeObject == null ||
                runtimeFogVolume == null ||
                runtimeFogProfile == null ||
                runtimeFogSettings == null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    ScheduleEditorPreparation();
                    return;
                }
#endif
                EnsureRuntimeFogVolume();
            }
        }

        public void Apply(in WeatherPipelineOutputState state)
        {
            if (isReleased)
            {
                return;
            }

#if UNITY_EDITOR
            pendingState = state;
            hasPendingState = true;
#endif
            Prepare();
            ApplyPreparedState(state);
        }

        private void ApplyPreparedState(in WeatherPipelineOutputState state)
        {
            if (runtimeSkySetting != null)
            {
                runtimeSkySetting.SetSpaceEmissionMultiplier(
                    state.StarEmission);
            }

            if (runtimeFogSettings == null)
            {
                return;
            }

            SetOverride(runtimeFogSettings.enabled, state.FogEnabled);
            SetOverride(runtimeFogSettings.volumetricFog, state.VolumetricFog.Sanitized());
            SetOverride(
                runtimeFogSettings.density,
                Mathf.Clamp(state.FogDensity, 0f, 10f));
            SetOverride(runtimeFogSettings.height, state.FogHeight);
            SetOverride(runtimeFogSettings.secondFogEnabled, state.SecondFogDensity > 0f);
            SetOverride(runtimeFogSettings.secondFogDensity, Mathf.Clamp(state.SecondFogDensity, 0f, 10f));
            SetOverride(runtimeFogSettings.secondFogHeightFalloff, Mathf.Clamp(state.SecondFogHeightFalloff, 0f, 2f));
            SetOverride(runtimeFogSettings.secondFogHeightOffset, state.SecondFogHeightOffset);
            SetOverride(runtimeFogSettings.inscatteringColor, state.FogColor);
            SetOverride(runtimeFogSettings.atmosphereLighting,
                state.FogHeightFogContribution > 0f);
            SetOverride(runtimeFogSettings.skyAtmosphereAmbientContributionColorScale, state.FogSkyAtmosphereAmbientContributionColorScale);
            SetOverride(runtimeFogSettings.heightFogContribution, Mathf.Max(0f, state.FogHeightFogContribution));
            SetOverride(runtimeFogSettings.directionalInscatteringColor, state.FogDirectionalInscatteringColor);
            SetOverride(runtimeFogSettings.directionalInscatteringExponent, Mathf.Clamp(state.FogDirectionalInscatteringExponent, 0.000001f, 1000f));
            SetOverride(runtimeFogSettings.directionalInscatteringStartDistance, Mathf.Max(0f, state.FogDirectionalInscatteringStartDistance));
            SetOverride(
                runtimeFogSettings.heightFalloff,
                Mathf.Clamp(state.FogHeightFalloff, 0f, 2f));
            SetOverride(runtimeFogSettings.maxOpacity, Mathf.Clamp01(state.FogMaxOpacity));
            SetOverride(runtimeFogSettings.startDistance, Mathf.Max(0f, state.FogStartDistance));
            SetOverride(runtimeFogSettings.endDistance, Mathf.Max(0f, state.FogEndDistance));
            SetOverride(runtimeFogSettings.cutoffDistance, Mathf.Max(0f, state.FogCutoffDistance));
        }

        public void Release()
        {
            if (isReleased)
            {
                return;
            }

            isReleased = true;
#if UNITY_EDITOR
            hasPendingState = false;
            if (editorPreparationScheduled)
            {
                EditorApplication.delayCall -= PrepareInEditor;
                editorPreparationScheduled = false;
            }
#endif
            if (runtimeFogVolume != null)
            {
                runtimeFogVolume.enabled = false;
            }

            DestroyRuntimeFogResources();
            if (runtimeSkySetting != null)
            {
                runtimeSkySetting.SetPipelineOutputActive(false);
            }
            runtimeSkySetting = null;
        }

        private void DestroyRuntimeFogResources()
        {
            DestroyRuntimeObject(runtimeFogVolumeObject);
            DestroyRuntimeObject(runtimeFogProfile);
            runtimeFogVolumeObject = null;
            runtimeFogVolume = null;
            runtimeFogProfile = null;
            runtimeFogSettings = null;
        }

        public bool IsConfigured(out string errorMessage)
        {
            if (owner == null)
            {
                errorMessage = "The URP output has no DawnTODSystem owner.";
                return false;
            }

            if (runtimeSkySetting == null)
            {
                runtimeSkySetting =
                    owner.GetComponent<RuntimeSkySetting>();
            }

            if (runtimeSkySetting == null)
            {
                errorMessage =
                    "URP environment output requires RuntimeSkySetting on " +
                    "the Dawn TOD System GameObject.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private void EnsureRuntimeFogVolume()
        {
            DestroyRuntimeFogResources();
            if (owner == null)
            {
                return;
            }

            runtimeFogVolumeObject =
                new GameObject("Dawn TOD Runtime Fog Volume")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = owner.gameObject.layer
                };
            runtimeFogVolumeObject.transform.SetParent(
                owner.transform,
                false);

            runtimeFogProfile =
                ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeFogProfile.name = "Dawn TOD Runtime Fog Profile";
            runtimeFogProfile.hideFlags = HideFlags.HideAndDontSave;
            runtimeFogSettings =
                runtimeFogProfile.Add<DawnFogVolume>(true);

            runtimeFogVolume =
                runtimeFogVolumeObject.AddComponent<Volume>();
            runtimeFogVolume.isGlobal = true;
            runtimeFogVolume.priority = RuntimeFogVolumePriority;
            runtimeFogVolume.weight = 1f;
            runtimeFogVolume.sharedProfile = runtimeFogProfile;
        }

#if UNITY_EDITOR
        private void ScheduleEditorPreparation()
        {
            if (editorPreparationScheduled || isReleased)
            {
                return;
            }

            editorPreparationScheduled = true;
            EditorApplication.delayCall += PrepareInEditor;
        }

        private void PrepareInEditor()
        {
            editorPreparationScheduled = false;
            if (isReleased ||
                Application.isPlaying ||
                owner == null ||
                !owner.isActiveAndEnabled ||
                WeatherPipelineCapabilities.Current.PipelineKind !=
                    WeatherRenderPipelineKind.Universal)
            {
                return;
            }

            EnsureRuntimeFogVolume();
            if (hasPendingState)
            {
                ApplyPreparedState(pendingState);
            }
        }
#endif

        private static void SetOverride<T>(
            VolumeParameter<T> parameter,
            T value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        private static void DestroyRuntimeObject(
            UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(target);
                return;
            }
#endif
            UnityEngine.Object.Destroy(target);
        }
    }
}
#endif
