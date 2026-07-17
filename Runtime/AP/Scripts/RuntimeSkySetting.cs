using System;
using System.Linq;
using UnityEngine;

using UnityEngine.Rendering;
namespace DawnTOD
{
    [ExecuteInEditMode]
    public class RuntimeSkySetting : MonoBehaviour

    {
        private const float SpaceEmissionTrackMaximum = 1000f;

        [Header("ScatteringSetting")]
        private float distanceScale = 1.0f;
        [HideInInspector]
        public Vector3 rCoef = new Vector3(5.8f, 13.5f, 33.1f);
        [HideInInspector]
        public float rScatterStrength = 1f;
        [HideInInspector]
        public float rExtinctionStrength = 1f;

        [HideInInspector]
        public Vector3 mCoef = new Vector3(2.0f, 2.0f, 2.0f);
        [HideInInspector]
        public float mScatterStrength = 1f;
        [HideInInspector]
        public float mExtinctionStrength = 1f;
        [HideInInspector]
        public float mieG = 0.625f;

        [Header("Environments")]
        private Light mainLight;

        [HideInInspector]
        [ColorUsage(false, true)]
        public Color lightFromOuterSpace = Color.white;

        [HideInInspector]
        [ColorUsage(false, true)]
        [Tooltip("Color behind atmosphere rays that hit the planet surface. Black preserves the current lower-hemisphere appearance.")]
        public Color atmosphereGroundColor = Color.black;

        [HideInInspector]
        public float planetRadius = 6357000.0f;
        [HideInInspector]
        public float atmosphereHeight = 12000f;
        [HideInInspector]
        public float surfaceHeight;

        [Header("Space")]
        [HideInInspector]
        [Tooltip("HDR cubemap rendered behind the atmosphere, equivalent to HDRP Space Emission Texture.")]
        public Cubemap spaceEmissionTexture;

        [HideInInspector]
        [Range(0f, SpaceEmissionTrackMaximum)]
        [Tooltip("Star Emission track value. URP maps 0-1000 here to a 0-1 shader multiplier.")]
        public float spaceEmissionMultiplier = SpaceEmissionTrackMaximum;

        [HideInInspector]
        [Tooltip("Euler rotation applied to the space emission cubemap.")]
        public Vector3 spaceRotation;


        [Header("Particles")]
        [HideInInspector]
        public float rDensityScale = 7994.0f;

        [HideInInspector]
        public float mDensityScale = 1200;

        [Header("Sun Disk")]
        [HideInInspector]
        public float sunDiskScale = 0.75f;

        [HideInInspector]
        [Range(-1, 1)]
        public float sunMieG = 0.99f;

        [Header("Precomputation")]
        [HideInInspector]
        public ComputeShader computerShader;
        private const string PrecomputationResourcePath = "Precomputation";
        private bool m_LoggedMissingComputerShader;

        private Vector2Int integrateCPDensityLUTSize = new Vector2Int(512, 512);
        private Vector2Int sunOnSurfaceLUTSize = new Vector2Int(512, 512);
        private int ambientLUTSize = 512;
        private Vector2Int inScatteringLUTSize = new Vector2Int(1024, 1024);

        [Header("Debug/Output")] [NonSerialized]
        private bool m_ShowFrustumCorners = false;

        [NonSerialized] [ColorUsage(false, true)]
        private Color m_MainLightColor;

        [NonSerialized] [ColorUsage(false, true)]
        private Color m_AmbientColor;

        // x : dot(-mianLightDir,worldUp)，y：height
        [NonSerialized]
        private RenderTexture m_IntegrateCPDensityLUT;

        // x : dot(-mianLightDir,worldUp)，y：height
        [NonSerialized]
        private RenderTexture m_SunOnSurfaceLUT;

        // x : dot(-mianLightDir,worldUp)，y：height
        [NonSerialized]
        private RenderTexture m_AmbientLUT;

        [NonSerialized]
        private RenderTexture m_InScatteringLUT;

        private Texture2D m_SunOnSurfaceLUTReadToCPU;
        private Texture2D m_HemiSphereRandomNormlizedVecLUT;
        private bool m_AmbientReadbackPending;
        private bool m_AmbientReadbackWarningLogged;

        private Camera m_Camera;
        private Vector3[] m_FrustumCorners = new Vector3[4];
        private Vector4[] m_FrustumCornersVec4 = new Vector4[4];

