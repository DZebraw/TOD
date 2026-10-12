Shader "NubisStorm/Atmosphere"
{
    Properties
    {
        _Zenith("Zenith", Color) = (0.075, 0.12, 0.18, 1)
        _Horizon("Horizon", Color) = (0.40, 0.36, 0.36, 1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _Zenith, _Horizon;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ray : TEXCOORD0; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.ray = i.positionOS.xyz;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 ray = normalize(i.ray);
                return float4(lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(ray.y), 0.55)), 1);
            }
            ENDHLSL
        }
    }
}
