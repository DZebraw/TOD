using DawnTOD;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RuntimeSkySetting))]
[CanEditMultipleObjects]
internal sealed class RuntimeSkySettingEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "RuntimeSkySetting is the URP atmosphere runtime driver. " +
            "Edit atmosphere parameters in Volume Profile > Add Override > Dawn TOD > Atmosphere.",
            MessageType.Info);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.LabelField("Runtime Driver", "Managed by DawnTODSystem");
        }
    }
}
