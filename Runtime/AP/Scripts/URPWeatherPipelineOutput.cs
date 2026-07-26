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
        private const float MinimumFogDistance = 0.01f;
        private const float DefaultFogHeightRange = 100f;
        private const float DefaultMaximumFogDistance = 5000f;

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

            float baseHeight = state.FogHeight;
            SetOverride(runtimeFogSettings.enabled, state.FogEnabled);
            SetOverride(
                runtimeFogSettings.meanFreePath,
                Mathf.Max(MinimumFogDistance, state.FogDistance));
            SetOverride(runtimeFogSettings.baseHeight, baseHeight);
            SetOverride(runtimeFogSettings.albedo, state.FogColor);
            SetOverride(
                runtimeFogSettings.maximumHeight,
                baseHeight + DefaultFogHeightRange);
            SetOverride(
                runtimeFogSettings.maximumFogDistance,
                DefaultMaximumFogDistance);
            SetOverride(runtimeFogSettings.affectSky, state.FogAffectSky);
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
