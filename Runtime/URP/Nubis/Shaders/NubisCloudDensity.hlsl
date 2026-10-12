#ifndef NUBIS_CLOUD_DENSITY_INCLUDED
#define NUBIS_CLOUD_DENSITY_INCLUDED

TEXTURE2D(_CloudWeather); SAMPLER(sampler_CloudWeather);
TEXTURE2D(_CloudBottomType); SAMPLER(sampler_CloudBottomType);
TEXTURE2D(_CloudWeatherTo); SAMPLER(sampler_CloudWeatherTo);
TEXTURE2D(_CloudBottomTypeTo); SAMPLER(sampler_CloudBottomTypeTo);
float4 _CloudWeatherBlend; // blend, source field size, destination field size
TEXTURE2D(_CloudTopProfile); SAMPLER(sampler_CloudTopProfile);
TEXTURE2D(_CloudBottomProfile); SAMPLER(sampler_CloudBottomProfile);
TEXTURE3D(_StormNoise); SAMPLER(sampler_StormNoise);
TEXTURE2D(_AuthoredFields); SAMPLER(sampler_AuthoredFields);
TEXTURE2D(_AuthoredBottomMask); SAMPLER(sampler_AuthoredBottomMask);
TEXTURE2D(_StormFields); SAMPLER(sampler_StormFields);
TEXTURE2D(_StormBottomMask); SAMPLER(sampler_StormBottomMask);
float4 _AuthoredRegion, _NdfInfluences;
float4 _CloudField, _CloudControls, _CloudWind;
float3 _CloudNoiseWeights;
float3 _StormCenter;
float4 _StormSize, _StormMotion, _StormInfluence;
#include "NubisCloudGeometry.hlsl"

