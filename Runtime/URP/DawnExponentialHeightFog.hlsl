#ifndef DAWN_EXPONENTIAL_HEIGHT_FOG_INCLUDED
#define DAWN_EXPONENTIAL_HEIGHT_FOG_INCLUDED

#include "../AP/Shader/ShaderLibrary/InScattering.hlsl"
#include "DawnVolumetricFog.hlsl"

// UE SceneCore.cpp: artist density/falloff / 1000 per cm, converted to Unity meters.
// x: density * 0.1, y: height falloff * 0.1, z: fog actor height (m), w: start distance (m).
float4 _DawnExponentialFogParameters;
// Second layer: density * 0.1 (zero when disabled), falloff * 0.1, absolute height (m).
float4 _DawnExponentialFogParameters2;
// x: horizontal end distance (m), y: fog cutoff distance (m); zero disables either.
float4 _DawnExponentialFogDistances;
// RGB: additional inscattering luminance, A: minimum transmittance (1 - Fog Max Opacity).
float4 _DawnFogInscattering;
float _DawnFogEnabled;
// Height Fog Contribution (zero disables atmosphere), directional exponent, directional start (m).
float4 _DawnFogAtmosphere;
float4 _DawnFogSkyAmbientScale;
float4 _DawnFogDirectionalColor;

float DawnHeightFogOriginTerm(float3 layer, float observerHeight)
{
    if (layer.x <= 0.0) return 0.0;
    return layer.x * exp2(clamp(-layer.y * (observerHeight - layer.z), -125.0, 126.0));
}

float DawnHeightFogLineIntegral(float heightFalloff, float verticalDistance, float originTerm)
{
    if (originTerm <= 0.0) return 0.0;
    // UE CalculateLineIntegralShared, including the epsilon/Taylor branch.
    float falloff = max(-127.0, heightFalloff * verticalDistance);
    const float ln2 = 0.69314718056;
    float lineIntegral = abs(falloff) > 0.01
        ? (1.0 - exp2(-falloff)) / falloff
        : ln2 - 0.5 * ln2 * ln2 * falloff;
    return originTerm * lineIntegral;
}

float DawnHeightFogSharedIntegral(float3 origin, float3 direction, float distance, float excludeDistance,
    out float rayLength, out float3 receiverDirection, out float3 fogOrigin)
{
    // UE clamps the observer against every layer which has nonzero density.
    float3 primary = _DawnExponentialFogParameters.xyz;
    float3 second = _DawnExponentialFogParameters2.xyz;
    float observerHeight = origin.y;
    if (primary.x > 0.0) observerHeight = min(observerHeight, primary.z + 655.36);
    if (second.x > 0.0) observerHeight = min(observerHeight, second.z + 655.36);
    float3 cameraToReceiver = direction * distance;
    float horizontalLengthSq = dot(cameraToReceiver.xz, cameraToReceiver.xz);
    float endDistance = _DawnExponentialFogDistances.x;
    if (endDistance > 0.0 && horizontalLengthSq > endDistance * endDistance)
        cameraToReceiver *= endDistance * rsqrt(max(0.0001, horizontalLengthSq));
    cameraToReceiver.y += origin.y - observerHeight;
    float lengthSq = dot(cameraToReceiver, cameraToReceiver);
    // UE's 1e-8 cm^2 epsilon is 1e-12 m^2, not 1e-8 m^2.
    float inverseLength = rsqrt(max(lengthSq, 1.0e-12));
    rayLength = lengthSq * inverseLength;
    receiverDirection = cameraToReceiver * inverseLength;
    fogOrigin = float3(origin.x, observerHeight, origin.z);
    float verticalDistance = cameraToReceiver.y;
    float cutoffDistance = _DawnExponentialFogDistances.y;
    if (cutoffDistance > 0.0 && rayLength > cutoffDistance)
    {
        rayLength = 0.0;
        return 0.0;
    }

    float2 originTerms = float2(DawnHeightFogOriginTerm(primary, observerHeight),
        DawnHeightFogOriginTerm(second, observerHeight));
    float startDistance = max(_DawnExponentialFogParameters.w, excludeDistance);
    if (startDistance > 0.0)
    {
        fogOrigin += receiverDirection * startDistance;
        float excludedFraction = startDistance * inverseLength;
        float excludedHeight = observerHeight + excludedFraction * verticalDistance;
        verticalDistance *= 1.0 - excludedFraction;
        rayLength *= 1.0 - excludedFraction;
        originTerms.x = primary.x > 0.0
            ? primary.x * exp2(-max(-127.0, primary.y * (excludedHeight - primary.z))) : 0.0;
        originTerms.y = second.x > 0.0
            ? second.x * exp2(-max(-127.0, second.y * (excludedHeight - second.z))) : 0.0;
    }
    if (rayLength <= 0.0) return 0.0;

    // Keep the shared integral: UE's directional term uses its own remaining distance.
    return DawnHeightFogLineIntegral(primary.y, verticalDistance, originTerms.x)
        + DawnHeightFogLineIntegral(second.y, verticalDistance, originTerms.y);
}

