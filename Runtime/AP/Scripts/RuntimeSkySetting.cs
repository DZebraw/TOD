using System;
#if DAWNTOD_URP_AVAILABLE
using System.Linq;
using Unity.Collections;
using UnityEngine;

using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace DawnTOD
{
    [ExecuteInEditMode]
    public class RuntimeSkySetting : MonoBehaviour

    {
        private const float SpaceEmissionTrackMaximum = 1000f;
        private const int AmbientSampleCount = 128;
        private const float AmbientSolidAngleWeight = 4f / AmbientSampleCount;
        private const float FibonacciGoldenAngle = 2.39996323f;
        private const int FrustumCornerCount = 4;
        // Must match the direction sequence and golden angle in
        // Precomputation.compute.
        private static readonly int AmbientSunDirectionId =
            Shader.PropertyToID("_AmbientSunDirection");
        private static readonly Vector3[] AmbientSampleDirections =
            CreateAmbientSampleDirections();

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

        private static readonly Vector2Int IntegrateCpDensityLutSize =
            new Vector2Int(512, 512);
        private static readonly Vector2Int SunOnSurfaceLutSize =
            new Vector2Int(512, 512);
        private static readonly Vector2Int InScatteringLutSize =
            new Vector2Int(1024, 1024);

        [Header("Debug/Output")] [NonSerialized]
        private bool m_ShowFrustumCorners = false;

        [NonSerialized] [ColorUsage(false, true)]
        private Color m_MainLightColor;

        [NonSerialized]
        private SphericalHarmonicsL2 m_AmbientProbe;

        [NonSerialized]
        private float m_AmbientNormalizationScale = 1f;

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
        private bool m_AmbientReadbackPending;
        private bool m_AmbientReadbackWarningLogged;
        private bool m_AmbientReadbackFailureWarningLogged;
        private bool m_MissingMainLightWarningLogged;
        private int m_AmbientReadbackGeneration;

        private Camera m_Camera;
        private VolumeStack m_AtmosphereVolumeStack;
        [NonSerialized]
        private Vector3[] m_FrustumCorners =
            new Vector3[FrustumCornerCount];
        [NonSerialized]
        private Vector4[] m_FrustumCornersVec4 =
            new Vector4[FrustumCornerCount];
        private bool m_PipelineOutputActive;
        private bool m_PipelineOutputSuspended;

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
                EnsureFrustumCornerBuffers();
                m_Camera.CalculateFrustumCorners(m_Camera.rect, m_Camera.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, m_FrustumCorners);
                for (int i = 0; i < FrustumCornerCount; i++)
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
            if (CanRenderPipelineOutput())
            {
                ApplySpaceParams(GetActiveAtmosphereVolume());
            }
        }

        internal void SetPipelineOutputActive(bool active)
        {
            bool shouldActivate =
                active &&
                isActiveAndEnabled &&
                IsUniversalPipelineActive();
            if (m_PipelineOutputActive == shouldActivate)
            {
                if (!shouldActivate)
                {
                    SuspendPipelineOutput();
                }
                return;
            }

            m_PipelineOutputActive = shouldActivate;
            if (!shouldActivate)
            {
                SuspendPipelineOutput();
                return;
            }

            m_PipelineOutputSuspended = false;
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
            }
            FindAndSetDirectionalLight();
            EnsureComputerShader();
            SetSkyboxMaterial();
            ApplySpaceParams(GetActiveAtmosphereVolume());
        }

        private DawnAtmosphereVolume GetActiveAtmosphereVolume()
        {
#if DAWNTOD_URP_AVAILABLE
            Camera mainCamera = Camera.main;
            if (m_Camera != mainCamera)
            {
                m_Camera = mainCamera;
            }

            Transform volumeTrigger = m_Camera != null
                ? m_Camera.transform
                : null;
            LayerMask volumeLayerMask = 1;
            if (m_Camera != null &&
                m_Camera.TryGetComponent(
                    out UniversalAdditionalCameraData cameraData))
            {
                volumeLayerMask = cameraData.volumeLayerMask;
                if (cameraData.volumeTrigger != null)
                {
                    volumeTrigger = cameraData.volumeTrigger;
                }
            }

            VolumeManager volumeManager = VolumeManager.instance;
            if (m_AtmosphereVolumeStack == null)
            {
                m_AtmosphereVolumeStack = volumeManager.CreateStack();
            }

            volumeManager.Update(
                m_AtmosphereVolumeStack,
                volumeTrigger,
                volumeLayerMask);
            return m_AtmosphereVolumeStack.GetComponent<DawnAtmosphereVolume>();
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
            ComputeAmbient();
        }

        private void ComputeIntegrateCPdensity()
        {
            ScatteringUtils.CheckOrCreateLUT(ref m_IntegrateCPDensityLUT, IntegrateCpDensityLutSize, RenderTextureFormat.RGFloat);

            int index = computerShader.FindKernel("CSIntergalCPDensity");

            // Set Params
            computerShader.SetTexture(index, ScatteringKeys.kRWintergalCPDensityLUT, m_IntegrateCPDensityLUT);

            ScatteringUtils.Dispatch(computerShader, index, IntegrateCpDensityLutSize);
        }

        //TODO need HDR format?
        private void ComputeSunOnSurface()
        {
            ScatteringUtils.CheckOrCreateLUT(ref m_SunOnSurfaceLUT, SunOnSurfaceLutSize, RenderTextureFormat.DefaultHDR);

            int index = computerShader.FindKernel("CSsunOnSurface");

            // Set Params
            computerShader.SetTexture(index, ScatteringKeys.kRWsunOnSurfaceLUT, m_SunOnSurfaceLUT);
            computerShader.SetTexture(index, ScatteringKeys.kIntergalCPDensityLUT, m_IntegrateCPDensityLUT);

            ScatteringUtils.Dispatch(computerShader, index, SunOnSurfaceLutSize);
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
            ScatteringUtils.CheckOrCreateLUT(ref m_InScatteringLUT, InScatteringLutSize, RenderTextureFormat.DefaultHDR);

            int index = computerShader.FindKernel("CSInScattering");

            //Set Params
            computerShader.SetTexture(index, ScatteringKeys.kRWinScatteringLUT, m_InScatteringLUT);
            computerShader.SetTexture(index, ScatteringKeys.kIntergalCPDensityLUT, m_IntegrateCPDensityLUT);

            ScatteringUtils.Dispatch(computerShader, index, InScatteringLutSize);
        }

        private void ComputeAmbient()
        {
            if (mainLight == null)
                return;

            Vector3 lightDir = -mainLight.transform.forward;
            if (!IsFiniteVector(lightDir) ||
                lightDir.sqrMagnitude <= Mathf.Epsilon)
                return;

            lightDir.Normalize();
            var size = new Vector2Int(AmbientSampleCount, 1);
            ScatteringUtils.CheckOrCreateLUT(ref m_AmbientLUT, size, RenderTextureFormat.DefaultHDR);

            int index = computerShader.FindKernel("CSAmbient");

            //Set Params
            computerShader.SetVector(
                AmbientSunDirectionId,
                new Vector4(lightDir.x, lightDir.y, lightDir.z, 0f));
            computerShader.SetTexture(
                index,
                ScatteringKeys.kIntergalCPDensityLUT,
                m_IntegrateCPDensityLUT);
            computerShader.SetTexture(index, ScatteringKeys.kRWambientLUT, m_AmbientLUT);

            ScatteringUtils.Dispatch(computerShader, index, size);
        }

        private void UpdateAmbient(DawnAtmosphereVolume volume)
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

            m_AmbientReadbackPending = true;
            int readbackGeneration = m_AmbientReadbackGeneration;
            float ambientLightIntensity = SanitizeAmbientLightIntensity(
                Resolve(
                    volume,
                    volume?.ambientLightIntensity,
                    1f));

            if (!ScatteringUtils.RequestRTpixelsBackToCPU(
                    m_AmbientLUT, TextureFormat.RGBAFloat,
                    request => OnAmbientReadbackCompleted(
                        request,
                        ambientLightIntensity,
                        readbackGeneration)))
            {
                m_AmbientReadbackPending = false;
            }
        }

        private void OnAmbientReadbackCompleted(
            AsyncGPUReadbackRequest request,
            float ambientLightIntensity,
            int readbackGeneration)
        {
            if (this == null)
                return;

            if (readbackGeneration != m_AmbientReadbackGeneration)
                return;

            m_AmbientReadbackPending = false;
            if (!isActiveAndEnabled ||
                !CanRenderPipelineOutput())
                return;

            if (request.hasError)
            {
                if (!m_AmbientReadbackFailureWarningLogged)
                {
                    Debug.LogWarning(
                        "Ambient SH GPU readback failed; the last valid " +
                        "ambient probe will be kept.",
                        this);
                    m_AmbientReadbackFailureWarningLogged = true;
                }

                return;
            }

            var pixels = request.GetData<Color>();
            if (pixels.Length < AmbientSampleCount)
                return;

            m_AmbientProbe = BuildAmbientProbe(
                pixels,
                ambientLightIntensity);
            ApplyAmbientProbe(m_AmbientProbe);
        }

        private static Vector3[] CreateAmbientSampleDirections()
        {
            var directions = new Vector3[AmbientSampleCount];
            for (int i = 0; i < directions.Length; i++)
            {
                float sample01 = (i + 0.5f) / AmbientSampleCount;
                float y = 1f - 2f * sample01;
                float phi = (i + 0.5f) * FibonacciGoldenAngle;
                float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                directions[i] = new Vector3(
                    Mathf.Cos(phi) * radius,
                    y,
                    Mathf.Sin(phi) * radius);
            }

            return directions;
        }

        private static float CalculateAmbientNormalizationScale()
        {
            var sampledUniformProbe = new SphericalHarmonicsL2();
            for (int i = 0; i < AmbientSampleDirections.Length; i++)
            {
                sampledUniformProbe.AddDirectionalLight(
                    AmbientSampleDirections[i],
                    Color.white,
                    AmbientSolidAngleWeight);
            }

            var referenceUniformProbe = new SphericalHarmonicsL2();
            referenceUniformProbe.AddAmbientLight(Color.white);
            float sampledL0 = sampledUniformProbe[0, 0];
            float referenceL0 = referenceUniformProbe[0, 0];
            if (!IsFinite(sampledL0) ||
                !IsFinite(referenceL0) ||
                Mathf.Abs(sampledL0) <= Mathf.Epsilon)
            {
                return 1f;
            }

            return referenceL0 / sampledL0;
        }

        private SphericalHarmonicsL2 BuildAmbientProbe(
            NativeArray<Color> radianceSamples,
            float ambientLightIntensity)
        {
            if (radianceSamples.Length < AmbientSampleCount)
            {
                throw new ArgumentException(
                    "Ambient radiance sample count is smaller than expected.",
                    nameof(radianceSamples));
            }

            ambientLightIntensity =
                SanitizeAmbientLightIntensity(ambientLightIntensity);
            var probe = new SphericalHarmonicsL2();
            for (int i = 0; i < AmbientSampleCount; i++)
            {
                probe.AddDirectionalLight(
                    AmbientSampleDirections[i],
                    SanitizeAmbientColor(radianceSamples[i]),
                    AmbientSolidAngleWeight);
            }

            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    probe[rgb, coefficient] = SanitizeAmbientCoefficient(
                        probe[rgb, coefficient] *
                        m_AmbientNormalizationScale *
                        ambientLightIntensity);
                }
            }

            return probe;
        }

        private static void ApplyAmbientProbe(SphericalHarmonicsL2 probe)
        {
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = probe;
        }

        private static Color SanitizeAmbientColor(Color color)
        {
            return new Color(
                SanitizeAmbientChannel(color.r),
                SanitizeAmbientChannel(color.g),
                SanitizeAmbientChannel(color.b),
                1f);
        }

        private static float SanitizeAmbientChannel(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Max(0f, value);
        }

        private static float SanitizeAmbientCoefficient(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : value;
        }

        private static float SanitizeAmbientLightIntensity(float value)
        {
            return IsFinite(value) ? Mathf.Max(0f, value) : 0f;
        }

        private static bool IsFiniteVector(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }


        private bool EnsureComputerShader()
        {
            if (computerShader != null)
                return true;

            computerShader = Resources.Load<ComputeShader>(PrecomputationResourcePath);
            return computerShader != null;
        }

        private void EnsureFrustumCornerBuffers()
        {
            if (m_FrustumCorners == null ||
                m_FrustumCorners.Length != FrustumCornerCount)
            {
                m_FrustumCorners = new Vector3[FrustumCornerCount];
            }

            if (m_FrustumCornersVec4 == null ||
                m_FrustumCornersVec4.Length != FrustumCornerCount)
            {
                m_FrustumCornersVec4 =
                    new Vector4[FrustumCornerCount];
            }
        }

        private void Awake()
        {
            EnsureFrustumCornerBuffers();
            m_AmbientNormalizationScale =
                CalculateAmbientNormalizationScale();
            m_Camera = Camera.main;
            m_PipelineOutputActive = false;
            SuspendPipelineOutput();
        }

        private void OnEnable()
        {
            m_PipelineOutputActive = false;
            SuspendPipelineOutput();
        }

        private void OnValidate()
        {
            spaceEmissionMultiplier = SanitizeSpaceEmission(spaceEmissionMultiplier);
            if (!CanRenderPipelineOutput())
            {
                SuspendPipelineOutput();
                return;
            }

            m_PipelineOutputSuspended = false;
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
                    LogMissingMainLight(
                        "No directional light was found in DawnTODSystem; " +
                        "the last valid ambient probe will be kept.");
                    return;
                }
            }
            else
            {
                mainLight = FindObjectsOfType<Light>().FirstOrDefault(light => light.type == LightType.Directional);
                if (mainLight == null)
                {
                    LogMissingMainLight(
                        "No directional light was found in the scene; the " +
                        "last valid ambient probe will be kept.");
                    return;
                }
            }

            m_MissingMainLightWarningLogged = false;
            lightFromOuterSpace = mainLight.color * mainLight.intensity;
        }

        private void LogMissingMainLight(string message)
        {
            if (m_MissingMainLightWarningLogged)
                return;

            Debug.LogWarning(message, this);
            m_MissingMainLightWarningLogged = true;
        }

        private void OnDisable()
        {
            m_PipelineOutputActive = false;
            SuspendPipelineOutput();
            m_AmbientReadbackGeneration++;
            m_AmbientReadbackPending = false;
            ReleaseAtmosphereVolumeStack();
            ReleaseRenderTexture(ref m_IntegrateCPDensityLUT);
            ReleaseRenderTexture(ref m_SunOnSurfaceLUT);
            ReleaseRenderTexture(ref m_AmbientLUT);
            ReleaseRenderTexture(ref m_InScatteringLUT);
        }

        private void Update()
        {
            if (!IsUniversalPipelineActive())
            {
                m_PipelineOutputActive = false;
                SuspendPipelineOutput();
                return;
            }

            if (!m_PipelineOutputActive)
            {
                SuspendPipelineOutput();
                return;
            }

            m_PipelineOutputSuspended = false;
            DawnAtmosphereVolume atmosphereVolume = GetActiveAtmosphereVolume();
            FindAndSetDirectionalLight();
            UpdateParams(atmosphereVolume);
            SetCommonParams(atmosphereVolume);
            PreComputeAll(atmosphereVolume);
            //UpdateMainLight();
            UpdateAmbient(atmosphereVolume);
        }

        private bool CanRenderPipelineOutput()
        {
            return m_PipelineOutputActive &&
                   IsUniversalPipelineActive();
        }

        private static bool IsUniversalPipelineActive()
        {
            return WeatherPipelineCapabilities.Current.PipelineKind ==
                   WeatherRenderPipelineKind.Universal;
        }

        private void SuspendPipelineOutput()
        {
            if (m_PipelineOutputSuspended)
            {
                return;
            }

            m_PipelineOutputSuspended = true;
            Shader.SetGlobalFloat(
                ScatteringKeys.kSpaceEmissionMultiplier,
                0f);
        }

        private static void ReleaseRenderTexture(
            ref RenderTexture renderTexture)
        {
            if (renderTexture == null)
            {
                return;
            }

            renderTexture.Release();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(renderTexture);
                renderTexture = null;
                return;
            }
#endif
            Destroy(renderTexture);
            renderTexture = null;
        }

        private void ReleaseAtmosphereVolumeStack()
        {
            if (m_AtmosphereVolumeStack == null)
            {
                return;
            }

            VolumeManager.instance.DestroyStack(m_AtmosphereVolumeStack);
            m_AtmosphereVolumeStack = null;
        }



    }
}
#endif
