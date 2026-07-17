#if USING_URP
using System.Linq;
using DawnTOD;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DawnTODEditor
{
    internal static class DawnFogRendererFeatureEditorUtility
    {
        private const string InstallMenuPath =
            "MagicDawn/TOD/Install URP Fog Renderer Feature";
        private const string FogShaderName = "Hidden/DawnTOD/PostProcessFog";
        private const string FeatureDisplayName = "Dawn TOD Fog";

        [MenuItem(InstallMenuPath, false, 120)]
        private static void InstallFromMenu()
        {
            if (InstallOnDefaultRenderer(out ScriptableRendererData rendererData))
            {
                Debug.Log(
                    $"Dawn TOD Fog Renderer Feature is installed on '{rendererData.name}'.",
                    rendererData);
            }
        }

        [MenuItem(InstallMenuPath, true)]
        private static bool ValidateInstallFromMenu()
        {
            return TryGetDefaultRendererData(out _);
        }

        internal static bool IsInstalled(out ScriptableRendererData rendererData)
        {
            if (!TryGetDefaultRendererData(out rendererData))
            {
                return false;
            }

            return rendererData.rendererFeatures.Any(
                feature => feature is DawnFogRendererFeature &&
                           !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(feature)));
        }

        internal static bool InstallOnDefaultRenderer(
            out ScriptableRendererData rendererData)
        {
            if (!TryGetDefaultRendererData(out rendererData))
            {
                Debug.LogError(
                    "Dawn TOD could not find the default URP Renderer Data. " +
                    "Assign a Universal Render Pipeline Asset in Graphics Settings first.");
                return false;
            }

            DawnFogRendererFeature installedFeature = rendererData.rendererFeatures
                .OfType<DawnFogRendererFeature>()
                .FirstOrDefault(
                    feature => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(feature)));
            if (installedFeature != null)
            {
                if (installedFeature.name != FeatureDisplayName)
                {
                    installedFeature.name = FeatureDisplayName;
                    EditorUtility.SetDirty(installedFeature);
                    AssetDatabase.SaveAssetIfDirty(installedFeature);
                }
                return true;
            }

            string rendererDataPath = AssetDatabase.GetAssetPath(rendererData);
            if (string.IsNullOrEmpty(rendererDataPath))
            {
                Debug.LogError(
                    $"The default URP Renderer Data '{rendererData.name}' is not a persistent asset. " +
                    "Assign a Renderer Data asset on the active URP Pipeline Asset.",
                    rendererData);
                return false;
            }

            RemoveTransientFogFeatures(rendererData);

            var feature = ScriptableObject.CreateInstance<DawnFogRendererFeature>();
            feature.name = FeatureDisplayName;
            Undo.RegisterCreatedObjectUndo(feature, "Install Dawn TOD Fog");

            AssetDatabase.AddObjectToAsset(feature, rendererData);
            feature.name = FeatureDisplayName;

            Shader shader = Shader.Find(FogShaderName);
            var featureObject = new SerializedObject(feature);
            featureObject.FindProperty("fogShader").objectReferenceValue = shader;
            featureObject.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                feature,
                out _,
                out long localId);

            var rendererObject = new SerializedObject(rendererData);
            SerializedProperty features =
                rendererObject.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap =
                rendererObject.FindProperty("m_RendererFeatureMap");
            int index = features.arraySize;
            features.arraySize++;
            features.GetArrayElementAtIndex(index).objectReferenceValue = feature;
            featureMap.arraySize++;
            featureMap.GetArrayElementAtIndex(index).longValue = localId;
            rendererObject.ApplyModifiedProperties();

            rendererData.SetDirty();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssetIfDirty(feature);
            AssetDatabase.SaveAssetIfDirty(rendererData);

            ScriptableRendererData savedRendererData =
                AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererDataPath);
            bool installed = savedRendererData != null &&
                savedRendererData.rendererFeatures.Any(
                    savedFeature => savedFeature is DawnFogRendererFeature);
            if (!installed)
            {
                Debug.LogError(
                    $"Dawn TOD Fog could not be persisted to '{rendererDataPath}'.",
                    rendererData);
            }

            return installed;
        }

        private static void RemoveTransientFogFeatures(
            ScriptableRendererData rendererData)
        {
            var rendererObject = new SerializedObject(rendererData);
            SerializedProperty features =
                rendererObject.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap =
                rendererObject.FindProperty("m_RendererFeatureMap");

            for (int index = features.arraySize - 1; index >= 0; index--)
            {
                Object feature = features
                    .GetArrayElementAtIndex(index)
                    .objectReferenceValue;
                if (!(feature is DawnFogRendererFeature) ||
                    !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(feature)))
                {
                    continue;
                }

                features.GetArrayElementAtIndex(index).objectReferenceValue = null;
                features.DeleteArrayElementAtIndex(index);
                if (index < featureMap.arraySize)
                {
                    featureMap.DeleteArrayElementAtIndex(index);
                }

                Object.DestroyImmediate(feature);
            }

            rendererObject.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static bool TryGetDefaultRendererData(
            out ScriptableRendererData rendererData)
        {
            rendererData = null;
            RenderPipelineAsset currentPipeline =
                QualitySettings.renderPipeline ?? GraphicsSettings.currentRenderPipeline;
            if (!(currentPipeline is UniversalRenderPipelineAsset urpAsset))
            {
                return false;
            }

            var pipelineObject = new SerializedObject(urpAsset);
            SerializedProperty rendererDataList =
                pipelineObject.FindProperty("m_RendererDataList");
            SerializedProperty defaultRendererIndex =
                pipelineObject.FindProperty("m_DefaultRendererIndex");
            if (rendererDataList == null || rendererDataList.arraySize == 0)
            {
                return false;
            }

            int index = defaultRendererIndex != null
                ? Mathf.Clamp(defaultRendererIndex.intValue, 0, rendererDataList.arraySize - 1)
                : 0;
            rendererData = rendererDataList
                .GetArrayElementAtIndex(index)
                .objectReferenceValue as ScriptableRendererData;
            return rendererData != null;
        }
    }
}
#endif
