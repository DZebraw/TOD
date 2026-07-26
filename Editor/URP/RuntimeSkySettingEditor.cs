#if DAWNTOD_URP_AVAILABLE
using DawnTOD;
using UnityEditor;
using UnityEngine;

namespace DawnTODEditor
{
    [CustomEditor(typeof(RuntimeSkySetting))]
    [CanEditMultipleObjects]
    internal sealed class RuntimeSkySettingEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "RuntimeSkySetting is managed by Dawn TOD's registered URP " +
                "environment output. Edit atmosphere parameters in Volume " +
                "Profile > Add Override > Dawn TOD > Atmosphere.",
                MessageType.Info);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField(
                    "Pipeline Output",
                    "Dawn TOD / Universal");
            }

            EditorGUILayout.Space();
            if (DawnFogRendererFeatureEditorUtility.IsInstalled(
                    out var rendererData))
            {
                EditorGUILayout.HelpBox(
                    $"Dawn TOD Fog is installed on '{rendererData.name}'.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Fog tracks require Dawn TOD Fog on the active URP " +
                    "Renderer Data.",
                    MessageType.Warning);
                if (GUILayout.Button(
                        "Install Dawn TOD Fog Renderer Feature"))
                {
                    DawnFogRendererFeatureEditorUtility
                        .InstallOnDefaultRenderer(out _);
                }
            }
        }
    }
}
#endif
