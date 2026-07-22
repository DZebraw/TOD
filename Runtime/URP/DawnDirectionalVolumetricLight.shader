Shader "Hidden/DawnTOD/DirectionalVolumetricLight"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "Dawn TOD Directional Volumetric Light"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _STEREO_MULTIVIEW_ON _STEREO_INSTANCING_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _DawnDirectionalLightScatteringParameters;
            float4 _DawnDirectionalLightQualityParameters;
            float4 _DawnDirectionalLightScatteringTint;
            float4 _DawnDirectionalLightCloudShaftParameters;
            float4 _DawnDirectionalLightSunScreenPosition;
            TEXTURE2D_X(_DawnCloudTexture);
            TEXTURE2D(_DawnCloudShadowTexture);
            float4x4 _DawnCloudWorldToShadow;

            float StableScreenNoise(float2 pixelPosition)
            {
                return frac(
                    52.9829189 * frac(
                        dot(pixelPosition, float2(0.06711056, 0.00583715))));
            }

            float HenyeyGreensteinPhase(float cosineTheta, float anisotropy)
            {
                float anisotropySquared = anisotropy * anisotropy;
                float denominator = max(
                    1.0 + anisotropySquared -
                    2.0 * anisotropy * cosineTheta,
                    0.0001);
                return (1.0 - anisotropySquared) /
                    (4.0 * PI * denominator * sqrt(denominator));
            }

            half SampleDirectionalShadow(float3 positionWS)
            {
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                // Screen-space shadows only describe the first visible surface.
                // Volumetric samples need the underlying cascaded shadow map.
                half cascadeIndex = ComputeCascadeIndex(positionWS);
                float4 shadowCoord = float4(
                    mul(
                        _MainLightWorldToShadow[cascadeIndex],
                        float4(positionWS, 1.0)).xyz,
                    0.0);
                ShadowSamplingData shadowSamplingData =
                    GetMainLightShadowSamplingData();
                half realtimeShadow = SampleShadowmap(
                    TEXTURE2D_ARGS(
                        _MainLightShadowmapTexture,
                        sampler_LinearClampCompare),
                    shadowCoord,
                    shadowSamplingData,
                    GetMainLightShadowParams(),
                    false);
#else
                half realtimeShadow = MainLightRealtimeShadow(
                    TransformWorldToShadowCoord(positionWS));
#endif
                return lerp(
                    realtimeShadow,
                    half(1.0),
                    GetMainLightShadowFade(positionWS));
#else
                return half(1.0);
#endif
            }

            float SampleWorldCloudShadow(float3 positionWS)
            {
                float cloudShaftIntensity =
                    _DawnDirectionalLightCloudShaftParameters.x;
                if (cloudShaftIntensity <= 0.0)
                {
                    return 1.0;
                }

                float3 shadowCoordinate = mul(
                    _DawnCloudWorldToShadow,
                    float4(positionWS, 1.0)).xyz;
                if (any(shadowCoordinate.xy < 0.0) ||
                    any(shadowCoordinate.xy > 1.0) ||
                    shadowCoordinate.z >= 1.0)
                {
                    return 1.0;
                }

                float cloudTransmittance = SAMPLE_TEXTURE2D_LOD(
                    _DawnCloudShadowTexture,
                    sampler_LinearClamp,
                    shadowCoordinate.xy,
                    0).r;
                float receiverFade = 1.0 - smoothstep(
                    0.8,
                    1.0,
                    shadowCoordinate.z);
                float shapedTransmittance = pow(
                    max(cloudTransmittance, 0.001),
                    cloudShaftIntensity);
                return lerp(1.0, shapedTransmittance, receiverFade);
            }

            float SampleCloudSkyVisibility(float2 uv)
            {
                if (any(uv < 0.0) || any(uv > 1.0))
                {
                    return 0.0;
                }

                float rawDepth = SampleSceneDepth(uv);
#if UNITY_REVERSED_Z
                float skyMask = rawDepth <= 0.0001 ? 1.0 : 0.0;
#else
                float skyMask = rawDepth >= 0.9999 ? 1.0 : 0.0;
#endif
                float cloudTransmittance = SAMPLE_TEXTURE2D_X_LOD(
                    _DawnCloudTexture,
                    sampler_LinearClamp,
                    uv,
                    0).a;
                return skyMask * cloudTransmittance;
            }

            float EvaluateCloudShafts(float2 uv)
            {
                if (_DawnDirectionalLightCloudShaftParameters.x <= 0.0 ||
                    _DawnDirectionalLightSunScreenPosition.z <= 0.0)
                {
                    return 0.0;
                }

                int sampleCount = max(
                    1,
                    (int)_DawnDirectionalLightCloudShaftParameters.y);
                float2 stepToSun =
                    (_DawnDirectionalLightSunScreenPosition.xy - uv) *
                    rcp(sampleCount);
                float shaftLength = saturate(
                    _DawnDirectionalLightCloudShaftParameters.z);
                float2 sampleUv = uv;
                float illuminationDecay = 1.0;
                float weightedVisibility = 0.0;
                float totalWeight = 0.0;

                UNITY_LOOP
                for (int sampleIndex = 0; sampleIndex < 64; sampleIndex++)
                {
                    if (sampleIndex >= sampleCount)
                    {
                        break;
                    }

                    sampleUv += stepToSun;
                    float sampleProgress =
                        (sampleIndex + 1.0) / sampleCount;
                    // Always sample the complete pixel-to-sun segment. Length
                    // now selects where a continuous tail begins instead of
                    // truncating the segment, so terrain pixels never switch
                    // abruptly between reaching and not reaching the sky.
                    float lengthFadeProgress = saturate(
                        (sampleProgress - shaftLength) /
                        max(1.0 - shaftLength, 0.0001));
                    float lengthFade =
                        lengthFadeProgress * lengthFadeProgress *
                        (3.0 - 2.0 * lengthFadeProgress);
                    float lengthWeight = lerp(1.0, 0.05, lengthFade);
                    // Keep a small continuous tail even at aggressive Decay
                    // values; otherwise far samples underflow and recreate the
                    // same binary influence boundary on opaque terrain.
                    float decayWeight = lerp(
                        0.05,
                        1.0,
                        illuminationDecay);
                    float sampleWeight = lengthWeight * decayWeight;
                    weightedVisibility +=
                        SampleCloudSkyVisibility(sampleUv) *
                        sampleWeight;
                    totalWeight += sampleWeight;
                    illuminationDecay *=
                        _DawnDirectionalLightCloudShaftParameters.w;
                }

                float radialVisibility =
                    weightedVisibility / max(totalWeight, 0.0001);
                float localVisibility = SampleCloudSkyVisibility(uv);

                // High-pass the radial visibility. Clear sky remains unchanged,
                // while cloud/geometry pixels crossed by a bright cloud gap form
                // the characteristic striped Tyndall shafts.
                return max(0.0, radialVisibility - localVisibility);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv,
                    0);

                float rawDepth = SampleSceneDepth(uv);
