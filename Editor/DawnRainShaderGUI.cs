using UnityEditor;
using UnityEngine;

namespace DawnTODEditor
{
    public sealed class DawnRainShaderGUI : ShaderGUI
    {
        public override void OnMaterialPreviewGUI(
            MaterialEditor materialEditor,
            Rect previewRect,
            GUIStyle background)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(
                    previewRect,
                    new Color(0.16f, 0.18f, 0.2f, 1f));
            }
        }
    }
}
