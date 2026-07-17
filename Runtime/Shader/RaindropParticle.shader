Shader "TOD/RaindropParticle"
{
    Properties
    {
        _MainTex ("Rain Mask (R=Near, G=Far)", 2D) = "white" {}
        _RainColor ("Rain Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _RainIntensity ("Rain Intensity", Range(0.0, 1.0)) = 0.05
        _ParticleSize ("Particle Length", Float) = 1.0
        _ParticleWidth ("Particle Width", Float) = 0.01
        _NearBlurDistance ("Near Blur Distance", Float) = 5.0
        _NearBlurFalloff ("Blur Transition Range", Float) = 3.0

        [HideInInspector] _ParticleStateRT ("Particle State", 2D) = "black" {}
        [HideInInspector] _BaseFallSpeed ("Base Fall Speed", Float) = 40.0
        [HideInInspector] _WindHorizontalSpeed ("Wind Horizontal Speed", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        LOD 100
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _RainColor;
            float _RainIntensity;
            float _ParticleSize;
            float _ParticleWidth;
            sampler2D _ParticleStateRT;
            float _BaseFallSpeed;
            float _WindHorizontalSpeed;
            float _NearBlurDistance;
            float _NearBlurFalloff;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float distToCam : TEXCOORD1;
                float4 vertex : SV_POSITION;
            };

            float3 SafeNormalize(float3 value, float3 fallback)
            {
                float lengthSquared = dot(value, value);
                return lengthSquared > 1e-8
                    ? value * rsqrt(lengthSquared)
                    : fallback;
            }

            v2f vert(appdata v)
            {
                v2f o = (v2f)0;
                float4 particleState = tex2Dlod(
                    _ParticleStateRT,
                    float4(v.color.rg, 0.0, 0.0));

                // state.w == 0 marks a particle disabled by the compute density filter.
                if (particleState.w == 0.0)
                {
                    o.vertex = float4(2.0, 2.0, 1.0, 1.0);
                    return o;
                }

                float3 particleCenterWS = mul(
                    unity_ObjectToWorld,
                    float4(particleState.xyz, 1.0)).xyz;

                float speedFactor = abs(particleState.w);
                float3 velocityLS = float3(
                    _WindHorizontalSpeed,
                    -_BaseFallSpeed,
                    0.0) * speedFactor;
                float3 velocityWS = mul(
                    (float3x3)unity_ObjectToWorld,
                    velocityLS);

                float3 fallbackUpWS = SafeNormalize(
                    mul((float3x3)unity_ObjectToWorld, float3(0.0, 1.0, 0.0)),
                    float3(0.0, 1.0, 0.0));
                // Keep UV.y pointing opposite to motion so the existing mask orientation is preserved.
                float3 streakAxisWS = SafeNormalize(-velocityWS, fallbackUpWS);
                float3 viewDirectionWS = SafeNormalize(
                    _WorldSpaceCameraPos - particleCenterWS,
                    float3(0.0, 0.0, 1.0));

                float3 alternateAxisWS = abs(streakAxisWS.y) < 0.99
                    ? float3(0.0, 1.0, 0.0)
                    : float3(1.0, 0.0, 0.0);
                float3 fallbackWidthWS = SafeNormalize(
                    cross(alternateAxisWS, streakAxisWS),
                    float3(0.0, 0.0, 1.0));
                float3 widthAxisWS = SafeNormalize(
                    cross(viewDirectionWS, streakAxisWS),
                    fallbackWidthWS);

                float lengthOffset =
                    (v.uv.y - 0.5) * max(_ParticleSize, 0.0);
                float widthOffset =
                    (v.uv.x - 0.5) * max(_ParticleWidth, 0.0);
                float3 vertexWS = particleCenterWS
                    + streakAxisWS * lengthOffset
                    + widthAxisWS * widthOffset;

                o.vertex = UnityWorldToClipPos(vertexWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.distToCam = distance(_WorldSpaceCameraPos, particleCenterWS);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 mask = tex2D(_MainTex, i.uv);
                float blurFalloff = max(_NearBlurFalloff, 1e-4);
                half distanceBlend = saturate(
                    (i.distToCam - _NearBlurDistance) / blurFalloff);
                half rainMask = lerp(mask.r, mask.g, distanceBlend);
                half opacity = saturate(rainMask * _RainColor.a);

                clip(opacity - 0.01h);
                half3 premultipliedColor =
                    (half3)_RainColor.rgb * (half)_RainIntensity * opacity;
                return half4(premultipliedColor, opacity);
            }
            ENDCG
        }
    }
}
