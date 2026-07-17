using System;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace DawnTOD
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [ExecuteAlways]
    public class DawnGPUParticleSystem : MonoBehaviour
    {
        private const string DefaultComputeResourcePath = "RainyParticleUpdate";
        private const string DefaultMaterialResourcePath = "DawnRain";
        private const string UpdateKernelName = "CSMain";
        private const int ThreadGroupSize = 8;
        private const float EditorDeltaTime = 0.02f;

        private static DawnGPUParticleSystem instance;

        public static DawnGPUParticleSystem Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<DawnGPUParticleSystem>(true);
                }

                return instance;
            }
        }

        [Header("Rain State")]
        [Tooltip("Whether the rain output should currently render and simulate.")]
        public bool ParticleShow;
        [Min(0f)] public float baseFallSpeed = 40f;
        [Min(0f)] public float rainDensity = 1f;
        [Range(-45f, 45f)] public float rainWindZRotation;

        [Header("Particle Capacity")]
        [Tooltip("GPU state texture dimensions. Values are rounded to powers of two and clamped to 8-2048.")]
        public Vector2Int maxParticlesCount = new Vector2Int(512, 512);
        [Tooltip("Horizontal size of the rain emitter around the active camera.")]
        public Vector2 emitterSize = new Vector2(35f, 35f);

        [Header("Runtime Resources")]
        [Tooltip("Optional override. When empty, Resources/RainyParticleUpdate is used.")]
        [SerializeField] private ComputeShader rainyParticleUpdateCS;
        [Tooltip("Optional override. When empty, Resources/DawnRain is used.")]
        [SerializeField] private Material rainMaterialTemplate;

        private Mesh particlesMesh;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock materialProperties;

        private ComputeShader runtimeParticleUpdateCS;
        private RenderTexture rainyParticleStateRT0;
        private RenderTexture rainyParticleStateRT1;
        private int updateKernel = -1;
        private Vector2Int allocatedParticleCount;
        private Vector2 allocatedEmitterSize;

        private readonly float yOffset = 50f;
        private Camera mainCamera;
        private bool initializationFailed;
        private bool warnedMissingCamera;

        private bool IsEditorMode => !Application.isPlaying;
        internal bool HasAllocatedResources =>
            particlesMesh != null ||
            rainyParticleStateRT0 != null ||
            rainyParticleStateRT1 != null ||
            runtimeParticleUpdateCS != null;

        private void Reset()
        {
            CacheComponents();
            AssignDefaultResourceOverrides();
            if (meshRenderer != null && meshRenderer.sharedMaterial == null)
            {
                meshRenderer.sharedMaterial = rainMaterialTemplate;
            }

            SetRendererVisible(false);
        }

        private void Awake()
        {
            CacheComponents();
            RegisterCompatibilityInstance();
        }

        private void OnEnable()
        {
            CacheComponents();
            RegisterCompatibilityInstance();
            CacheMainCamera();
            initializationFailed = false;
            warnedMissingCamera = false;
            SetRendererVisible(false);
        }

        private void Update()
        {
            CacheComponents();
            if (!ParticleShow)
            {
                SetRendererVisible(false);
                return;
            }

            if (!TryEnsureInitialized())
            {
                SetRendererVisible(false);
                return;
            }

            SetRendererVisible(true);
            SyncWithMainCamera();
            UpdateGPUSimulation(IsEditorMode ? EditorDeltaTime : Time.deltaTime);
        }

        private void OnDisable()
        {
            SetRendererVisible(false);
            ReleaseOwnedResources();
            if (instance == this)
            {
                instance = null;
            }
        }

        private void OnDestroy()
        {
            ReleaseOwnedResources();
            if (instance == this)
            {
                instance = null;
            }
        }

        public void SetRainState(
            bool show,
            float fallSpeed,
            float density,
            float windZRotation)
        {
            ParticleShow = show;
            baseFallSpeed = Mathf.Max(0f, fallSpeed);
            rainDensity = Mathf.Max(0f, density);
            rainWindZRotation = Mathf.Clamp(windZRotation, -45f, 45f);
            if (!show)
            {
                SetRendererVisible(false);
            }
        }

        internal void ConfigureResources(
            ComputeShader computeShader,
            Material materialTemplate)
        {
            if (rainyParticleUpdateCS == computeShader &&
                rainMaterialTemplate == materialTemplate)
            {
                return;
            }

            ReleaseOwnedResources();
            rainyParticleUpdateCS = computeShader;
            rainMaterialTemplate = materialTemplate;
            initializationFailed = false;
            CacheComponents();
            if (meshRenderer != null && materialTemplate != null)
            {
                meshRenderer.sharedMaterial = materialTemplate;
            }
        }

        internal void AssignDefaultResourceOverrides()
        {
            if (rainyParticleUpdateCS == null)
            {
                rainyParticleUpdateCS =
                    Resources.Load<ComputeShader>(DefaultComputeResourcePath);
            }

            if (rainMaterialTemplate == null)
            {
                rainMaterialTemplate =
                    Resources.Load<Material>(DefaultMaterialResourcePath);
            }

            CacheComponents();
            if (meshRenderer != null &&
                meshRenderer.sharedMaterial == null &&
                rainMaterialTemplate != null)
            {
                meshRenderer.sharedMaterial = rainMaterialTemplate;
            }

            initializationFailed = false;
        }

        internal bool TryEnsureInitialized()
        {
            SanitizeSettings();
            if (IsConfigurationCurrent())
            {
                return true;
            }

            ReleaseOwnedResources();
            if (initializationFailed)
            {
                return false;
            }

            CacheComponents();
            ComputeShader sourceCompute = rainyParticleUpdateCS != null
                ? rainyParticleUpdateCS
                : Resources.Load<ComputeShader>(DefaultComputeResourcePath);
            Material sourceMaterial = rainMaterialTemplate != null
                ? rainMaterialTemplate
                : Resources.Load<Material>(DefaultMaterialResourcePath);

            if (meshFilter == null || meshRenderer == null)
            {
                return FailInitialization(
                    "Rain output requires both MeshFilter and MeshRenderer components.");
            }

            if (sourceCompute == null)
            {
                return FailInitialization(
                    "Missing rain ComputeShader. Assign an override or restore Resources/RainyParticleUpdate.compute.");
            }

            if (sourceMaterial == null || sourceMaterial.shader == null)
            {
                return FailInitialization(
                    "Missing rain Material. Assign an override or restore Resources/DawnRain.mat.");
            }

            if (!SystemInfo.supportsComputeShaders)
            {
                return FailInitialization(
                    "The current graphics device does not support Compute Shaders.");
            }

            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(
                    RenderTextureFormat.ARGBFloat))
            {
                return FailInitialization(
                    "The current graphics device does not support random-write ARGBFloat RenderTextures.");
            }

            try
            {
                runtimeParticleUpdateCS = Instantiate(sourceCompute);
                runtimeParticleUpdateCS.name = sourceCompute.name + " (Dawn Rain Instance)";
                runtimeParticleUpdateCS.hideFlags = HideFlags.HideAndDontSave;
                updateKernel = runtimeParticleUpdateCS.FindKernel(UpdateKernelName);

                particlesMesh = GenerateParticlesMeshInternal(
                    maxParticlesCount.x,
                    maxParticlesCount.y,
                    emitterSize);
                particlesMesh.hideFlags = HideFlags.HideAndDontSave;
                meshFilter.sharedMesh = particlesMesh;

                rainyParticleStateRT0 = CreateStateTexture("Dawn Rain State 0");
                rainyParticleStateRT1 = CreateStateTexture("Dawn Rain State 1");
                if (!rainyParticleStateRT0.IsCreated() ||
                    !rainyParticleStateRT1.IsCreated())
                {
                    throw new InvalidOperationException(
                        "Unity failed to create the rain state RenderTextures.");
                }

                // The serialized template is authoritative. Per-instance values stay in
                // the MaterialPropertyBlock, so assigning it never mutates the asset.
                meshRenderer.sharedMaterial = sourceMaterial;

                allocatedParticleCount = maxParticlesCount;
                allocatedEmitterSize = emitterSize;
                InitializeGPUSimulation();
                UpdateMaterialProperties();
                initializationFailed = false;
                return true;
            }
            catch (Exception exception)
            {
                ReleaseOwnedResources();
                return FailInitialization(
                    $"Rain GPU initialization failed: {exception.Message}");
            }
        }

        public void ValidateAndGenerateMesh()
        {
            SanitizeSettings();
            CacheComponents();
            ReleaseOwnedResources();
            particlesMesh = GenerateParticlesMeshInternal(
                maxParticlesCount.x,
                maxParticlesCount.y,
                emitterSize);
            particlesMesh.hideFlags = HideFlags.HideAndDontSave;
            meshFilter.sharedMesh = particlesMesh;
        }

        private void RegisterCompatibilityInstance()
        {
            if (instance == null)
            {
                instance = this;
                return;
            }

            if (instance != this && Application.isPlaying)
            {
                Debug.LogWarning(
                    "Multiple DawnGPUParticleSystem components are active. " +
                    "DawnTOD uses its explicit Rain Output reference; the compatibility Instance returns the first active component.",
                    this);
            }
        }

        private void CacheComponents()
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
            }

            if (meshRenderer == null)
            {
                meshRenderer = GetComponent<MeshRenderer>();
            }
        }

        private void CacheMainCamera()
        {
            mainCamera = Camera.main;
        }

        private void SyncWithMainCamera()
        {
            Camera currentMainCamera = Camera.main;
            if (currentMainCamera != null && currentMainCamera != mainCamera)
            {
                mainCamera = currentMainCamera;
            }

            if (mainCamera == null)
            {
                CacheMainCamera();
                if (mainCamera == null)
                {
                    if (!warnedMissingCamera)
                    {
                        warnedMissingCamera = true;
                        Debug.LogWarning(
                            "Dawn rain is active but no enabled camera with the MainCamera tag was found.",
                            this);
                    }

                    return;
                }
            }

            warnedMissingCamera = false;
            Vector3 cameraPosition = mainCamera.transform.position;
            transform.position = new Vector3(
                cameraPosition.x,
                cameraPosition.y + yOffset,
                cameraPosition.z);
        }

        private bool IsConfigurationCurrent()
        {
            return runtimeParticleUpdateCS != null &&
                   particlesMesh != null &&
                   rainyParticleStateRT0 != null &&
                   rainyParticleStateRT1 != null &&
                   allocatedParticleCount == maxParticlesCount &&
                   allocatedEmitterSize == emitterSize;
        }

        private void InitializeGPUSimulation()
        {
            runtimeParticleUpdateCS.SetInts(
                "ParticleCount",
                maxParticlesCount.x,
                maxParticlesCount.y);
            runtimeParticleUpdateCS.SetVector("EmitterSize", emitterSize);
            runtimeParticleUpdateCS.SetFloat("BaseFallSpeed", baseFallSpeed);
            runtimeParticleUpdateCS.SetFloat("RainDensity", rainDensity);
            runtimeParticleUpdateCS.SetTexture(
                updateKernel,
                "ParticleState",
                rainyParticleStateRT0);
            runtimeParticleUpdateCS.SetTexture(
                updateKernel,
                "Result",
                rainyParticleStateRT0);
            runtimeParticleUpdateCS.SetFloat("DeltaTime", 0f);
            DispatchSimulation();
        }

        private void UpdateGPUSimulation(float deltaTime)
        {
            if (!IsConfigurationCurrent())
            {
                return;
            }

            runtimeParticleUpdateCS.SetFloat("BaseFallSpeed", baseFallSpeed);
            runtimeParticleUpdateCS.SetFloat("RainDensity", rainDensity);
            runtimeParticleUpdateCS.SetFloat("DeltaTime", Mathf.Max(0f, deltaTime));
            runtimeParticleUpdateCS.SetTexture(
                updateKernel,
                "ParticleState",
                rainyParticleStateRT0);
            runtimeParticleUpdateCS.SetTexture(
                updateKernel,
                "Result",
                rainyParticleStateRT1);
            DispatchSimulation();

            (rainyParticleStateRT0, rainyParticleStateRT1) =
                (rainyParticleStateRT1, rainyParticleStateRT0);
            UpdateMaterialProperties();
        }

        private void DispatchSimulation()
        {
            int groupsX = Mathf.CeilToInt(
                maxParticlesCount.x / (float)ThreadGroupSize);
            int groupsY = Mathf.CeilToInt(
                maxParticlesCount.y / (float)ThreadGroupSize);
            runtimeParticleUpdateCS.Dispatch(updateKernel, groupsX, groupsY, 1);
        }

        private void UpdateMaterialProperties()
        {
            materialProperties ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(materialProperties);
            materialProperties.SetTexture(
                "_ParticleStateRT",
                rainyParticleStateRT0);
            materialProperties.SetFloat("_RainDensity", rainDensity);
            materialProperties.SetFloat("_WindZRotation", rainWindZRotation);
            meshRenderer.SetPropertyBlock(materialProperties);
        }

        private RenderTexture CreateStateTexture(string textureName)
        {
            var texture = new RenderTexture(
                maxParticlesCount.x,
                maxParticlesCount.y,
                0,
                RenderTextureFormat.ARGBFloat)
            {
                name = textureName,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
            return texture;
        }

        private void ReleaseOwnedResources()
        {
            SetRendererVisible(false);
            if (meshRenderer != null)
            {
                meshRenderer.SetPropertyBlock(null);
            }

            materialProperties?.Clear();

            if (meshFilter != null && meshFilter.sharedMesh == particlesMesh)
            {
                meshFilter.sharedMesh = null;
            }

            DestroyOwnedObject(rainyParticleStateRT0, releaseRenderTexture: true);
            DestroyOwnedObject(rainyParticleStateRT1, releaseRenderTexture: true);
            DestroyOwnedObject(particlesMesh);
            DestroyOwnedObject(runtimeParticleUpdateCS);

            rainyParticleStateRT0 = null;
            rainyParticleStateRT1 = null;
            particlesMesh = null;
            runtimeParticleUpdateCS = null;
            updateKernel = -1;
            allocatedParticleCount = default;
            allocatedEmitterSize = default;
        }

        private static void DestroyOwnedObject(
            UnityEngine.Object ownedObject,
            bool releaseRenderTexture = false)
        {
            if (ownedObject == null)
            {
                return;
            }

            if (releaseRenderTexture && ownedObject is RenderTexture renderTexture)
            {
                renderTexture.Release();
            }

            if (Application.isPlaying)
            {
                Destroy(ownedObject);
            }
            else
            {
                DestroyImmediate(ownedObject);
            }
        }

        private bool FailInitialization(string message)
        {
            if (!initializationFailed)
            {
                Debug.LogWarning(message, this);
            }

            initializationFailed = true;
            return false;
        }

        private void SetRendererVisible(bool visible)
        {
            if (meshRenderer != null)
            {
                meshRenderer.enabled = visible;
            }
        }

        private void SanitizeSettings()
        {
            maxParticlesCount.x = Mathf.Clamp(
                Mathf.ClosestPowerOfTwo(maxParticlesCount.x),
                ThreadGroupSize,
                2048);
            maxParticlesCount.y = Mathf.Clamp(
                Mathf.ClosestPowerOfTwo(maxParticlesCount.y),
                ThreadGroupSize,
                2048);
            emitterSize.x = Mathf.Max(0.01f, emitterSize.x);
            emitterSize.y = Mathf.Max(0.01f, emitterSize.y);
            baseFallSpeed = Mathf.Max(0f, baseFallSpeed);
            rainDensity = Mathf.Max(0f, rainDensity);
            rainWindZRotation = Mathf.Clamp(rainWindZRotation, -45f, 45f);
        }

        private Mesh GenerateParticlesMeshInternal(
            int countX,
            int countZ,
            Vector2 meshSize)
        {
            var mesh = new Mesh
            {
                name = "SimplifiedGPUParticleMesh",
                indexFormat = countX * countZ > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };

            var allVertices = new Vector3[countX * countZ * 4];
            var allTriangles = new int[countX * countZ * 6];
            var allUVs = new Vector2[countX * countZ * 4];
            var allColors = new Color[countX * countZ * 4];

            float xStep = meshSize.x / countX;
            float zStep = meshSize.y / countZ;
            int vertexIndex = 0;
            int triangleIndex = 0;

            for (int z = 0; z < countZ; z++)
            {
                for (int x = 0; x < countX; x++)
                {
                    Vector3 particleCenter = new Vector3(
                        x * xStep - meshSize.x * 0.5f,
                        0f,
                        z * zStep - meshSize.y * 0.5f);
                    allVertices[vertexIndex] = particleCenter;
                    allVertices[vertexIndex + 1] = particleCenter;
                    allVertices[vertexIndex + 2] = particleCenter;
                    allVertices[vertexIndex + 3] = particleCenter;

                    allUVs[vertexIndex] = new Vector2(0f, 0f);
                    allUVs[vertexIndex + 1] = new Vector2(1f, 0f);
                    allUVs[vertexIndex + 2] = new Vector2(0f, 1f);
                    allUVs[vertexIndex + 3] = new Vector2(1f, 1f);

                    var particleUV = new Color(
                        (float)x / countX,
                        (float)z / countZ,
                        0f,
                        Random.value);
                    allColors[vertexIndex] = particleUV;
                    allColors[vertexIndex + 1] = particleUV;
                    allColors[vertexIndex + 2] = particleUV;
                    allColors[vertexIndex + 3] = particleUV;

                    allTriangles[triangleIndex] = vertexIndex;
                    allTriangles[triangleIndex + 1] = vertexIndex + 2;
                    allTriangles[triangleIndex + 2] = vertexIndex + 1;
                    allTriangles[triangleIndex + 3] = vertexIndex + 1;
                    allTriangles[triangleIndex + 4] = vertexIndex + 2;
                    allTriangles[triangleIndex + 5] = vertexIndex + 3;

                    vertexIndex += 4;
                    triangleIndex += 6;
                }
            }

            mesh.vertices = allVertices;
            mesh.triangles = allTriangles;
            mesh.uv = allUVs;
            mesh.colors = allColors;
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(
                Vector3.zero,
                new Vector3(meshSize.x, 100f, meshSize.y));
            return mesh;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireCube(
                transform.position,
                new Vector3(emitterSize.x, 0f, emitterSize.y));
        }

        private void OnValidate()
        {
            SanitizeSettings();
            initializationFailed = false;
        }
#endif
    }
}
