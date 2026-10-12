#ifndef NUBIS_CLOUD_LIGHTING_INCLUDED
#define NUBIS_CLOUD_LIGHTING_INCLUDED

#include "../../../AP/Shader/ShaderLibrary/InScattering.hlsl"
float _CloudAtmosphereTransmittance, _CloudAtmosphereAmbient, _CloudPerSampleAtmosphereTransmittance;

float3 CloudAtmosphereLight(float3 p, float3 direction, float3 radiance)
{
    float height = max(0, _CloudSphere.x>.5 ? CloudAltitude(p) : p.y);
    float3 up = _CloudSphere.x>.5 ? normalize(p-_CloudPlanet.xyz) : float3(0,1,0);
    float mu = dot(up,direction);
    float radius = _CloudPlanet.w;
    float horizon = -sqrt(height*(2*radius+height))/max(1,radius+height);
    if (mu < horizon) return 0;
    if (_DawnAtmosphereAvailable<.5 || _CloudAtmosphereTransmittance<.5) return radiance;
    // UE defaults to one near-ground transmission for both cloud layers.
    // Its GetTransmittanceAtGroundLevel reference is 500 m above the surface.
    if (_CloudPerSampleAtmosphereTransmittance<.5)
        return DawnGroundAtmosphereLightRadiance(direction,radiance);
    float2 optical = AtmosphereLightOpticalDepth(height,mu);
    return radiance*exp(-optical.x*_ExtinctionR-optical.y*_ExtinctionM);
}

float3 CloudSkyAmbient(float3 p, float3 fallback)
{
    if (_DawnAtmosphereAvailable<.5 || _CloudAtmosphereAmbient<.5) return fallback;
    return max(0,_DawnSkyAmbient);
}

float4 _CloudPhaseParameters; // primary g, silver g, silver weight, secondary g
float4 _CloudLightingStrength; // direct, MS, ambient, sun trace metres
float4 _CloudMultipleScattering; // reference profile length, depth power, height power
float4 _CloudStormLighting; // extinction scale, ambient dimmer
float _CloudLightingDebug;

// Normalized HG, matching UE's SamplePhaseFunction / IsotropicPhase convention.
float CloudHG(float mu,float g)
{
    float d=max(1e-4,1+g*g-2*g*clamp(mu,-1,1));
    return (1-g*g)/(4*PI*d*sqrt(d));
}
float CloudPrimaryPhase(float mu)
{
    return lerp(CloudHG(mu,_CloudPhaseParameters.x),
        CloudHG(mu,_CloudPhaseParameters.y),_CloudPhaseParameters.z);
}
float CloudMultipleProbability(float profile,float coverage,float type,float transmittance,float height)
{
    // P54 probability structure; the reference length is kept independent of view-ray ds.
    // This avoids changing source radiance when the finite integration budget changes.
    float profileProbability=saturate((saturate(profile)*_CloudMultipleScattering.x-.1)/.9);
    float typeProbability=pow(saturate(coverage*type),.25);
    float depthProbability=pow(max(1e-8,transmittance),_CloudMultipleScattering.y);
    float heightProbability=pow(max(1e-8,saturate(height)),_CloudMultipleScattering.z);
    return profileProbability*typeProbability*depthProbability*heightProbability;
}
float CloudAmbientProbability(float profile)
{
    return sqrt(saturate(1-profile));
}
#endif
