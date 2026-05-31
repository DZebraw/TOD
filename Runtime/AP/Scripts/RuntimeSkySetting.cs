using System;
using System.Linq;
using UnityEngine;

namespace DawnTOD
{
    [ExecuteInEditMode]
    public class RuntimeSkySetting : MonoBehaviour

    {
        [Header("ScatteringSetting")]
        private float distanceScale = 1.0f;
        public Vector3 rCoef = new Vector3(5.8f, 13.5f, 33.1f);
        public float rScatterStrength = 1f;
        public float rExtinctionStrength = 1f;

        public Vector3 mCoef = new Vector3(2.0f, 2.0f, 2.0f);
        public float mScatterStrength = 1f;
        public float mExtinctionStrength = 1f;
        public float mieG = 0.625f;

        [Header("Environments")]
        private Light mainLight;

        [ColorUsage(false, true)]
        public Color lightFromOuterSpace = Color.white;

        public float planetRadius = 6357000.0f;
        public float atmosphereHeight = 12000f;
        public float surfaceHeight;


        [Header("Particles")]
        public float rDensityScale = 7994.0f;

        public float mDensityScale = 1200;

        [Header("Sun Disk")]
        public float sunDiskScale = 0.75f;

        [Range(-1, 1)]
        public float sunMieG = 0.99f;

        [Header("Precomputation")]
        public ComputeShader computerShader;

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
        private Texture2D m_AmbientLUTReadToCPU;

        private Camera m_Camera;
        private Vector3[] m_FrustumCorners = new Vector3[4];
        private Vector4[] m_FrustumCornersVec4 = new Vector4[4];

        private void UpdateParams()
        {
            Shader.DisableKeyword(ScatteringKeys.kDebugExtinction);
            Shader.DisableKeyword(ScatteringKeys.kDebugInscattering);

            Shader.SetGlobalFloat(ScatteringKeys.kDistanceScale, distanceScale);
            //地球的数据：
            //private readonly Vector4 _rayleighSct = new Vector4(5.8f, 13.5f, 33.1f, 0.0f) * 0.000001f; 
            //private readonly Vector4 _mieSct = new Vector4(2.0f, 2.0f, 2.0f, 0.0f) * 0.00001f; 
            var rCoef = this.rCoef * 0.000001f;
            var mCoef = this.mCoef * 0.00001f;
            Shader.SetGlobalVector(ScatteringKeys.kScatteringR, rCoef * rScatterStrength);
            Shader.SetGlobalVector(ScatteringKeys.kScatteringM, mCoef * mScatterStrength);
            Shader.SetGlobalVector(ScatteringKeys.kExtinctionR, rCoef * rExtinctionStrength);
            Shader.SetGlobalVector(ScatteringKeys.kExtinctionM, mCoef * mExtinctionStrength);
            Shader.SetGlobalFloat(ScatteringKeys.kMieG, mieG);
        }

        private void SetCommonParams()
        {

            FindAndSetDirectionalLight();
            
            Shader.SetGlobalTexture(ScatteringKeys.kIntergalCPDensityLUT, m_IntegrateCPDensityLUT);
            //Shader.SetGlobalTexture(Keys.kSunOnSurface, m_SunOnSurfaceLUT);
            Shader.SetGlobalVector(ScatteringKeys.kDensityScaleHeight, new Vector4(rDensityScale, mDensityScale));
            Shader.SetGlobalFloat(ScatteringKeys.kPlanetRadius, planetRadius);
            Shader.SetGlobalFloat(ScatteringKeys.kAtmosphereHeight, atmosphereHeight);
            Shader.SetGlobalFloat(ScatteringKeys.kSurfaceHeight, surfaceHeight);
            Shader.SetGlobalVector(ScatteringKeys.kIncomingLight, lightFromOuterSpace);
            Shader.SetGlobalFloat(ScatteringKeys.kSunIntensity, sunDiskScale);
            Shader.SetGlobalFloat(ScatteringKeys.kSunMieG, sunMieG);
            
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



        private void PreComputeAll()
        {
            if (computerShader == null)
            {
                Debug.LogWarningFormat("Computer shader for precompute scattering lut is empty");
                return;
            }

            SetCommonParams();
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
            if (m_AmbientLUTReadToCPU == null) m_AmbientLUTReadToCPU = new Texture2D(ambientLUTSize, 1, TextureFormat.RGB24, false, true);

            ScatteringUtils.ReadRTpixelsBackToCPU(m_AmbientLUT, m_AmbientLUTReadToCPU);

            FindAndSetDirectionalLight();

            var lightDir = -mainLight.transform.forward;
            var cosAngle01 = Vector3.Dot(Vector3.up, lightDir) * 0.5 + 0.5;

            var ambient = m_AmbientLUTReadToCPU.GetPixel((int) (cosAngle01 * m_AmbientLUTReadToCPU.width), 0);

            RenderSettings.ambientLight = ambient.gamma;
            m_AmbientColor = ambient;
        }


        private void Awake()
        {
            m_Camera = Camera.main;
            
            FindAndSetDirectionalLight();
            
            if (computerShader == null)
            {
                var computeShaders = Resources.FindObjectsOfTypeAll<ComputeShader>();
                foreach (var cs in computeShaders)
                {
                    if (cs.name == "Precomputation")
                    {
                        computerShader = cs;
                        break;
                    }
                }
                if (computerShader == null)
                {
                    computerShader = Resources.Load<ComputeShader>("Precomputation");
                }
                if (computerShader == null)
                {
                    Debug.LogWarning("Precomputation.compute shader not found. Please make sure the file exists in Resources folder or in the project.");
                }
                else
                {
                    Debug.Log("Precomputation compute shader automatically assigned: " + computerShader.name);
                }
            }
            SetSkyboxMaterial();
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
        }

        private void Update()
        {
            FindAndSetDirectionalLight();
            UpdateParams();
            SetCommonParams();
            PreComputeAll();
            //UpdateMainLight();
            UpdateAmbient();
        }



    }
}