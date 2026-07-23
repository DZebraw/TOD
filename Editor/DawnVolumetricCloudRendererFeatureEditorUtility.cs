#if USING_URP
using System.Linq;
using DawnTOD;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DawnTODEditor
{
    internal static class DawnVolumetricCloudRendererFeatureEditorUtility
    {
        private const string InstallMenuPath =
            "MagicDawn/TOD/Install URP Volumetric Cloud Renderer Feature";
        private const string CreateVolumeMenuPath =
            "GameObject/MagicDawn/URP Volumetric Cloud Volume";
        private const string CloudShaderName = "Hidden/DawnTOD/VolumetricCloud";
        private const string FeatureDisplayName = "Dawn TOD Volumetric Cloud";
        private const string DefaultProfilePath = "Assets/DawnVolumetricCloudProfile.asset";

        [MenuItem(InstallMenuPath, false, 119)]
        private static void InstallFromMenu()
        {
            if (InstallOnDefaultRenderer(out ScriptableRendererData rendererData))
            {
                Debug.Log(
                    $"Dawn TOD Volumetric Cloud Renderer Feature is installed on '{rendererData.name}'.",
                    rendererData);
            }
        }

        [MenuItem(InstallMenuPath, true)]
        private static bool ValidateInstallFromMenu()
        {
            return DawnFogRendererFeatureEditorUtility.TryGetDefaultRendererData(out _);
        }

        [MenuItem(CreateVolumeMenuPath, false, 22)]
        private static void CreateDefaultVolume(MenuCommand command)
        {
            var volumeObject = new GameObject("Dawn TOD Volumetric Cloud Volume");
            GameObjectUtility.SetParentAndAlign(
                volumeObject,
                command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(
                volumeObject,
                "Create Dawn TOD Volumetric Cloud Volume");

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            string profilePath = AssetDatabase.GenerateUniqueAssetPath(DefaultProfilePath);
            AssetDatabase.CreateAsset(profile, profilePath);
            Undo.RegisterCreatedObjectUndo(profile, "Create Dawn TOD Volumetric Cloud Profile");

            DawnVolumetricCloudVolume cloud =
                profile.Add<DawnVolumetricCloudVolume>(true);
            cloud.enabled.value = true;
            cloud.shapeNoise.value = Resources.Load<Texture3D>(
                "DawnTOD/VolumetricCloud/ExampleNoise13D");
            cloud.detailNoise.value = Resources.Load<Texture3D>(
                "DawnTOD/VolumetricCloud/cloudDetailTexture");
            cloud.weatherMap.value = Resources.Load<Texture2D>(
                "DawnTOD/VolumetricCloud/Substance_graph_output");
            cloud.maskNoise.value = Resources.Load<Texture2D>(
                "DawnTOD/VolumetricCloud/Sky_NoiseSmooth");
            cloud.blueNoise.value = Resources.Load<Texture2D>(
                "DawnTOD/VolumetricCloud/blueNoise");

            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;
            volume.sharedProfile = profile;

            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(volume);
            AssetDatabase.SaveAssetIfDirty(profile);
            Selection.activeGameObject = volumeObject;
        }

        internal static bool IsInstalled(out ScriptableRendererData rendererData)
        {
            if (!DawnFogRendererFeatureEditorUtility.TryGetDefaultRendererData(
                    out rendererData))
            {
                return false;
            }

            return rendererData.rendererFeatures.Any(
                feature => feature is DawnVolumetricCloudRendererFeature &&
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

            DawnVolumetricCloudRendererFeature installedFeature =
                rendererData.rendererFeatures
                    .OfType<DawnVolumetricCloudRendererFeature>()
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

                EnsureCloudBeforeFog(rendererData);
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

            RemoveTransientCloudFeatures(rendererData);

            var feature = ScriptableObject.CreateInstance<DawnVolumetricCloudRendererFeature>();
            feature.name = FeatureDisplayName;
            Undo.RegisterCreatedObjectUndo(feature, "Install Dawn TOD Volumetric Cloud");
            AssetDatabase.AddObjectToAsset(feature, rendererData);

            Shader shader = Shader.Find(CloudShaderName);
            var featureObject = new SerializedObject(feature);
            featureObject.FindProperty("cloudShader").objectReferenceValue = shader;
            featureObject.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                feature,
                out _,
                out long localId);

            var rendererObject = new SerializedObject(rendererData);
            SerializedProperty features = rendererObject.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = rendererObject.FindProperty("m_RendererFeatureMap");
            int featureIndex = features.arraySize;
            features.arraySize++;
            features.GetArrayElementAtIndex(featureIndex).objectReferenceValue = feature;
            featureMap.arraySize++;
            featureMap.GetArrayElementAtIndex(featureIndex).longValue = localId;
            rendererObject.ApplyModifiedProperties();

            EnsureCloudBeforeFog(rendererData);
            rendererData.SetDirty();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssetIfDirty(feature);
            AssetDatabase.SaveAssetIfDirty(rendererData);

            ScriptableRendererData savedRendererData =
                AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererDataPath);
            bool installed = savedRendererData != null &&
                             savedRendererData.rendererFeatures.Any(
                                 savedFeature =>
                                     savedFeature is DawnVolumetricCloudRendererFeature);
            if (!installed)
            {
                Debug.LogError(
                    $"Dawn TOD Volumetric Cloud could not be persisted to '{rendererDataPath}'.",
                    rendererData);
            }

            return installed;
        }

        private static void EnsureCloudBeforeFog(ScriptableRendererData rendererData)
        {
            var rendererObject = new SerializedObject(rendererData);
            SerializedProperty features = rendererObject.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = rendererObject.FindProperty("m_RendererFeatureMap");
            int cloudIndex = -1;
            int fogIndex = -1;
            for (int index = 0; index < features.arraySize; index++)
            {
                Object feature = features.GetArrayElementAtIndex(index).objectReferenceValue;
                if (feature is DawnVolumetricCloudRendererFeature)
                {
                    cloudIndex = index;
                }
                else if (feature is DawnFogRendererFeature)
                {
                    fogIndex = index;
                }
            }

            if (cloudIndex < 0 || fogIndex < 0 || cloudIndex < fogIndex)
            {
                return;
            }

            features.MoveArrayElement(cloudIndex, fogIndex);
            if (featureMap != null && cloudIndex < featureMap.arraySize &&
                fogIndex < featureMap.arraySize)
            {
                featureMap.MoveArrayElement(cloudIndex, fogIndex);
            }

            rendererObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssetIfDirty(rendererData);
        }

        private static void RemoveTransientCloudFeatures(
            ScriptableRendererData rendererData)
        {
            var rendererObject = new SerializedObject(rendererData);
            SerializedProperty features = rendererObject.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = rendererObject.FindProperty("m_RendererFeatureMap");
            for (int index = features.arraySize - 1; index >= 0; index--)
            {
                Object feature = features.GetArrayElementAtIndex(index).objectReferenceValue;
                if (!(feature is DawnVolumetricCloudRendererFeature) ||
                    !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(feature)))
                {
                    continue;
                }

                features.GetArrayElementAtIndex(index).objectReferenceValue = null;
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