struct CloudNdf
{
    float minimum, maximum, coverage, topType, bottomType, stormMask, authoredMask;
};
struct CloudSample
{
    float density, dimensionalProfile, coverage, height, topType, bottomType, stormMask, noiseComposite;
    float2 heights;
    float authoredMask;
};
float2 CloudRotate(float2 p, float angle)
{
    float s, c; sincos(angle,s,c);
    return float2(c*p.x-s*p.y,s*p.x+c*p.y);
}
float2 StormNdfUV(float2 model) { return (model-_StormCenter.xz)/(_StormSize.x*2.32)+0.5; }
float InsideNdf(float2 uv) { return all(uv>=0) && all(uv<=1) ? 1 : 0; }
float StormMask(float2 world)
{
    float2 uv=StormNdfUV(world);
    return SAMPLE_TEXTURE2D_LOD(_StormBottomMask,sampler_StormBottomMask,saturate(uv),0).g
        *InsideNdf(uv)*saturate(_StormInfluence.x)*_NdfInfluences.y;
}
CloudNdf ReadCloudNdf(float3 world)
{
    float2 fieldP = world.xz-_CloudWind.zw*_StormMotion.x;
    float2 uv = (fieldP-_CloudField.xy)/max(1000,_CloudWeatherBlend.y)+0.5;
    float4 packed = SAMPLE_TEXTURE2D_LOD(_CloudWeather,sampler_CloudWeather,uv,0);
    float bottomType = SAMPLE_TEXTURE2D_LOD(_CloudBottomType,sampler_CloudBottomType,uv,0).r;
    if(_CloudWeatherBlend.x>0)
    {
        float2 nextUV=(fieldP-_CloudField.xy)/max(1000,_CloudWeatherBlend.z)+0.5;
        packed=lerp(packed,SAMPLE_TEXTURE2D_LOD(_CloudWeatherTo,sampler_CloudWeatherTo,nextUV,0),_CloudWeatherBlend.x);
        bottomType=lerp(bottomType,SAMPLE_TEXTURE2D_LOD(_CloudBottomTypeTo,sampler_CloudBottomTypeTo,nextUV,0).r,_CloudWeatherBlend.x);
    }
    CloudNdf field;
    field.minimum = packed.r;
    field.maximum = packed.g;
    field.coverage = packed.b;
    field.topType = packed.a;
    field.bottomType = bottomType;
    field.stormMask = StormMask(world.xz);

    // Authored coordinates are anchored in model/world space and never use field wind or time.
    float2 authoredUV=(world.xz-_AuthoredRegion.xy)/_AuthoredRegion.zw+0.5;
    float2 authoredControl=SAMPLE_TEXTURE2D_LOD(_AuthoredBottomMask,sampler_AuthoredBottomMask,saturate(authoredUV),0).rg;
    field.authoredMask=authoredControl.g*InsideNdf(authoredUV)*_NdfInfluences.x;
    if (field.authoredMask>0)
    {
        float4 authored=SAMPLE_TEXTURE2D_LOD(_AuthoredFields,sampler_AuthoredFields,saturate(authoredUV),0);
        packed=lerp(packed,authored,field.authoredMask);
        field.bottomType=lerp(field.bottomType,authoredControl.r,field.authoredMask);
    }
    if (field.stormMask>0)
    {
        float2 stormUV=StormNdfUV(world.xz);
        float4 storm=SAMPLE_TEXTURE2D_LOD(_StormFields,sampler_StormFields,saturate(stormUV),0);
        float bottom=SAMPLE_TEXTURE2D_LOD(_StormBottomMask,sampler_StormBottomMask,saturate(stormUV),0).r;
        packed=lerp(packed,storm,field.stormMask);
        field.bottomType=lerp(field.bottomType,bottom,field.stormMask);
    }
    field.minimum=packed.r; field.maximum=max(packed.r+0.001,packed.g);
    field.coverage=packed.b; field.topType=packed.a;
    // Finite 16 km demonstration patch. Fade spatial boundaries without clamping repeated weather data.
    float2 edge = abs((world.xz-_CloudField.xy)/_CloudField.z)*2;
    float boundary = _CloudSphere.x > 0.5 ? 1 : 1-smoothstep(0.86,1.0,max(edge.x,edge.y));
    field.coverage = saturate(field.coverage*_CloudControls.x)*boundary;
    field.topType = saturate(field.topType+_CloudControls.y);
    field.bottomType = saturate(field.bottomType+_CloudControls.z);
    field.minimum = _CloudField.w+field.minimum*_StormSize.y;
    field.maximum = _CloudField.w+field.maximum*_StormSize.y;
    return field;
}
float ProfileLookup(Texture2D<float4> lut, SamplerState samplerLut, float type, float height)
{
    // Inset half a texel: LUT endpoints represent exactly h=0/1 and type=0/1.
    float2 uv = (saturate(float2(type,height))*255+0.5)/256;
    return lut.SampleLevel(samplerLut,uv,0).r;
}
float4 SampleCloudNoise(float3 materialPosition, float footprint)
{
    float mip=clamp(log2(max(1,footprint*128/_CloudControls.w)),0,4);
    return SAMPLE_TEXTURE3D_LOD(_StormNoise,sampler_StormNoise,materialPosition/_CloudControls.w,mip);
}
float StormBandSpeed(float band) { return lerp(0.052,0.008,band/5)*_StormMotion.y; }
float3 StormMaterialPositionAtTime(float3 world, float band, float time)
{
    float3 local = world-_StormCenter;
    float r = length(local.xz)/_StormSize.x;
    float h = local.y/_StormSize.y;
    float speed = StormBandSpeed(band);
    float skew = _StormMotion.z*(log(0.25+r)*0.8 + h*0.3);
    local.xz = CloudRotate(local.xz,-time*speed+skew);
    return local+_StormCenter-float3(0,time*2,0);
}
float3 StormMaterialPosition(float3 world, float band) { return StormMaterialPositionAtTime(world,band,_StormMotion.x); }

