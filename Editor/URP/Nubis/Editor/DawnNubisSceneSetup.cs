using System;
using System.Linq;
using DawnTOD;
using NubisStorm;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DawnTODEditor
{
    public static class DawnNubisSceneSetup
    {
        const string Root = "Assets/DawnTOD";

        [MenuItem("GameObject/MagicDawn/Nubis Cloud Model", false, 22)]
        static void CreateCloud(MenuCommand command)
        {
            var model = new GameObject("Dawn TOD Nubis Clouds");
            GameObjectUtility.SetParentAndAlign(model, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(model, "Create Nubis cloud model");
            model.transform.position = new Vector3(model.transform.position.x,900,model.transform.position.z);
            var cloud = model.AddComponent<NubisStormVolume>();
            cloud.cloudData = Resources.Load<NubisCloudData>("DawnTOD/NubisCloud/Data/SharedCloudData");
            cloud.sun = DawnTODSystem.Instance ? DawnTODSystem.Instance.GetMainDirectionalLight() : RenderSettings.sun;
            cloud.stormStrength = 0;
            Selection.activeGameObject = model;
        }

        [MenuItem("MagicDawn/TOD/Install URP Nubis Cloud Renderer Feature", false, 119)]
        static void InstallCloud()
        {
            if (!Camera.main) throw new InvalidOperationException("A Main Camera is required.");
            foreach (var renderer in new[] { ResolveRenderer(Camera.main), ResolveRenderer(null) }.Distinct())
            {
                EnsureFeature<NubisStormRendererFeature>(renderer, "Nubis Clouds", "stormShader", "Hidden/NubisStorm/Volume");
                EditorUtility.SetDirty(renderer);
            }
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
        }

        [MenuItem("MagicDawn/TOD/Configure Current Nubis Scene (Preserve Clouds)", false, 117)]
        public static void ConfigureCurrentScene()
        {
            var cloud = NubisStormVolume.Active;
            var camera = Camera.main;
            if (!cloud || !cloud.IsReady || !camera)
                throw new InvalidOperationException("The current scene needs a ready Nubis cloud model and a Main Camera.");
            if (!cloud.sun || cloud.sun.type != LightType.Directional)
                throw new InvalidOperationException("Assign the existing directional Sun on the Nubis cloud model first.");
            var renderers = new[] { ResolveRenderer(camera), ResolveRenderer(null) }.Distinct().ToArray();
            string originalCloud = EditorJsonUtility.ToJson(cloud);
            Quaternion sunRotation = cloud.sun.transform.rotation;
            Color sunColor = cloud.sun.color;
            float sunIntensity = cloud.sun.intensity;
            bool sunActive = cloud.sun.gameObject.activeSelf;

            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "DawnTOD");
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/NubisAtmosphereProfile.asset");
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, Root + "/NubisAtmosphereProfile.asset");
                var atmosphere = profile.Add<DawnAtmosphereVolume>(true);
                // Nubis scenes commonly use a low, artistic Sun intensity. Scale the
                // sky scattering independently so preserving that Sun stays usable.
                atmosphere.rayleighScatterStrength.value = 10;
                atmosphere.mieScatterStrength.value = 10;
                var previousSky = RenderSettings.skybox;
                if (previousSky && previousSky.HasProperty("_Horizon"))
                    atmosphere.groundColor.value = previousSky.GetColor("_Horizon").linear;
                atmosphere.sunDiskScale.value = .75f;
                atmosphere.ambientLightIntensity.value = 1;
                // Height fog is authored only in TOD and supplied by its hidden runtime Volume.
                foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            }
            foreach (var renderer in renderers)
            {
                EnsureFeature<NubisStormRendererFeature>(renderer, "Nubis Clouds", "stormShader", "Hidden/NubisStorm/Volume");
                EnsureFeature<DawnAtmosphereLightingRendererFeature>(renderer,"Dawn TOD Surface Lighting",null,null);
                EnsureFeature<DawnFogRendererFeature>(renderer, "Dawn TOD Fog", "fogShader", "Hidden/DawnTOD/PostProcessFog");
            }

            var system = UnityEngine.Object.FindObjectsOfType<DawnTODSystem>(true)
                .FirstOrDefault(x => x.gameObject.scene == cloud.gameObject.scene);
            if (!system)
            {
                var root = new GameObject("Dawn TOD · Nubis atmosphere and light");
                root.SetActive(false);
                Undo.RegisterCreatedObjectUndo(root, "Configure Dawn TOD Nubis scene");
                system = root.AddComponent<DawnTODSystem>();
                system.sunLight = cloud.sun;
                system.driveDirectionalLights = false;
                system.AutoAdvanceTime = false;
                var preset = DawnWeatherPreset.CreateWithDefaults(WeatherRenderPipelineKind.Universal);
                preset.name = "Nubis scene lighting";
                preset.sunIntensityCurve = AnimationCurve.Constant(0,1,sunIntensity);
                preset.sunColorGradient = ConstantColor(sunColor);
                preset.starEmissionCurve = AnimationCurve.Constant(0,1,0);
                string presetPath = AssetDatabase.GenerateUniqueAssetPath(Root + "/NubisSceneWeather.asset");
                AssetDatabase.CreateAsset(preset,presetPath);
                system.FallbackPreset = preset;
                var controller = root.AddComponent<DawnWeatherController>();
                controller.ActivePreset = preset;
                var controllerObject = new SerializedObject(controller);
                controllerObject.FindProperty("fogEnabled").boolValue = false;
                controllerObject.ApplyModifiedPropertiesWithoutUndo();
                var sky = root.AddComponent<RuntimeSkySetting>();
                sky.planetRadius = cloud.planetRadius;
                var volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 100;
                volume.sharedProfile = profile;
                root.SetActive(true);
                system.RescanControllers(true);
            }
            else
            {
                Undo.RecordObject(system,"Preserve Nubis lighting");
                system.driveDirectionalLights = false;
                system.sunLight = cloud.sun;
                var sky = system.GetComponent<RuntimeSkySetting>();
                if (!sky) sky = Undo.AddComponent<RuntimeSkySetting>(system.gameObject);
                var volume = system.GetComponent<Volume>();
                if (!volume) volume = Undo.AddComponent<Volume>(system.gameObject);
                Undo.RecordObject(volume,"Configure Nubis atmosphere");
                volume.isGlobal = true; volume.priority = 100; volume.sharedProfile = profile;
            }
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            system.RefreshWeatherBlendingSystem();
            system.GetComponent<RuntimeSkySetting>().SendMessage("Update", SendMessageOptions.DontRequireReceiver);
            if (originalCloud != EditorJsonUtility.ToJson(cloud) || sunRotation != cloud.sun.transform.rotation ||
                sunColor != cloud.sun.color || sunIntensity != cloud.sun.intensity || sunActive != cloud.sun.gameObject.activeSelf)
                throw new InvalidOperationException("Scene setup unexpectedly changed the authored cloud or Sun.");
            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(system);
            foreach (var renderer in renderers) EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(cloud.gameObject.scene);
            SceneView.RepaintAll();
            Debug.Log("Dawn TOD configured on the camera and Scene View renderers. Nubis cloud parameters and authored Sun are preserved.", system);
        }

        static Gradient ConstantColor(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(color,0),new GradientColorKey(color,1) },
                new[] { new GradientAlphaKey(color.a,0),new GradientAlphaKey(color.a,1) });
            return gradient;
        }

        static ScriptableRendererData ResolveRenderer(Camera camera)
        {
            var pipeline = (QualitySettings.renderPipeline ?? GraphicsSettings.currentRenderPipeline) as UniversalRenderPipelineAsset;
            if (!pipeline) throw new InvalidOperationException("An active URP 14 pipeline is required.");
            var pipelineObject = new SerializedObject(pipeline);
            // URP renders Scene View with the pipeline's default renderer, even when
            // the Main Camera selects a different renderer in its additional data.
            int index = camera ? new SerializedObject(camera.GetUniversalAdditionalCameraData())
                .FindProperty("m_RendererIndex").intValue : -1;
            if (index < 0) index = pipelineObject.FindProperty("m_DefaultRendererIndex").intValue;
            var renderers = pipelineObject.FindProperty("m_RendererDataList");
            if (index >= renderers.arraySize || index < 0) throw new InvalidOperationException("Invalid camera renderer index.");
            var renderer = renderers.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
            if (!renderer) throw new InvalidOperationException("Camera renderer is missing.");
            return renderer;
        }

        static void EnsureFeature<T>(ScriptableRendererData renderer, string name, string shaderField, string shaderName)
            where T : ScriptableRendererFeature
        {
            var feature = renderer.rendererFeatures.OfType<T>().FirstOrDefault();
            if (!feature)
            {
                feature = ScriptableObject.CreateInstance<T>();
                feature.name = name;
                Undo.RegisterCreatedObjectUndo(feature,"Install " + name);
                AssetDatabase.AddObjectToAsset(feature,renderer);
                renderer.rendererFeatures.Add(feature);
            }
            var serialized = new SerializedObject(feature);
            if (!string.IsNullOrEmpty(shaderField))
                serialized.FindProperty(shaderField).objectReferenceValue = Shader.Find(shaderName);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            feature.SetActive(true);
            var rendererObject = new SerializedObject(renderer);
            var map = rendererObject.FindProperty("m_RendererFeatureMap");
            map.arraySize = renderer.rendererFeatures.Count;
            for (int i=0;i<renderer.rendererFeatures.Count;i++)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i],out string _,out long id);
                map.GetArrayElementAtIndex(i).longValue = id;
            }
            rendererObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);
            renderer.SetDirty();
        }
    }
}
