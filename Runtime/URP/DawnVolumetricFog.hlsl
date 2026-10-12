#ifndef DAWN_VOLUMETRIC_FOG_INCLUDED
#define DAWN_VOLUMETRIC_FOG_INCLUDED

TEXTURE3D(_DawnIntegratedVolumetricFog);
SAMPLER(sampler_DawnIntegratedVolumetricFog);
float _DawnVolumetricFogEnabled;
float4 _DawnFogGridSize; // xyz: grid size, w: 1 / grid Z
float4 _DawnFogGridZ; // B, O, S, unused. slice = log2(depth * B + O) * S.
float4 _DawnFogGridUVScale; // screen UV -> resource UV, and sampling upper bounds.
float4 _DawnFogVolumetricRange; // view distance (m), start (m), fade inverse (1/m), orthographic
float4 _DawnFogCameraOrigin; // xyz: camera, w: camera near plane (m)
float4 _DawnFogCameraForward;
float4x4 _DawnFogInvViewProj;
float4x4 _DawnFogWorldToClip;

float DawnFogSliceDepth(float slice)
{
    return (exp2(slice / _DawnFogGridZ.z) - _DawnFogGridZ.y) / _DawnFogGridZ.x;
}

float DawnFogDepthSlice(float depth)
{
    return log2(max(1.0e-6, depth * _DawnFogGridZ.x + _DawnFogGridZ.y)) * _DawnFogGridZ.z;
}

float2 DawnFogProjectedUV(float4 clip)
{
    float2 ndc = clip.xy / clip.w;
#if UNITY_UV_STARTS_AT_TOP
    ndc.y = -ndc.y;
#endif
    return ndc * 0.5 + 0.5;
}

float3 DawnFogWorldAtDepth(float2 uv, float depth)
{
#if UNITY_REVERSED_Z
    const float farZ = 0.0, nearZ = 1.0;
#else
    const float farZ = 1.0, nearZ = UNITY_NEAR_CLIP_VALUE;
#endif
    if (_DawnFogVolumetricRange.w > 0.5)
    {
        float3 nearWorld = ComputeWorldSpacePosition(uv, nearZ, _DawnFogInvViewProj);
        return nearWorld + _DawnFogCameraForward.xyz * (depth - _DawnFogCameraOrigin.w);
    }
    float3 farWorld = ComputeWorldSpacePosition(uv, farZ, _DawnFogInvViewProj);
    float3 ray = farWorld - _DawnFogCameraOrigin.xyz;
    return _DawnFogCameraOrigin.xyz + ray * (depth / max(1.0e-6, dot(ray, _DawnFogCameraForward.xyz)));
}

float3 DawnFogCellWorld(uint3 cell, float3 offset)
{
    return DawnFogWorldAtDepth((cell.xy + offset.xy) / (_DawnFogGridSize.xy * _DawnFogGridUVScale.xy),
        DawnFogSliceDepth(max(0.0, cell.z + offset.z)));
}

// UE ParticipatingMediaCommon.ush, using a camera-to-sample ray and a vector TO the light.
float DawnVolumetricFogPhase(float g, float cosineToLight)
{
    float denominator = max(1.0e-4, 1.0 + g * g - 2.0 * g * cosineToLight);
    return (1.0 - g * g) / (4.0 * PI * denominator * sqrt(denominator));
}

float DawnVolumetricLayerDensity(float3 layer, float height)
{
    return layer.x > 0.0 ? layer.x * exp2(clamp(-layer.y * (height - layer.z), -125.0, 126.0)) : 0.0;
}

float4 DawnSampleVolumetricFog(float3 world)
{
    float depth = dot(world - _DawnFogCameraOrigin.xyz, _DawnFogCameraForward.xyz);
    if (depth <= _DawnFogVolumetricRange.y) return float4(0, 0, 0, 1);
    float4 clip = mul(_DawnFogWorldToClip, float4(world, 1));
    float2 uv = DawnFogProjectedUV(clip) * _DawnFogGridUVScale.xy;
    uv = clamp(uv, 0.5 / _DawnFogGridSize.xy, _DawnFogGridUVScale.zw);
    float z = saturate(DawnFogDepthSlice(depth) * _DawnFogGridSize.w);
    return SAMPLE_TEXTURE3D_LOD(_DawnIntegratedVolumetricFog, sampler_DawnIntegratedVolumetricFog, float3(uv, z), 0);
}

#endif