float3 BacktraceStormBand(float3 model, float band, float deltaTime)
{
    float3 local = model-_StormCenter;
    // Visible material rises at 2 m/s. The height-dependent skew must be inverted too.
    local.y -= deltaTime*2;
    float skewDelta = _StormMotion.z*0.3*deltaTime*2/_StormSize.y;
    local.xz = CloudRotate(local.xz,-deltaTime*StormBandSpeed(band)+skewDelta);
    return local+_StormCenter;
}
float3 PreviousCloudPosition(float3 world, float deltaTime, out float confidence)
{
    float3 model = CloudModelPosition(world);
    float mask = StormMask(model.xz);
    float3 previousSky = model-float3(_CloudWind.x,0,_CloudWind.y)*deltaTime;
    float band = saturate(length(model.xz-_StormCenter.xz)/(_StormSize.x*1.16))*5;
    float lo = min(floor(band),4), blend = smoothstep(0,1,band-lo);
    float3 previousStorm = lerp(BacktraceStormBand(model,lo,deltaTime),BacktraceStormBand(model,lo+1,deltaTime),blend);
    // A blended pair of advected noise fields has no single exact velocity.
    float overlap = 4*blend*(1-blend)*mask;
    float mixedBoundary = 4*mask*(1-mask);
    confidence = 1-0.35*overlap-0.25*mixedBoundary;
    confidence *= exp(-length(_CloudWind.zw)*abs(deltaTime)*0.01);
    return CloudWorldPosition(lerp(previousSky,previousStorm,mask));
}
float4 SharedCloudNoise(float3 world, float mask, float frequency, float footprint)
{
    float3 regular = world-float3(_CloudWind.x,0,_CloudWind.y)*_StormMotion.x;
    float4 sky = SampleCloudNoise(regular*frequency,footprint*frequency);
    if (mask <= 0) return sky;
    float band = saturate(length(world.xz-_StormCenter.xz)/(_StormSize.x*1.16))*5;
    float lo = min(floor(band),4);
    float blend = smoothstep(0,1,band-lo);
    // Two independent samples, not a single sample at an interpolated angle.
    float4 a = SampleCloudNoise(StormMaterialPosition(world,lo)*frequency,footprint*frequency);
    float4 b = SampleCloudNoise(StormMaterialPosition(world,lo+1)*frequency,footprint*frequency);
    return lerp(sky,lerp(a,b,blend),mask);
}
CloudSample SampleCloud(float3 world, float footprint)
{
    world = CloudModelPosition(world);
    CloudSample result = (CloudSample)0;
    CloudNdf field = ReadCloudNdf(world);
    result.coverage = field.coverage;
    result.authoredMask = field.authoredMask;
    result.topType = field.topType;
    result.bottomType = field.bottomType;
    result.stormMask = field.stormMask;
    result.heights = float2(field.minimum,field.maximum);
    if (field.coverage <= 0 || field.maximum <= field.minimum || world.y <= field.minimum || world.y >= field.maximum)
        return result;
    float h = saturate((world.y-field.minimum)/(field.maximum-field.minimum));
    result.height = h;
    float top = ProfileLookup(_CloudTopProfile,sampler_CloudTopProfile,field.topType,h);
    float bottom = ProfileLookup(_CloudBottomProfile,sampler_CloudBottomProfile,field.bottomType,h);
    // Engineering approximation to the flipped-profile anvil on slide 160. Same LUT and final erosion.
    float flipped = ProfileLookup(_CloudTopProfile,sampler_CloudTopProfile,0.36,1-h);
    float anvilWindow = smoothstep(0.57,0.76,h)*(1-smoothstep(0.86,1,h));
    float anvil = flipped*anvilWindow*field.stormMask*_StormInfluence.y;
    top = max(top,anvil);
    result.dimensionalProfile = top*bottom*field.coverage;
    if (result.dimensionalProfile <= 0) return result;

    float4 noise = SharedCloudNoise(world,field.stormMask,1,footprint);
    float worley = dot(noise.gba,_CloudNoiseWeights);
    float billowMix = smoothstep(0.1,0.9,h)*0.20;
    float perlinWorley = saturate((noise.r-0.32)/0.68);
    float composite = lerp(perlinWorley,worley,billowMix);
    float3 detail = SharedCloudNoise(world,field.stormMask,3.1,footprint).gba;
    float detailErosion = (1-dot(detail,_CloudNoiseWeights))*_StormSize.w*lerp(0.35,1,h);
    result.noiseComposite = saturate(composite-detailErosion);
    // SIGGRAPH 2022 slide 34. Coverage changes the erosion threshold, not just opacity.
    result.density = saturate(result.noiseComposite-(1-result.dimensionalProfile))*_StormSize.z;
    return result;
}
CloudSample SampleCloud(float3 world) { return SampleCloud(world,0); }
float Density(float3 world) { return SampleCloud(world,0).density; }
float Density(float3 world, float footprint) { return SampleCloud(world,footprint).density; }

#endif