        private void UpdateParams(DawnAtmosphereVolume volume)
        {
            Shader.DisableKeyword(ScatteringKeys.kDebugExtinction);
            Shader.DisableKeyword(ScatteringKeys.kDebugInscattering);

            Shader.SetGlobalFloat(ScatteringKeys.kDistanceScale, distanceScale);
            //地球的数据：
            //private readonly Vector4 _rayleighSct = new Vector4(5.8f, 13.5f, 33.1f, 0.0f) * 0.000001f; 
            //private readonly Vector4 _mieSct = new Vector4(2.0f, 2.0f, 2.0f, 0.0f) * 0.00001f; 
            Vector3 effectiveRayleighCoef = Resolve(volume, volume?.rayleighCoefficients, rCoef);
            Vector3 effectiveMieCoef = Resolve(volume, volume?.mieCoefficients, mCoef);
            float effectiveRayleighScatter = Resolve(volume, volume?.rayleighScatterStrength, rScatterStrength);
            float effectiveMieScatter = Resolve(volume, volume?.mieScatterStrength, mScatterStrength);
            float effectiveRayleighExtinction = Resolve(volume, volume?.rayleighExtinctionStrength, rExtinctionStrength);
            float effectiveMieExtinction = Resolve(volume, volume?.mieExtinctionStrength, mExtinctionStrength);
            float effectiveMieG = Resolve(volume, volume?.mieAnisotropy, mieG);

            effectiveRayleighCoef *= 0.000001f;
            effectiveMieCoef *= 0.00001f;
            Shader.SetGlobalVector(ScatteringKeys.kScatteringR, effectiveRayleighCoef * effectiveRayleighScatter);
            Shader.SetGlobalVector(ScatteringKeys.kScatteringM, effectiveMieCoef * effectiveMieScatter);
            Shader.SetGlobalVector(ScatteringKeys.kExtinctionR, effectiveRayleighCoef * effectiveRayleighExtinction);
            Shader.SetGlobalVector(ScatteringKeys.kExtinctionM, effectiveMieCoef * effectiveMieExtinction);
            Shader.SetGlobalFloat(ScatteringKeys.kMieG, effectiveMieG);
        }

        private void SetCommonParams(DawnAtmosphereVolume volume)
        {

            FindAndSetDirectionalLight();
            
            Shader.SetGlobalTexture(ScatteringKeys.kIntergalCPDensityLUT, m_IntegrateCPDensityLUT);
            //Shader.SetGlobalTexture(Keys.kSunOnSurface, m_SunOnSurfaceLUT);
            float effectiveRayleighDensity = Resolve(volume, volume?.rayleighDensityScale, rDensityScale);
            float effectiveMieDensity = Resolve(volume, volume?.mieDensityScale, mDensityScale);
            Shader.SetGlobalVector(
                ScatteringKeys.kDensityScaleHeight,
                new Vector4(effectiveRayleighDensity, effectiveMieDensity));
            Shader.SetGlobalFloat(ScatteringKeys.kPlanetRadius, planetRadius);
            Shader.SetGlobalFloat(ScatteringKeys.kAtmosphereHeight, atmosphereHeight);
            Shader.SetGlobalFloat(ScatteringKeys.kSurfaceHeight, surfaceHeight);
            Shader.SetGlobalColor(
                ScatteringKeys.kAtmosphereGroundColor,
                Resolve(volume, volume?.groundColor, atmosphereGroundColor));
            ApplySpaceParams(volume);
            Shader.SetGlobalVector(ScatteringKeys.kIncomingLight, lightFromOuterSpace);
            Shader.SetGlobalFloat(
                ScatteringKeys.kSunIntensity,
                Resolve(volume, volume?.sunDiskScale, sunDiskScale));
            Shader.SetGlobalFloat(
                ScatteringKeys.kSunMieG,
                Resolve(volume, volume?.sunMieAnisotropy, sunMieG));
            
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
            }
            else
            {
                m_Camera.CalculateFrustumCorners(m_Camera.rect, m_Camera.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, m_FrustumCorners);
                for (int i = 0; i < 4; i++)
                {
                    m_FrustumCorners[i] = m_Camera.transform.TransformDirection(m_FrustumCorners[i]);
                    m_FrustumCornersVec4[i] = m_FrustumCorners[i];
                    if (m_ShowFrustumCorners)
                        Debug.DrawRay(m_Camera.transform.position, m_FrustumCorners[i], Color.blue);
                }

                Shader.SetGlobalVectorArray(ScatteringKeys.kFrustumCorners, m_FrustumCornersVec4);
            }
        }

