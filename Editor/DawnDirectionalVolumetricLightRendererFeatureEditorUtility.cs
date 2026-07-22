#if USING_URP
using System.Linq;
using DawnTOD;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DawnTODEditor
{
    internal static class
        DawnDirectionalVolumetricLightRendererFeatureEditorUtility
    {
        private const string InstallMenuPath =
            "MagicDawn/TOD/Install URP Directional Volumetric Light Renderer Feature";
        private const string VolumetricLightShaderName =
            "Hidden/DawnTOD/DirectionalVolumetricLight";
        private const string FeatureDisplayName =
            "Dawn TOD Directional Volumetric Light";

        [MenuItem(InstallMenuPath, false, 118)]
        private static void InstallFromMenu()
        {
            if (InstallOnDefaultRenderer(out ScriptableRendererData rendererData))
            {
                Debug.Log(
                    $"Dawn TOD Directional Volumetric Light Renderer Feature is installed on '{rendererData.name}'.",
                    rendererData);
            }
        }

        [MenuItem(InstallMenuPath, true)]
        private static bool ValidateInstallFromMenu()
        {
            return DawnFogRendererFeatureEditorUtility.TryGetDefaultRendererData(
                out _);
        }

        internal static bool IsInstalled(out ScriptableRendererData rendererData)
        {
            if (!DawnFogRendererFeatureEditorUtility.TryGetDefaultRendererData(
                    out rendererData))
            {
                return false;
            }

            return rendererData.rendererFeatures.Any(
                feature =>
                    feature is DawnDirectionalVolumetricLightRendererFeature &&
                    !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(feature)));
        }

        internal static bool InstallOnDefaultRenderer(
            out ScriptableRendererData rendererData)
        {
            if (!DawnFogRendererFeatureEditorUtility.TryGetDefaultRendererData(
                    out rendererData))
            {
                Debug.LogError(
                    "Dawn TOD could not find the default URP Renderer Data. " +
                    "Assign a Universal Render Pipeline Asset in Graphics Settings first.");
                return false;
            }

            DawnDirectionalVolumetricLightRendererFeature installedFeature =
                rendererData.rendererFeatures
                    .OfType<DawnDirectionalVolumetricLightRendererFeature>()
                    .FirstOrDefault(
                        feature => !string.IsNullOrEmpty(
                            AssetDatabase.GetAssetPath(feature)));
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

            RemoveTransientFeatures(rendererData);

            var feature = ScriptableObject.CreateInstance<
                DawnDirectionalVolumetricLightRendererFeature>();
            feature.name = FeatureDisplayName;
            Undo.RegisterCreatedObjectUndo(
                feature,
                "Install Dawn TOD Directional Volumetric Light");
            AssetDatabase.AddObjectToAsset(feature, rendererData);

            Shader shader = Shader.Find(VolumetricLightShaderName);
            var featureObject = new SerializedObject(feature);
            featureObject.FindProperty("volumetricLightShader")
                .objectReferenceValue = shader;
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
                AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(
                    rendererDataPath);
            bool installed = savedRendererData != null &&
                savedRendererData.rendererFeatures.Any(
                    savedFeature =>
                        savedFeature is
                            DawnDirectionalVolumetricLightRendererFeature);
            if (!installed)
            {
                Debug.LogError(
                    $"Dawn TOD Directional Volumetric Light could not be persisted to '{rendererDataPath}'.",
                    rendererData);
            }

            return installed;
        }

        private static void RemoveTransientFeatures(
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
                if (!(feature is
                        DawnDirectionalVolumetricLightRendererFeature) ||
                    !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(feature)))
                {
                    continue;
                }

                features.GetArrayElementAtIndex(index).objectReferenceValue =
                    null;
                features.DeleteArrayElementAtIndex(index);
                if (featureMap != null && index < featureMap.arraySize)
                {
                    featureMap.DeleteArrayElementAtIndex(index);
                }

                Object.DestroyImmediate(feature);
            }

            rendererObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
