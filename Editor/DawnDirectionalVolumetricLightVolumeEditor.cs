#if USING_URP
using DawnTOD;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace DawnTODEditor
{
    [CustomEditor(typeof(DawnDirectionalVolumetricLightVolume))]
    internal sealed class DawnDirectionalVolumetricLightVolumeEditor :
        VolumeComponentEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (DawnDirectionalVolumetricLightRendererFeatureEditorUtility
                .IsInstalled(out _))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "This Volume override needs the Dawn TOD Directional " +
                "Volumetric Light Renderer Feature on the active URP Renderer.",
                MessageType.Warning);

            bool canInstall = DawnFogRendererFeatureEditorUtility
                .TryGetDefaultRendererData(out _);
            using (new EditorGUI.DisabledScope(!canInstall))
            {
                if (GUILayout.Button("Install Required Renderer Feature"))
                {
                    DawnDirectionalVolumetricLightRendererFeatureEditorUtility
                        .InstallOnDefaultRenderer(out _);
                }
            }
        }
    }
}
#endif
