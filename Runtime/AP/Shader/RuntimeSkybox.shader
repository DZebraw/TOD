Shader "AtmosphericScattering/RuntimeSkybox"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }
        ZWrite Off Cull Off
        
        Pass
        {
            HLSLPROGRAM
            
            #pragma target 5.0
            
            #pragma vertex vert
            #pragma fragment frag
            
            #define _RENDERSUN 1
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ShaderLibrary/InScattering.hlsl"
            
            #define SAMPLECOUNT_KSYBOX 64
            #define ATMOSPHERE_GROUND_EPSILON 1.0

            uniform float3 _AtmosphereGroundColor;
            
            struct appdata
            {
                float3 vertex: POSITION;
            };
            
            struct v2f
            {
                float4 positionCS: SV_POSITION;
                float3 positionOS: TEXCOORD0;
            };
            
            v2f vert(appdata v)
            {
                v2f o;
                o.positionCS = TransformObjectToHClip(v.vertex);
                o.positionOS = v.vertex;
                return o;
            }
            
            half4 frag(v2f i): SV_Target
            {
                float3 planetCenter = float3(0, -_PlanetRadius, 0);
                float3 rayStart = ClampRayOriginAboveSphere(
                    _WorldSpaceCameraPos.xyz,
                    planetCenter,
                    _PlanetRadius,
                    ATMOSPHERE_GROUND_EPSILON);
                float3 rayDir = normalize(TransformObjectToWorld(i.positionOS));
                float3 lightDir = _MainLightPosition.xyz;
                
                float2 atmosphereIntersections = RaySphereIntersection(
                    rayStart,
                    rayDir,
                    planetCenter,
                    _PlanetRadius + _AtmosphereHeight);
                float rayLength = RaySphereNearestForwardIntersection(atmosphereIntersections);

                if (rayLength < 0.0)
                    return float4(0.0, 0.0, 0.0, 1.0);

                float2 groundIntersections = RaySphereIntersection(rayStart, rayDir, planetCenter, _PlanetRadius);
                float groundDistance = RaySphereNearestForwardIntersection(groundIntersections);
                bool hitsGround = groundDistance >= 0.0 && groundDistance <= rayLength;

                if (hitsGround)
                    rayLength = groundDistance;

                if (rayLength <= 0.0)
                    return float4(hitsGround ? _AtmosphereGroundColor : 0.0, 1.0);
                
                float3 extinction;
                
                float3 inscattering = IntegrateInscattering(rayStart, rayDir, rayLength, planetCenter, 1, lightDir, SAMPLECOUNT_KSYBOX, extinction);
                float3 background = hitsGround ? _AtmosphereGroundColor * extinction : 0.0;
                return float4(background + inscattering, 1.0);
            }
            ENDHLSL
            
        }
    }
}