#if UNITY_REVERSED_Z
                bool isSky = rawDepth <= 0.0001;
                float deviceDepth = rawDepth;
                float nearDeviceDepth = 1.0;
#else
                bool isSky = rawDepth >= 0.9999;
                float deviceDepth = lerp(
                    UNITY_NEAR_CLIP_VALUE,
                    1.0,
                    rawDepth);
                float nearDeviceDepth = UNITY_NEAR_CLIP_VALUE;
#endif
                if (isSky && _DawnDirectionalLightQualityParameters.w < 0.5)
                {
                    return source;
                }

                // UNITY_MATRIX_I_VP is the inverse GPU view-projection matrix.
                // Reconstructing the near plane per pixel is equivalent to
                // interpolating the four CPU-computed frustum corners, while also
                // remaining correct for stereo and orthographic cameras.
                float3 rayStartPosition = ComputeWorldSpacePosition(
                    uv,
                    nearDeviceDepth,
                    UNITY_MATRIX_I_VP);
                float3 worldPosition = ComputeWorldSpacePosition(
                    uv,
                    deviceDepth,
                    UNITY_MATRIX_I_VP);
                float3 rayToPixel = worldPosition - rayStartPosition;
                float reconstructedDistance = length(rayToPixel);
                if (reconstructedDistance <= 0.0001)
                {
                    return source;
                }

                float3 rayDirection = rayToPixel / reconstructedDistance;
                float rayLength = isSky
                    ? _DawnDirectionalLightQualityParameters.x
                    : min(
                        reconstructedDistance,
                        _DawnDirectionalLightQualityParameters.x);
                int stepCount = max(
                    1,
                    (int)_DawnDirectionalLightQualityParameters.y);
                float stepLength = rayLength / stepCount;
                float extinction = rcp(max(
                    _DawnDirectionalLightScatteringParameters.y,
                    0.01));
                float stepTransmittance = exp(-stepLength * extinction);
                float transmittance = 1.0;
                float accumulatedScattering = 0.0;

                float jitter = lerp(
                    0.5,
                    StableScreenNoise(input.positionCS.xy),
                    _DawnDirectionalLightQualityParameters.z);

                UNITY_LOOP
                for (int stepIndex = 0; stepIndex < 128; stepIndex++)
                {
                    if (stepIndex >= stepCount)
                    {
                        break;
                    }

                    float sampleDistance =
                        (stepIndex + jitter) * stepLength;
                    float3 samplePosition =
                        rayStartPosition + rayDirection * sampleDistance;
                    float shadow = SampleDirectionalShadow(samplePosition);
                    float opaqueVisibility = lerp(
                        1.0,
                        shadow,
                        _DawnDirectionalLightScatteringParameters.w);
                    float cloudVisibility =
                        SampleWorldCloudShadow(samplePosition);
                    float visibility =
                        opaqueVisibility * cloudVisibility;
                    accumulatedScattering +=
                        transmittance * visibility *
                        (1.0 - stepTransmittance);
                    transmittance *= stepTransmittance;
                }

                Light mainLight = GetMainLight();
                float phase = HenyeyGreensteinPhase(
                    dot(rayDirection, mainLight.direction),
                    _DawnDirectionalLightScatteringParameters.z);
                float3 scattering =
                    accumulatedScattering * phase *
                    _DawnDirectionalLightScatteringParameters.x *
                    mainLight.distanceAttenuation * mainLight.color *
                    _DawnDirectionalLightScatteringTint.rgb;
                // The screen-space radial mask is valid only for sky pixels.
                // Opaque terrain receives the world-space cloud shadow sampled
                // during ray marching, avoiding depth-silhouette boundaries.
                float cloudShafts = isSky
                    ? EvaluateCloudShafts(uv)
                    : 0.0;
                float mediumAmount = 1.0 - transmittance;
                float3 cloudShaftScattering =
                    cloudShafts * mediumAmount *
                    _DawnDirectionalLightCloudShaftParameters.x *
                    _DawnDirectionalLightScatteringParameters.x *
                    mainLight.distanceAttenuation * mainLight.color *
                    _DawnDirectionalLightScatteringTint.rgb;
                source.rgb += scattering + cloudShaftScattering;
                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
