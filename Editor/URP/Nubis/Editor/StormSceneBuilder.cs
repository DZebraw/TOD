using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NubisStorm.Editor
{
    public static class StormSceneBuilder
    {
        const string Root = "Assets/NubisCloud";
        public const string ScenePath = Root + "/Scenes/Superstorm.unity";
        public const string SkyScenePath = Root + "/Scenes/SkyClouds.unity";

        [MenuItem("Tools/Nubis Storm/Create or rebuild demo scene")]
        public static void CreateScene() => CreateDemo(true);

        [MenuItem("Tools/Nubis Clouds/Create or rebuild sky scene")]
        public static void CreateSkyScene() => CreateDemo(false);

        static void CreateDemo(bool stormEnabled)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Root + "/Generated");
            Directory.CreateDirectory(Root + "/Scenes");
            AssetDatabase.Refresh();
            // NewScene unloads unused asset dependencies; load the model only after that transition.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cloudData = NubisCloudAssetBuilder.Build();
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Generated/Rendering/StormRenderer.asset");
            if (!renderer)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "StormRenderer";
                AssetDatabase.CreateAsset(renderer, Root + "/Generated/Rendering/StormRenderer.asset");
                var feature = ScriptableObject.CreateInstance<NubisStormRendererFeature>();
                feature.name = "Nubis Superstorm";
                var serialized = new SerializedObject(feature);
                serialized.FindProperty("stormShader").objectReferenceValue = Shader.Find("Hidden/NubisStorm/Volume");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
                renderer.SetDirty();
                EditorUtility.SetDirty(renderer);
            }
            // A programmatically created renderer does not automatically receive PostProcessData.
            var template = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP-HighFidelity-Renderer.asset");
            if (template)
            {
                var source = new SerializedObject(template);
                var target = new SerializedObject(renderer);
                target.FindProperty("postProcessData").objectReferenceValue = source.FindProperty("postProcessData").objectReferenceValue;
                var map = target.FindProperty("m_RendererFeatureMap");
                map.arraySize = renderer.rendererFeatures.Count;
                for (int i = 0; i < renderer.rendererFeatures.Count; i++)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string _, out long localId);
                    map.GetArrayElementAtIndex(i).longValue = localId;
                }
                target.ApplyModifiedPropertiesWithoutUndo();
                renderer.SetDirty();
                EditorUtility.SetDirty(renderer);
            }
            // Register a dedicated renderer without replacing any existing default renderer.
            int rendererIndex = -1;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                var serialized = new SerializedObject(pipeline);
                var list = serialized.FindProperty("m_RendererDataList");
                int found = -1;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) found = i;
                if (found < 0)
                {
                    found = list.arraySize;
                    list.InsertArrayElementAtIndex(found);
                    list.GetArrayElementAtIndex(found).objectReferenceValue = renderer;
                }
                if (rendererIndex >= 0 && rendererIndex != found)
                    throw new InvalidOperationException("Storm renderer index differs between quality pipelines.");
                rendererIndex = found;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pipeline);
            }
            if (rendererIndex < 0) throw new InvalidOperationException("No URP pipeline found in Assets/Settings.");
            var storm = new GameObject(stormEnabled ? "Cloud model · sky + storm influence" : "Cloud model · NDF sky").AddComponent<NubisStormVolume>();
            storm.transform.position = new Vector3(0, 900, 0);
            storm.cloudData = cloudData;
            storm.stormNdf = cloudData.superstormNdf;
            storm.fieldVelocity = new Vector2(6,2);
            storm.stormStrength = stormEnabled ? 1 : 0;

            var sun = new GameObject("Sun · grazing cloud light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(38, -30, 0);
            sun.color = new Color(1, 0.96f, 0.90f);
            sun.intensity = 1.2f;
            storm.sun = sun;

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 300000;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.gameObject.AddComponent<AudioListener>();
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(rendererIndex);
            cameraData.requiresDepthTexture = true;
            cameraData.renderPostProcessing = true;
            var controller = camera.gameObject.AddComponent<StormCameraController>();
            controller.storm = storm;
            controller.SetView(0);

            var sky = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Generated/Materials/CloudAtmosphere.mat");
            if (!sky)
            {
                sky = new Material(Shader.Find("NubisStorm/Atmosphere"));
                AssetDatabase.CreateAsset(sky, Root + "/Generated/Materials/CloudAtmosphere.mat");
            }
            sky.SetColor("_Zenith", new Color(0.09f,0.23f,0.40f));
            sky.SetColor("_Horizon", new Color(0.52f,0.67f,0.79f));
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.27f, 0.34f);
            RenderSettings.fog = false;

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/Generated/Rendering/StormPost.asset");
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, Root + "/Generated/Rendering/StormPost.asset");
                var bloom = profile.Add<Bloom>(true);
                bloom.intensity.Override(0.18f);
                bloom.threshold.Override(1.1f);
                bloom.scatter.Override(0.65f);
                var tonemap = profile.Add<Tonemapping>(true);
                tonemap.mode.Override(TonemappingMode.Neutral);
                foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(profile);
            }
            var volume = new GameObject("Atmosphere grading").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, stormEnabled ? ScenePath : SkyScenePath);
            Selection.activeGameObject = storm.gameObject;
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0, 2300, 0), camera.transform.rotation, 5000);
            Debug.Log("Nubis: created " + (stormEnabled ? ScenePath : SkyScenePath));
        }

        [MenuItem("Tools/Nubis Storm/Open demo scene")]
        public static void OpenScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Nubis Clouds/Open sky scene")]
        public static void OpenSkyScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(SkyScenePath);
        }

        [MenuItem("Tools/Nubis Storm/Capture both views")]
        public static void CaptureViews()
        {
            var camera = Camera.main;
            var controller = camera ? camera.GetComponent<StormCameraController>() : null;
            if (!controller) throw new InvalidOperationException("Open the Superstorm scene first.");
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../CloudCaptures"));
            Directory.CreateDirectory(output);
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            var storm = controller.storm;
            if (!storm.IsReady || NubisStormVolume.Active != storm)
                throw new InvalidOperationException("Cloud model is not ready for capture. Ready=" + storm.IsReady + " active=" + NubisStormVolume.Active + " expected=" + storm);
            bool animate = storm.animate;
            float frozen = storm.frozenTime;
            if (animate && Application.isPlaying) storm.frozenTime = storm.AnimationTime;
            storm.animate = false;
            try
            {
                for (int view = 0; view < (storm.stormStrength > 0 ? 2 : 3); view++)
                {
                    controller.SetView(view);
                    string prefix = storm.stormStrength > 0 ? "Storm" : "Sky";
                    Capture(camera, Path.Combine(output, prefix + (view == 0 ? "-Underneath.png" : view == 1 ? "-Overview.png" : "-Horizon.png")));
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                storm.animate = animate;
                storm.frozenTime = frozen;
            }
            Debug.Log("Nubis cloud captures: " + output);
        }

        static void CaptureStages()
        {
            var camera = Camera.main;
            var cloud = NubisStormVolume.Active;
            camera.GetComponent<StormCameraController>().SetView(1);
            var cameraData = camera.GetUniversalAdditionalCameraData();
            bool post = cameraData.renderPostProcessing;
            cameraData.renderPostProcessing = false;
            string output = Path.GetFullPath(Application.dataPath + "/../CloudCaptures");
            foreach (var mode in new[] { NubisDebugView.DimensionalProfile, NubisDebugView.Density, NubisDebugView.Coverage, NubisDebugView.NoiseComposite })
            {
                cloud.debugView = mode;
                Capture(camera, Path.Combine(output, "Sky-Stage-" + mode + ".png"));
            }
            cloud.debugView = NubisDebugView.Lit;
            cameraData.renderPostProcessing = post;
        }

        public static void Capture(Camera camera, string path)
        {
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            var previous = RenderTexture.active;
            var originalTarget = camera.targetTexture;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.aspect = target.width / (float)target.height;
                var cloud = NubisStormVolume.Active;
                int frames = cloud && cloud.temporalReconstruction && cloud.debugView == NubisDebugView.Lit ? 17 : 1;
                for (int frame = 0; frame < frames; frame++) camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = originalTarget;
                camera.ResetAspect();
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixels);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        // Batch entrypoint: also useful for regenerating deterministic review screenshots.
        public static void BuildAndCapture()
        {
            try
            {
                CreateSkyScene();
                NubisCloudAssetBuilder.ExportPreviews();
                CaptureViews();
                CaptureStages();
                CreateScene();
                CaptureViews();
                foreach (string name in new[] { "Hidden/NubisStorm/Volume", "NubisStorm/Atmosphere" })
                {
                    var shader = Shader.Find(name);
                    var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
                    if (errors.Length > 0) throw new Exception(string.Join("\n", errors.Select(m => m.message)));
                }
                Debug.Log("NUBIS_VALIDATION_OK");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }
    }
}
