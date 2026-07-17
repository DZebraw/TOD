using System;
using UnityEditor;

namespace DawnTODEditor.AI
{
    [InitializeOnLoad]
    internal static class DawnTodAiEditorLifecycle
    {
        static DawnTodAiEditorLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.delayCall += RestoreAfterDomainLoad;
        }

        private static async void RestoreAfterDomainLoad()
        {
            try
            {
                await DawnTodAiServiceManager.Shared.TryRestoreSessionAsync();
            }
            catch (Exception)
            {
                // Lifecycle failures are exposed through manager state; never log session data.
            }
        }

        private static void BeforeAssemblyReload()
        {
            DawnTodAiServiceManager.Shared.PrepareForAssemblyReload();
        }

        private static void OnEditorQuitting()
        {
            DawnTodAiServiceManager.Shared.TerminateForEditorQuit();
        }
    }
}