// UE HeightFogCommon.ush's default branch: PROJECT_EXPFOG_MATCHES_VFOG = 0,
// PROJECT_SUPPORT_SKY_ATMOSPHERE_AFFECTS_HEIGHFOG = 1. Returns inscattering RGB and T.
float4 DawnEvaluateAnalyticHeightFog(float3 origin, float3 direction, float distance, float excludeDistance)
{
    if (_DawnFogEnabled < 0.5 || distance <= 0.0) return float4(0, 0, 0, 1);
    float rayLength;
    float3 receiverDirection;
    float3 fogOrigin;
    float integral = DawnHeightFogSharedIntegral(origin, direction, distance, excludeDistance,
        rayLength, receiverDirection, fogOrigin);
    if (rayLength <= 0.0) return float4(0, 0, 0, 1);
    float transmittance = max(saturate(exp2(-integral * rayLength)), _DawnFogInscattering.a);
    float atmosphereContribution = _DawnAtmosphereAvailable > 0.5 ? _DawnFogAtmosphere.x : 0.0;
    float3 ambient = _DawnFogInscattering.rgb
        + _DawnFogSkyAmbientScale.rgb * max(0, _DawnSkyAmbient) * atmosphereContribution;

    // The authored directional luminance is scaled by the primary light's luminance.
    // Default UE 5.6 working color space is sRGB/Rec.709 (legacy coefficients disabled).
    int primaryLight = _DawnAtmosphereLightColors[0].w > 0.0 ? 0 : 1;
    float primaryLuminance = dot(_DawnAtmosphereLightColors[primaryLight].rgb,
        float3(0.212639, 0.715169, 0.072192));
    float3 authoredDirectional = _DawnFogDirectionalColor.rgb * primaryLuminance;
    float3 directional = 0.0;
    [unroll] for (int i = 0; i < 2; i++)
    {
        if (_DawnAtmosphereLightColors[i].w > 0.0)
        {
            float3 lightDirection = _DawnAtmosphereLightDirections[i].xyz;
            float3 color = authoredDirectional + atmosphereContribution * DawnGroundAtmosphereLightRadiance(
                lightDirection, _DawnAtmosphereLightColors[i].rgb);
            float phase = pow(saturate(dot(receiverDirection, lightDirection)), _DawnFogAtmosphere.y);
            // UE HeightFogCommon.ush keeps the analytic directional lobe unshadowed.
            // Geometry shadows belong to the finite froxel volume; sparse shadow
            // integration here produces hard dark silhouettes across distant haze.
            directional += color * phase / (4.0 * PI);
        }
    }
    float directionalIntegral = integral * max(rayLength - _DawnFogAtmosphere.z, 0.0);
    // UE does not clamp directional opacity to Fog Max Opacity.
    return float4(ambient * (1.0 - transmittance)
        + directional * (1.0 - saturate(exp2(-directionalIntegral))), transmittance);
}

// UE CalculateHeightFog / CombineVolumetricFog: froxels replace the near analytic range.
float4 DawnEvaluateHeightFog(float3 origin, float3 direction, float distance)
{
    float forwardCosine = dot(direction, _DawnFogCameraForward.xyz);
    bool useVolume = _DawnVolumetricFogEnabled > 0.5 && forwardCosine > 1.0e-6;
    float excludeDistance = useVolume ? _DawnFogVolumetricRange.x / forwardCosine : 0;
    float4 analytic = DawnEvaluateAnalyticHeightFog(origin, direction, distance, excludeDistance);
    if (!useVolume || _DawnFogEnabled < 0.5 || distance <= 0) return analytic;
    float4 volume = DawnSampleVolumetricFog(origin + direction * distance);
    return float4(volume.rgb + analytic.rgb * volume.a, volume.a * analytic.a);
}

float4 DawnFogCloudLayer(float4 cloud, float3 origin, float3 direction, float distance)
{
    // UE VolumetricCloud.usf: L = T_fog * L_cloud + fog.rgb * cloud coverage.
    float4 fog = DawnEvaluateHeightFog(origin, direction, distance);
    cloud.rgb = cloud.rgb * fog.a + fog.rgb * saturate(1.0 - cloud.a);
    return cloud;
}

#endif
