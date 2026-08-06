Shader "Hidden/DawnTOD/PostProcessFog"
{
    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.universal": "[14.0.0,15.0.0)"
        }

        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "Dawn TOD Post Process Fog"
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _DawnFogParameters;
            float4 _DawnFogAlbedo;
            float _DawnFogAffectSky;
            float _DawnFogCloudCoverageAvailable;
            TEXTURE2D_X(_DawnCloudTexture);
            TEXTURE2D_X(_DawnCloudDistanceTexture);

            float FogDensityAtHeight(float height, float baseHeight, float maximumHeight)
            {
                float inverseHeightRange = rcp(max(maximumHeight - baseHeight, 0.01));
                float normalizedDensity =
                    (maximumHeight - height) * inverseHeightRange;
                return smoothstep(0.0, 1.0, normalizedDensity);
            }

            float FogDensityPrimitive(
                float height,
                float baseHeight,
                float maximumHeight)
            {
                float heightRange =
                    max(maximumHeight - baseHeight, 0.01);
                float clampedHeight =
                    clamp(height, baseHeight, maximumHeight);
                float normalizedDensity =
                    (maximumHeight - clampedHeight) / heightRange;
                float normalizedDensitySquared =
                    normalizedDensity * normalizedDensity;
                float transitionIntegral = heightRange * (
                    0.5 -
                    normalizedDensitySquared * normalizedDensity +
                    0.5 * normalizedDensitySquared *
                    normalizedDensitySquared);

                // Below baseHeight the density is one; above maximumHeight
                // the primitive remains constant because density is zero.
                return min(height - baseHeight, 0.0) +
                    transitionIntegral;
            }

            float FogFactorAlongRay(
                float3 cameraPosition,
                float3 rayDirection,
                float fogDistance)
            {
                float baseHeight = _DawnFogParameters.y;
                float maximumHeight = _DawnFogParameters.z;
                fogDistance = max(fogDistance, 0.0);
                float verticalDistance =
                    rayDirection.y * fogDistance;
                float integratedDensityDistance;
                if (abs(verticalDistance) < 0.001)
                {
                    integratedDensityDistance =
                        fogDistance * FogDensityAtHeight(
                            cameraPosition.y,
                            baseHeight,
                            maximumHeight);
                }
                else
                {
                    float endHeight =
                        cameraPosition.y + verticalDistance;
                    float integratedHeightDensity =
                        FogDensityPrimitive(
                            endHeight,
                            baseHeight,
                            maximumHeight) -
                        FogDensityPrimitive(
                            cameraPosition.y,
                            baseHeight,
                            maximumHeight);
                    integratedDensityDistance =
                        fogDistance * integratedHeightDensity /
                        verticalDistance;
                }

                float opticalDepth =
                    max(integratedDensityDistance, 0.0) /
                    max(_DawnFogParameters.x, 0.01);
                return 1.0 - exp(-opticalDepth);
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
#else
                bool isSky = rawDepth >= 0.9999;
                float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
#endif

                float cloudOpacity = 0.0;
                float cloudDistance = 0.0;
                if (_DawnFogCloudCoverageAvailable > 0.5)
                {
                    float cloudTransmittance = SAMPLE_TEXTURE2D_X_LOD(
                        _DawnCloudTexture,
                        sampler_LinearClamp,
                        uv,
                        0).a;
                    cloudOpacity = saturate(1.0 - cloudTransmittance);
                    cloudDistance = max(
                        SAMPLE_TEXTURE2D_X_LOD(
                            _DawnCloudDistanceTexture,
                            sampler_LinearClamp,
                            uv,
                            0).r,
                        0.0);
                }

                if (isSky && _DawnFogAffectSky < 0.5 &&
                    cloudOpacity <= 0.0025)
                {
                    return source;
                }

                // The pass is full-screen, but fog density is evaluated in world
                // space: reconstruct the pixel position from depth and integrate
                // along the world-space camera ray. Moving the camera therefore
                // changes optical depth naturally, not because the fog is local-space.
                float3 cameraPosition = _WorldSpaceCameraPos;
                float3 worldPosition = ComputeWorldSpacePosition(
                    uv,
                    deviceDepth,
                    UNITY_MATRIX_I_VP);
                float3 cameraToPixel = worldPosition - cameraPosition;
                float reconstructedDistance = length(cameraToPixel);
                float3 rayDirection = cameraToPixel /
                    max(reconstructedDistance, 0.0001);

                float maximumFogDistance = _DawnFogParameters.w;
                float sceneFogDistance = isSky
                    ? maximumFogDistance
                    : min(reconstructedDistance, maximumFogDistance);
                float sceneFogFactor =
                    isSky && _DawnFogAffectSky < 0.5
                        ? 0.0
                        : FogFactorAlongRay(
                            cameraPosition,
                            rayDirection,
                            sceneFogDistance);
                float fogFactor = sceneFogFactor;

                if (cloudOpacity > 0.0025)
                {
                    float cloudFogDistance =
                        min(cloudDistance, maximumFogDistance);
                    float cloudFogFactor = FogFactorAlongRay(
                        cameraPosition,
                        rayDirection,
                        cloudFogDistance);
                    // Dense cloud pixels use their ray-marched distance instead
                    // of the opaque surface behind them. At soft cloud edges,
                    // opacity blends the scene and cloud layers continuously.
                    fogFactor = lerp(
                        sceneFogFactor,
                        cloudFogFactor,
                        cloudOpacity);
                }

                source.rgb = lerp(source.rgb, _DawnFogAlbedo.rgb, saturate(fogFactor));
                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
