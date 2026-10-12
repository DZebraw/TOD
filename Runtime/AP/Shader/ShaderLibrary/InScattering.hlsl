#ifndef _AC2_SCATTERING_INCLUDED
    #define _AC2_SCATTERING_INCLUDED
    
    // IntegrateInscattering
    // P - current integration point
    // A - camera position
    // C - top of the atmosphere
    #include "ACMath.hlsl"
    
    TEXTURE2D(_IntegralCPDensityLUT);
    SAMPLER(sampler_IntegralCPDensityLUT);
    
    uniform float2 _DensityScaleHeight;
    uniform float _PlanetRadius;
    uniform float _AtmosphereHeight;
    uniform float _SurfaceHeight;
    
    uniform float3 _ScatteringR;
    uniform float3 _ScatteringM;
    uniform float3 _ExtinctionR;
    uniform float3 _ExtinctionM;
    uniform float _MieG;
    
    uniform half3 _LightFromOuterSpace;
    uniform float _SunIntensity;
    uniform float _SunMieG;
    float _DawnAtmosphereAvailable;
    float3 _DawnSkyAmbient;
    float4 _DawnAtmosphereLightDirections[2];
    float4 _DawnAtmosphereLightColors[2];

    // Separate analytic planet shadow from the bilinearly filtered density LUT.
    float2 AtmosphereLightOpticalDepth(float height, float cosAngle)
    {
        height = max(0, height);
        float horizon = -sqrt(height * (2 * _PlanetRadius + height)) / max(1, _PlanetRadius + height);
        if (cosAngle < horizon) return 1e20;
        if (height >= _AtmosphereHeight && cosAngle >= 0) return 0;
        // CSIntergalCPDensity stores samples at id / size, not texel centers.
        float2 uv = float2(cosAngle*.5+.5, saturate(height/max(1,_AtmosphereHeight))) + .5/512.0;
        return SAMPLE_TEXTURE2D_LOD(_IntegralCPDensityLUT, sampler_IntegralCPDensityLUT, uv, 0).xy;
    }

    // UE FAtmosphereSetup::GetTransmittanceAtGroundLevel uses a 500 m reference.
    // Shared by its default cloud-lighting mode and atmospheric height fog.
    float3 DawnGroundAtmosphereLightRadiance(float3 direction, float3 radiance)
    {
        if (_DawnAtmosphereAvailable < .5) return radiance;
        float2 optical = AtmosphereLightOpticalDepth(500,direction.y);
        return radiance*exp(-optical.x*_ExtinctionR-optical.y*_ExtinctionM);
    }

    float3 DawnOuterSpaceLightRadiance(float3 direction, float3 fallback)
    {
        if (_DawnAtmosphereAvailable < .5) return fallback;
        [unroll] for (int i=0;i<2;i++)
            if (dot(direction,_DawnAtmosphereLightDirections[i].xyz) > .99999)
                return _DawnAtmosphereLightColors[i].rgb;
        return fallback;
    }

    float3 DawnAtmosphereLightRadiance(float3 world, float3 lightDirection, float3 radiance)
    {
        if (_DawnAtmosphereAvailable < .5) return radiance;
        float3 relative = world + float3(0,_PlanetRadius,0);
        float r = length(relative);
        // Factored difference of squares preserves near-ground altitude precision.
        float height = max(0,(dot(world.xz,world.xz)+world.y*(2*_PlanetRadius+world.y))/max(1,r+_PlanetRadius));
        float2 optical = AtmosphereLightOpticalDepth(height,dot(relative/max(1,r),lightDirection));
        return radiance*exp(-optical.x*_ExtinctionR-optical.y*_ExtinctionM);
    }
    
    void ApplyPhaseFunction(inout float3 scatterR, inout float3 scatterM, float cosAngle)
    {
        scatterR *= RayleighPhase(cosAngle);
        scatterM *= MiePhaseHGCS(cosAngle, _MieG);
    }
    
    float3 RenderSun(float3 scatterM, float cosAngle)
    {
        return scatterM * MiePhaseHG(cosAngle, _SunMieG) * 0.003;
    }
    
    void GetAtmosphereDensity(float3 position, float3 planetCenter, float3 lightDir, out float2 densityAtP, out float2 particleDensityCP)
    {
        float height = length(position - planetCenter) - _PlanetRadius;
        densityAtP = ParticleDensity(height, _DensityScaleHeight.xy);
        
        float cosAngle = dot(normalize(position - planetCenter), lightDir.xyz);
        
        particleDensityCP = AtmosphereLightOpticalDepth(height, cosAngle);
    }
    
    void ComputeLocalInscattering(float2 densityAtP, float2 particleDensityCP, float2 particleDensityAP, out float3 localInscatterR, out float3 localInscatterM)
    {
        float2 particleDensityCPA = particleDensityAP + particleDensityCP;
        
        float3 Tr = particleDensityCPA.x * _ExtinctionR;
        float3 Tm = particleDensityCPA.y * _ExtinctionM;
        
        float3 extinction = exp( - (Tr + Tm));
        
        localInscatterR = densityAtP.x * extinction;
        localInscatterM = densityAtP.y * extinction;
    }
    
    float3 IntegrateInscatteringRadiance(float3 rayStart, float3 rayDir, float rayLength, float3 planetCenter, float distanceScale, float3 lightDir, float sampleCount, float3 incomingLight, out float3 extinction)
    {
        rayLength *= distanceScale;
        float3 step = rayDir * (rayLength / sampleCount);
        float stepSize = length(step) ;//* distanceScale;
        
        float2 particleDensityAP = 0;
        float3 scatterR = 0;
        float3 scatterM = 0;
        
        float2 densityAtP;
        float2 particleDensityCP;
        
        float2 prevDensityAtP;
        float3 prevLocalInscatterR, prevLocalInscatterM;
        GetAtmosphereDensity(rayStart, planetCenter, lightDir, prevDensityAtP, particleDensityCP);
        ComputeLocalInscattering(prevDensityAtP, particleDensityCP, particleDensityAP, prevLocalInscatterR, prevLocalInscatterM);
        
        //TODO loop vs Unroll?
        [loop]
        for (float s = 1.0; s < sampleCount; s += 1)
        {
            float3 p = rayStart + step * s;
            
            GetAtmosphereDensity(p, planetCenter, lightDir, densityAtP, particleDensityCP);
            particleDensityAP += (densityAtP + prevDensityAtP) * (stepSize / 2.0);
            
            prevDensityAtP = densityAtP;
            
            float3 localInscatterR, localInscatterM;
            ComputeLocalInscattering(densityAtP, particleDensityCP, particleDensityAP, localInscatterR, localInscatterM);
            
            scatterR += (localInscatterR + prevLocalInscatterR) * (stepSize / 2.0);
            scatterM += (localInscatterM + prevLocalInscatterM) * (stepSize / 2.0);
            
            prevLocalInscatterR = localInscatterR;
            prevLocalInscatterM = localInscatterM;
        }
        
        float3 m = scatterR;
        float cosAngle = dot(rayDir, lightDir.xyz);
        
        ApplyPhaseFunction(scatterR, scatterM, cosAngle);
        
        float3 lightInscatter = (scatterR * _ScatteringR + scatterM * _ScatteringM) * incomingLight;
        #if defined(_RENDERSUN)
            lightInscatter += RenderSun(m, cosAngle) * _SunIntensity * incomingLight;
        #endif

        
        // Extinction
        extinction = exp( - (particleDensityAP.x * _ExtinctionR + particleDensityAP.y * _ExtinctionM));
        
        return lightInscatter.xyz;
    }

    float3 IntegrateInscattering(float3 rayStart, float3 rayDir, float rayLength, float3 planetCenter,
        float distanceScale, float3 lightDir, float sampleCount, out float3 extinction)
    {
        return IntegrateInscatteringRadiance(rayStart,rayDir,rayLength,planetCenter,distanceScale,
            lightDir,sampleCount,_LightFromOuterSpace.rgb,extinction);
    }

    float3 IntegrateAtmosphereLights(float3 rayStart, float3 rayDir, float rayLength,
        float3 planetCenter, float sampleCount, out float3 extinction)
    {
        // Pick the active source first; only twilight overlap requires the second integration.
        int first=dot(_DawnAtmosphereLightColors[0].rgb,float3(1,1,1))>0 ? 0 : 1;
        int second=1-first;
        float3 value=IntegrateInscatteringRadiance(rayStart,rayDir,rayLength,planetCenter,1,
            _DawnAtmosphereLightDirections[first].xyz,sampleCount,_DawnAtmosphereLightColors[first].rgb,extinction);
        if(dot(_DawnAtmosphereLightColors[second].rgb,float3(1,1,1))>0)
        {
            float3 ignoredExtinction;
            value+=IntegrateInscatteringRadiance(rayStart,rayDir,rayLength,planetCenter,1,
                _DawnAtmosphereLightDirections[second].xyz,sampleCount,_DawnAtmosphereLightColors[second].rgb,ignoredExtinction);
        }
        return value;
    }
    
#endif
