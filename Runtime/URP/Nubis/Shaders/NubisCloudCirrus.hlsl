#ifndef NUBIS_CIRRUS_INCLUDED
#define NUBIS_CIRRUS_INCLUDED
// Requires NubisCloudDensity / Geometry for the same spherical material coordinates.
TEXTURE2D(_CirrusWeather); SAMPLER(sampler_CirrusWeather);
TEXTURE2D(_CirrusWeatherTo); SAMPLER(sampler_CirrusWeatherTo);
float4 _CirrusWeatherBlend;
TEXTURE2D(_CirrusShapes); SAMPLER(sampler_CirrusShapes);
TEXTURE2D(_CirrusAuthored); SAMPLER(sampler_CirrusAuthored);
float4 _CirrusLayer, _CirrusControl, _CirrusScale, _CirrusWind, _CirrusRegion;

float3 ReadCirrusNdf(float2 model)
{
    float2 fieldP=model-_CloudField.xy-_CirrusWind.zw*_StormMotion.x;
    float2 uv=fieldP/max(1000,_CirrusWeatherBlend.y)+0.5;
    float2 field=SAMPLE_TEXTURE2D_LOD(_CirrusWeather,sampler_CirrusWeather,uv,0).rg;
    if(_CirrusWeatherBlend.x>0)
        field=lerp(field,SAMPLE_TEXTURE2D_LOD(_CirrusWeatherTo,sampler_CirrusWeatherTo,
            fieldP/max(1000,_CirrusWeatherBlend.z)+0.5,0).rg,_CirrusWeatherBlend.x);
    float2 authoredUV=(model-_CirrusRegion.xy)/_CirrusRegion.zw+0.5;
    float3 authored=SAMPLE_TEXTURE2D_LOD(_CirrusAuthored,sampler_CirrusAuthored,saturate(authoredUV),0).rgb;
    float mask=authored.b*InsideNdf(authoredUV)*saturate(_CirrusControl.z);
    field=lerp(field,authored.rg,mask);
    return float3(saturate(field.x*_CirrusControl.x),saturate(field.y+_CirrusControl.y),mask);
}
float CirrusDensity(float2 model,float footprint)
{
    float3 ndf=ReadCirrusNdf(model);
    float2 uv=(model-_CloudField.xy-_CirrusWind.xy*_StormMotion.x)/_CirrusScale.y;
    float lod=max(0,log2(max(1,footprint*_CirrusScale.z/_CirrusScale.y)));
    float3 cr=SAMPLE_TEXTURE2D_LOD(_CirrusShapes,sampler_CirrusShapes,uv,lod).rgb;
    // SIGGRAPH 2022 P65. ValueRemap clamps its interpolation weight.
    float density=lerp(lerp(cr.r,cr.g,saturate(ndf.y*2)),cr.b,saturate((ndf.y-.5)*2));
    density=pow(max(0,density),1.9-1.8*ndf.x);
    return density*saturate(ndf.x*ndf.x*ndf.x*2);
}
// The lower volumetric shell is strictly nested inside this surface; both positive intersections
// are retained when viewed from above. Ground and scene-depth clipping apply independently.
float2 CirrusIntersections(float3 origin,float3 ray,float sceneDistance)
{
    if(_CirrusLayer.x<.5) return -1;
    float2 hits=-1;
    float limit=min(sceneDistance,_CloudSphere.y);
    if(_CloudSphere.x>.5)
    {
        if(CloudAltitude(origin)<-.1) return -1;
        hits=CloudSphereRoots(origin,ray,_CirrusLayer.y);
        float2 ground=CloudSphereRoots(origin,ray,0);
        if(ground.y>=ground.x && ground.x>=0) limit=min(limit,ground.x);
        if(hits.y<hits.x) return -1;
    }
    else if(abs(ray.y)>1e-6) hits.x=(_CirrusLayer.y-origin.y)/ray.y;
    hits.x=hits.x>0 && hits.x<limit ? hits.x : -1;
    hits.y=hits.y>0 && hits.y<limit && abs(hits.y-hits.x)>.01 ? hits.y : -1;
    return hits;
}
#endif
