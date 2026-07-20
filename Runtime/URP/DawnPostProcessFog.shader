Shader "Hidden/DawnTOD/PostProcessFog"
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

            float FogDensityAtHeight(float height, float baseHeight, float maximumHeight)
            {
                float inverseHeightRange = rcp(max(maximumHeight - baseHeight, 0.01));
                return saturate((maximumHeight - height) * inverseHeightRange);
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
                if (isSky && _DawnFogAffectSky < 0.5)
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
                float fogDistance = isSky
                    ? maximumFogDistance
                    : min(reconstructedDistance, maximumFogDistance);
                float3 fogEndPosition =
                    cameraPosition + rayDirection * fogDistance;
                float midpointHeight =
                    (cameraPosition.y + fogEndPosition.y) * 0.5;

                float baseHeight = _DawnFogParameters.y;
                float maximumHeight = _DawnFogParameters.z;
                float densityAtCamera = FogDensityAtHeight(
                    cameraPosition.y,
                    baseHeight,
                    maximumHeight);
                float densityAtMidpoint = FogDensityAtHeight(
                    midpointHeight,
                    baseHeight,
                    maximumHeight);
                float densityAtEnd = FogDensityAtHeight(
                    fogEndPosition.y,
                    baseHeight,
                    maximumHeight);

                // Simpson integration keeps sloped camera rays stable while remaining one pass.
                float averageDensity =
                    (densityAtCamera + 4.0 * densityAtMidpoint + densityAtEnd) / 6.0;
                float opticalDepth =
                    fogDistance * averageDensity / max(_DawnFogParameters.x, 0.01);
                float fogFactor = 1.0 - exp(-opticalDepth);

                source.rgb = lerp(source.rgb, _DawnFogAlbedo.rgb, saturate(fogFactor));
                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