        private void ApplySpaceParams(DawnAtmosphereVolume volume)
        {
            Cubemap effectiveTexture = Resolve(volume, volume?.spaceEmissionTexture, spaceEmissionTexture);
            float effectiveEmission = Resolve(volume, volume?.spaceEmission, spaceEmissionMultiplier);
            Vector3 effectiveRotation = Resolve(volume, volume?.spaceRotation, spaceRotation);

            Shader.SetGlobalTexture(ScatteringKeys.kSpaceEmissionTexture, effectiveTexture);
            Shader.SetGlobalFloat(
                ScatteringKeys.kSpaceEmissionMultiplier,
                effectiveTexture != null ? NormalizeSpaceEmission(effectiveEmission) : 0f);
            Shader.SetGlobalMatrix(
                ScatteringKeys.kSpaceRotationMatrix,
                Matrix4x4.Rotate(Quaternion.Inverse(Quaternion.Euler(SanitizeEuler(effectiveRotation)))));
        }

        public void SetSpaceEmissionMultiplier(float multiplier)
        {
            spaceEmissionMultiplier = SanitizeSpaceEmission(multiplier);
            ApplySpaceParams(GetActiveAtmosphereVolume());
        }

        private static DawnAtmosphereVolume GetActiveAtmosphereVolume()
        {
#if USING_URP
            VolumeStack stack = VolumeManager.instance.stack;
            return stack != null ? stack.GetComponent<DawnAtmosphereVolume>() : null;
#else
            return null;
#endif
        }

        private static float Resolve(
            DawnAtmosphereVolume volume,
            VolumeParameter<float> parameter,
            float fallback)
        {
            return CanOverride(volume, parameter) ? parameter.value : fallback;
        }

        private static Vector3 Resolve(
            DawnAtmosphereVolume volume,
            VolumeParameter<Vector3> parameter,
            Vector3 fallback)
        {
            return CanOverride(volume, parameter) ? parameter.value : fallback;
        }

        private static Color Resolve(
            DawnAtmosphereVolume volume,
            VolumeParameter<Color> parameter,
            Color fallback)
        {
            return CanOverride(volume, parameter) ? parameter.value : fallback;
        }

        private static Cubemap Resolve(
            DawnAtmosphereVolume volume,
            VolumeParameter<Texture> parameter,
            Cubemap fallback)
        {
            return CanOverride(volume, parameter) ? parameter.value as Cubemap : fallback;
        }

        private static bool CanOverride(DawnAtmosphereVolume volume, VolumeParameter parameter)
        {
            return volume != null && volume.active && parameter != null && parameter.overrideState;
        }

        private static float SanitizeSpaceEmission(float multiplier)
        {
            return float.IsNaN(multiplier) || float.IsInfinity(multiplier)
                ? 0f
                : Mathf.Clamp(multiplier, 0f, SpaceEmissionTrackMaximum);
        }

        private static float NormalizeSpaceEmission(float multiplier)
        {
            return SanitizeSpaceEmission(multiplier) / SpaceEmissionTrackMaximum;
        }

        private static Vector3 SanitizeEuler(Vector3 euler)
        {
            euler.x = float.IsNaN(euler.x) || float.IsInfinity(euler.x) ? 0f : euler.x;
            euler.y = float.IsNaN(euler.y) || float.IsInfinity(euler.y) ? 0f : euler.y;
            euler.z = float.IsNaN(euler.z) || float.IsInfinity(euler.z) ? 0f : euler.z;
            return euler;
        }



        private void PreComputeAll(DawnAtmosphereVolume volume)
        {
            if (!EnsureComputerShader())
            {
                if (!m_LoggedMissingComputerShader)
                {
                    Debug.LogWarning("Precomputation.compute could not be loaded from Resources/Precomputation. Assign a ComputeShader in the Inspector or ensure the package resource is imported.", this);
                    m_LoggedMissingComputerShader = true;
                }

                return;
            }

            m_LoggedMissingComputerShader = false;

            SetCommonParams(volume);
            ComputeIntegrateCPdensity();
            ComputeSunOnSurface();
            ComputeInScattering();
            ComputeHemiSphereRandomVectorLUT();
            ComputeAmbient();
        }

        private void ComputeIntegrateCPdensity()
        {
            ScatteringUtils.CheckOrCreateLUT(ref m_IntegrateCPDensityLUT, integrateCPDensityLUTSize, RenderTextureFormat.RGFloat);

            int index = computerShader.FindKernel("CSIntergalCPDensity");

            // Set Params
            computerShader.SetTexture(index, ScatteringKeys.kRWintergalCPDensityLUT, m_IntegrateCPDensityLUT);

            ScatteringUtils.Dispatch(computerShader, index, integrateCPDensityLUTSize);
        }

