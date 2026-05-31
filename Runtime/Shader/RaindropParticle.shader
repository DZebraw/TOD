Shader "TOD/RaindropParticle"
{
    Properties
    {
        _MainTex ("雨滴纹理 (R=近清晰 G=远模糊)", 2D) = "white" {}
        _RainColor("雨颜色",Color) = (1.0,1.0,1.0,1.0)
        _ParticleSize ("粒子长度", Float) = 1
        //_RainDensity ("雨滴密度", Range(0,1)) = 1.0 
        _NearBlurDistance ("近模糊距离", Float) = 5.0
        _NearBlurFalloff ("模糊过渡范围", Float) = 3.0
        _WindZRotation ("风力Z轴旋转角度", Range(-45, 45)) = 0.0  
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        LOD 100
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _RainColor;
            float _ParticleSize;
            sampler2D _ParticleStateRT;
            float4 _ParticleStateRT_TexelSize;
            float _RainDensity;
            float _NearBlurDistance;
            float _NearBlurFalloff;
            float _WindZRotation;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; 
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float2 particleUV : TEXCOORD3;
                float distToCam : TEXCOORD4;
            };
            
            v2f vert (appdata v)
            {
                v2f o;
                float2 particleUV = v.color.rg;
                
                // 雨滴密度过滤
                float particleRandom = frac(sin(dot(particleUV * 9999.0, float2(12.9898, 78.233))) * 43758.5453);
                if (particleRandom > _RainDensity)
                {
                    o.vertex = float4(0,0,-1000,1);
                    return o;
                }
                
                // 采样粒子状态
                float4 particleState = tex2Dlod(_ParticleStateRT, float4(particleUV, 0, 0));
                float3 worldPos = float3(
                    particleState.x,
                    particleState.y,
                    particleState.z
                );
                
                float windRad = radians(_WindZRotation);
                
                // 原有：雨滴面向摄像机计算
                float3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                float3 upDir = float3(0, 1, 0);
                float3 rightDir = normalize(cross(viewDir, upDir));
                
                // 旋转整个雨滴的顶点偏移
                float3x3 rotMatZ = float3x3(
                    cos(windRad), -sin(windRad), 0,
                    sin(windRad), cos(windRad),  0,
                    0,            0,             1
                );

                // 计算原始顶点偏移（基于UV的雨滴Quad顶点）
                float3 originalOffset = float3(
                    (v.uv.x - 0.5) * 0.01 * rightDir.x,
                    (v.uv.y - 0.5) * _ParticleSize,
                    (v.uv.x - 0.5) * 0.01 * rightDir.z
                );
                
                float3 rotatedOffset = mul(rotMatZ, originalOffset);

                worldPos += rotatedOffset;
                
                o.distToCam = distance(_WorldSpaceCameraPos, worldPos);
                
                o.vertex = UnityObjectToClipPos(float4(worldPos, 1));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.particleUV = particleUV;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 原有：密度二次校验
                float particleRandom = frac(sin(dot(i.particleUV * 9999.0, float2(12.9898, 78.233))) * 43758.5453);
                if (particleRandom > _RainDensity)
                {
                    discard;
                }
                
                fixed4 col = tex2D(_MainTex, i.uv);
                float blendFactor = saturate(
                    (i.distToCam - _NearBlurDistance) / _NearBlurFalloff
                );
                blendFactor = clamp(blendFactor, 0.0, 1.0);
                float finalAlpha = lerp(col.r, col.g, blendFactor);
                
                clip(finalAlpha - 0.01);
                fixed3 finalColor = 0.05 * _RainColor;
                
                return fixed4(finalColor, finalAlpha);
            }
            ENDCG
        }
    }
}