#if DAWNTOD_URP_AVAILABLE
using DawnTOD;
using UnityEditor;
using UnityEngine;

namespace DawnTODEditor
{
    [InitializeOnLoad]
    internal static class URPWeatherPipelineOutputRegistration
    {
        static URPWeatherPipelineOutputRegistration()
        {
            URPWeatherPipelineOutput.Register();
            EditorApplication.delayCall += RefreshLoadedSystems;
        }

        private static void RefreshLoadedSystems()
        {
            DawnTODSystem[] systems =
                Resources.FindObjectsOfTypeAll<DawnTODSystem>();
            for (int index = 0; index < systems.Length; index++)
            {
                DawnTODSystem system = systems[index];
                if (system != null &&
                    system.isActiveAndEnabled &&
                    system.gameObject.scene.IsValid())
                {
                    system.RefreshWeatherBlendingSystem();
                }
            }
        }
    }
}
#endif
