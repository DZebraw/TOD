Shader "Hidden/NubisStorm/WorldMap"
{
    Properties
    {
        _CloudWeather("NDF", 2D) = "black" {}
        _CloudBottomType("Bottom", 2D) = "black" {}
        _CloudWeatherTo("Destination NDF", 2D) = "black" {}
        _CloudBottomTypeTo("Destination bottom", 2D) = "black" {}
        _CloudTopProfile("Top profile", 2D) = "black" {}
        _CloudBottomProfile("Bottom profile", 2D) = "black" {}
        _StormNoise("Noise", 3D) = "white" {}
        _AuthoredFields("Authored", 2D) = "black" {}
        _AuthoredBottomMask("Authored mask", 2D) = "black" {}
        _StormFields("Storm", 2D) = "black" {}
        _StormBottomMask("Storm mask", 2D) = "black" {}
        _CirrusWeather("Cirrus NDF", 2D) = "black" {}
        _CirrusWeatherTo("Destination cirrus NDF", 2D) = "black" {}
        _CirrusShapes("Cirrus shapes", 2D) = "black" {}
        _CirrusAuthored("Cirrus influence", 2D) = "black" {}
    }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.tencent.dawn.tod/Runtime/URP/Nubis/Shaders/NubisCloudDensity.hlsl"
            #include "Packages/com.tencent.dawn.tod/Runtime/URP/Nubis/Shaders/NubisCloudCirrus.hlsl"
            float4 _MapRegion; // Absolute model XZ centre and extent in metres.
            float _MapMode, _MapTint, _MapLayer;
            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.vertex.xyz); o.uv=v.uv; return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                float2 p=_MapRegion.xy+(i.uv-0.5)*_MapRegion.zw;
                if(_MapLayer>.5)
                {
                    float3 f=ReadCirrusNdf(p);
                    float3 c=float3(f.x,f.x*f.y,f.x*(1-f.y));
                    if(_MapMode<.5 && _MapTint>.5) c=lerp(c,float3(.05,.75,.9),f.z*.4);
                    if(_MapMode>.5) c=f.x.xxx;
                    if(_MapMode>1.5) c=f.y.xxx;
                    if(_MapMode>2.5) c=CirrusDensity(p,_MapRegion.z/1024).xxx;
                    if(_MapMode>3.5) c=f.z.xxx;
                    return float4(c,1);
                }
                // Same field sampling, mask order, wind and global controls as the volume renderer.
                CloudNdf f=ReadCloudNdf(float3(p.x,0,p.y));
                float3 color=float3(f.coverage,f.topType*f.coverage,f.bottomType*f.coverage);
                if(_MapMode<0.5 && _MapTint>0.5)
                {
                    color=lerp(color,float3(0.05,0.75,0.9),f.authoredMask*0.4);
                    color=lerp(color,float3(1,0.3,0.04),f.stormMask*0.65);
                }
                if(_MapMode>0.5) color=f.coverage.xxx;
                if(_MapMode>1.5) color=f.topType.xxx;
                if(_MapMode>2.5) color=f.bottomType.xxx;
                if(_MapMode>3.5) color=((f.minimum-_CloudField.w)/_StormSize.y).xxx;
                if(_MapMode>4.5) color=((f.maximum-_CloudField.w)/_StormSize.y).xxx;
                if(_MapMode>5.5) color=f.authoredMask.xxx;
                if(_MapMode>6.5) color=f.stormMask.xxx;
                return float4(saturate(color),1);
            }
            ENDHLSL
        }
    }
}