        //TODO need HDR format?
        private void ComputeSunOnSurface()
        {
            ScatteringUtils.CheckOrCreateLUT(ref m_SunOnSurfaceLUT, sunOnSurfaceLUTSize, RenderTextureFormat.DefaultHDR);

            int index = computerShader.FindKernel("CSsunOnSurface");

            // Set Params
            computerShader.SetTexture(index, ScatteringKeys.kRWsunOnSurfaceLUT, m_SunOnSurfaceLUT);
            computerShader.SetTexture(index, ScatteringKeys.kIntergalCPDensityLUT, m_IntegrateCPDensityLUT);

            ScatteringUtils.Dispatch(computerShader, index, sunOnSurfaceLUTSize);
        }

        //private void UpdateMainLight()
        //{
        //    if (mainLight == null) return;

        //    if (m_SunOnSurfaceLUTReadToCPU == null) m_SunOnSurfaceLUTReadToCPU = new Texture2D(m_SunOnSurfaceLUT.width, m_SunOnSurfaceLUT.height, TextureFormat.RGBAHalf, false, true);
        //    ScatteringUtils.ReadRTpixelsBackToCPU(m_SunOnSurfaceLUT, m_SunOnSurfaceLUTReadToCPU);

        //    var lightDir = -mainLight.transform.forward;
        //    var cosAngle01 = Vector3.Dot(Vector3.up, lightDir) * 0.5 + 0.5;
        //    var height01 = surfaceHeight / atmosphereHeight;

        //    var col = m_SunOnSurfaceLUTReadToCPU.GetPixel((int) (cosAngle01 * m_SunOnSurfaceLUTReadToCPU.width), (int) (height01 * m_SunOnSurfaceLUTReadToCPU.height));
        //    Color lightColor;
        //    float intensity;
        //    ScatteringUtils.HDRToColorIntendity(col, out lightColor, out intensity);

        //    mainLight.color = lightColor.gamma;
        //    mainLight.intensity = intensity;
        //    m_MainLightColor = col;
        //}

        private void ComputeInScattering()
        {
            // Need HDR?
            ScatteringUtils.CheckOrCreateLUT(ref m_InScatteringLUT, inScatteringLUTSize, RenderTextureFormat.DefaultHDR);

            int index = computerShader.FindKernel("CSInScattering");

            //Set Params
            computerShader.SetTexture(index, ScatteringKeys.kRWinScatteringLUT, m_InScatteringLUT);
            computerShader.SetTexture(index, ScatteringKeys.kIntergalCPDensityLUT, m_IntegrateCPDensityLUT);

            ScatteringUtils.Dispatch(computerShader, index, inScatteringLUTSize);
        }

        private void ComputeHemiSphereRandomVectorLUT()
        {
            if (m_HemiSphereRandomNormlizedVecLUT == null)
            {
                m_HemiSphereRandomNormlizedVecLUT = new Texture2D(512, 1, TextureFormat.RGB24, false, true);
                m_HemiSphereRandomNormlizedVecLUT.filterMode = FilterMode.Point;
                ;
                m_HemiSphereRandomNormlizedVecLUT.Apply();
                for (int i = 0; i < m_HemiSphereRandomNormlizedVecLUT.width; ++i)
                {
                    var randomVec = UnityEngine.Random.onUnitSphere;
                    m_HemiSphereRandomNormlizedVecLUT.SetPixel(i, 0, new Color(randomVec.x, Mathf.Abs(randomVec.y), randomVec.z));
                }
            }
        }

        private void ComputeAmbient()
        {
            var size = new Vector2Int(ambientLUTSize, 1);
            ScatteringUtils.CheckOrCreateLUT(ref m_AmbientLUT, size, RenderTextureFormat.DefaultHDR);

            int index = computerShader.FindKernel("CSAmbient");

            //Set Params
            computerShader.SetTexture(index, ScatteringKeys.kRWhemiSphereRandomNormlizedVecLUT, m_HemiSphereRandomNormlizedVecLUT);
            computerShader.SetTexture(index, ScatteringKeys.kInScatteringLUT, m_InScatteringLUT);
            computerShader.SetTexture(index, ScatteringKeys.kRWambientLUT, m_AmbientLUT);

            ScatteringUtils.Dispatch(computerShader, index, size);
        }

