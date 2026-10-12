Shader "Hidden/DawnTOD/PostProcessFog"
{
    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.universal": "[14.0.0,15.0.0)"
        }
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Name "Dawn TOD Exponential Height Fog"
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
            #include "DawnExponentialHeightFog.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
                float rawDepth = SampleSceneDepth(uv);
#if UNITY_REVERSED_Z
                bool isSky = rawDepth <= 0.0000001;
                float deviceDepth = rawDepth;
                const float nearDepth = 1.0;
                const float farDepth = 0.0000001;
#else
                bool isSky = rawDepth >= 0.9999999;
                float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                const float nearDepth = UNITY_NEAR_CLIP_VALUE;
                const float farDepth = 0.9999999;
#endif
                float3 origin = _WorldSpaceCameraPos;
                if (unity_OrthoParams.w > 0.5)
                    origin = ComputeWorldSpacePosition(uv, nearDepth, UNITY_MATRIX_I_VP);
                float3 worldPosition = ComputeWorldSpacePosition(
                    uv, isSky ? farDepth : deviceDepth, UNITY_MATRIX_I_VP);
                float3 cameraToPixel = worldPosition - origin;
                float rayDistance = length(cameraToPixel);
                float3 direction = cameraToPixel / max(rayDistance, 0.0001);
                float4 fog = DawnEvaluateHeightFog(origin, direction, isSky ? 1.0e8 : rayDistance);
                source.rgb = source.rgb * fog.a + fog.rgb;
                return source;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
