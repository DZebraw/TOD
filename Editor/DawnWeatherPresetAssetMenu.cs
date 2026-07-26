using DawnTOD;
using UnityEditor;

namespace DawnTODEditor
{
    internal static class DawnWeatherPresetAssetMenu
    {
        [MenuItem("Assets/Create/MagicDawn/TODPreset", false, 100)]
        private static void CreatePreset()
        {
            WeatherRenderPipelineKind pipelineKind =
                WeatherPipelineCapabilities.Current.PipelineKind;
            DawnWeatherPreset preset =
                DawnWeatherPreset.CreateWithDefaults(pipelineKind);
            ProjectWindowUtil.CreateAsset(preset, "TODPreset.asset");
        }
    }
}