        private void UpdateAmbient()
        {
            if (m_AmbientLUT == null || !m_AmbientLUT.IsCreated() || m_AmbientReadbackPending)
                return;

            FindAndSetDirectionalLight();
            if (mainLight == null)
                return;

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                if (!m_AmbientReadbackWarningLogged)
                {
                    Debug.LogWarning("Async GPU readback is not supported; ambient lighting cannot be updated from the scattering LUT.", this);
                    m_AmbientReadbackWarningLogged = true;
                }
                return;
            }

            var lightDir = -mainLight.transform.forward;
            var cosAngle01 = Mathf.Clamp01(Vector3.Dot(Vector3.up, lightDir) * 0.5f + 0.5f);
            m_AmbientReadbackPending = true;

            if (!ScatteringUtils.RequestRTpixelsBackToCPU(
                    m_AmbientLUT, TextureFormat.RGBA32,
                    request => OnAmbientReadbackCompleted(request, cosAngle01)))
            {
                m_AmbientReadbackPending = false;
            }
        }

        private void OnAmbientReadbackCompleted(AsyncGPUReadbackRequest request, float cosAngle01)
        {
            if (this == null)
                return;

            m_AmbientReadbackPending = false;
            if (!isActiveAndEnabled || request.hasError)
                return;

            var pixels = request.GetData<Color32>();
            if (pixels.Length == 0)
                return;

            var pixelIndex = Mathf.Clamp(Mathf.FloorToInt(cosAngle01 * pixels.Length), 0, pixels.Length - 1);
            var ambient = (Color)pixels[pixelIndex];
            RenderSettings.ambientLight = ambient.gamma;
            m_AmbientColor = ambient;
        }


        private bool EnsureComputerShader()
        {
            if (computerShader != null)
                return true;

            computerShader = Resources.Load<ComputeShader>(PrecomputationResourcePath);
            return computerShader != null;
        }

        private void Awake()
        {
            m_Camera = Camera.main;
            
            FindAndSetDirectionalLight();
            
            EnsureComputerShader();
            SetSkyboxMaterial();
        }

        private void OnEnable()
        {
            EnsureComputerShader();
            ApplySpaceParams(GetActiveAtmosphereVolume());
        }

        private void OnValidate()
        {
            spaceEmissionMultiplier = SanitizeSpaceEmission(spaceEmissionMultiplier);
            ApplySpaceParams(GetActiveAtmosphereVolume());
        }

        private void SetSkyboxMaterial()
        {
            var materials = Resources.FindObjectsOfTypeAll<Material>();
            Material dawnRuntimeAPMaterial = null;
            
            foreach (var mat in materials)
            {
                if (mat.name == "DawnRuntimeAP")
                {
                    dawnRuntimeAPMaterial = mat;
                    break;
                }
            }
            if (dawnRuntimeAPMaterial == null)
            {
                dawnRuntimeAPMaterial = Resources.Load<Material>("DawnRuntimeAP");
            }
            if (dawnRuntimeAPMaterial != null)
            {
                RenderSettings.skybox = dawnRuntimeAPMaterial;
            }
            else
            {
                Debug.LogWarning("DawnRuntimeAP material not found. Please make sure the material exists in the project.");
            }
        }

        private void FindAndSetDirectionalLight()
        {
            if (DawnTODSystem.Instance != null)
            {
                mainLight = DawnTODSystem.Instance.GetMainDirectionalLight();
                if (mainLight == null)
                {
                    Debug.LogError("Error: No directional light found in DawnTODSystem!");
                    return;
                }
            }
            else
            {
                mainLight = FindObjectsOfType<Light>().FirstOrDefault(light => light.type == LightType.Directional);
                if (mainLight == null)
                {
                    Debug.LogError("Error: No directional light found in the scene!");
                    return;
                }
            }
            lightFromOuterSpace = mainLight.color * mainLight.intensity;
        }

        private void OnDisable()
        {
            if (m_IntegrateCPDensityLUT != null) m_IntegrateCPDensityLUT.Release();
            Shader.SetGlobalFloat(ScatteringKeys.kSpaceEmissionMultiplier, 0f);
        }

        private void Update()
        {
            DawnAtmosphereVolume atmosphereVolume = GetActiveAtmosphereVolume();
            FindAndSetDirectionalLight();
            UpdateParams(atmosphereVolume);
            SetCommonParams(atmosphereVolume);
            PreComputeAll(atmosphereVolume);
            //UpdateMainLight();
            UpdateAmbient();
        }



    }
}
