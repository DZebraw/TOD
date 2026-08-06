Shader "Hidden/DawnTOD/VolumetricCloud"
{
    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.universal": "[14.0.0,15.0.0)"
        }

        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        HLSLINCLUDE
        #pragma target 4.5
        #pragma multi_compile_instancing
        #pragma multi_compile _ _STEREO_MULTIVIEW_ON _STEREO_INSTANCING_ON

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE3D(_DawnCloudShapeNoise);
        SAMPLER(sampler_DawnCloudShapeNoise);
        TEXTURE3D(_DawnCloudDetailNoise);
        SAMPLER(sampler_DawnCloudDetailNoise);
        TEXTURE2D(_DawnCloudWeatherMap);
        SAMPLER(sampler_DawnCloudWeatherMap);
        TEXTURE2D(_DawnCloudMaskNoise);
        SAMPLER(sampler_DawnCloudMaskNoise);
        TEXTURE2D(_DawnCloudBlueNoise);
        SAMPLER(sampler_DawnCloudBlueNoise);
        TEXTURE2D_X(_DawnCloudLowDepthTexture);
        SAMPLER(sampler_DawnCloudLowDepthTexture);
        TEXTURE2D_X(_DawnCloudTexture);
        SAMPLER(sampler_DawnCloudTexture);
        TEXTURE2D_X(_DawnCloudDistanceTexture);
        SAMPLER(sampler_DawnCloudDistanceTexture);
        TEXTURE2D_X(_DawnCloudHistoryTexture);
        SAMPLER(sampler_DawnCloudHistoryTexture);
        TEXTURE2D_X(_DawnCloudHistoryDistanceTexture);
        SAMPLER(sampler_DawnCloudHistoryDistanceTexture);

        float4 _DawnCloudShadowRayOrigin;
        float4 _DawnCloudShadowRight;
        float4 _DawnCloudShadowUp;
        float4 _DawnCloudShadowLightDirection;

        float4 _DawnCloudBoundsMin;
        float4 _DawnCloudBoundsMax;
        float4 _DawnCloudShapeNoiseWeights;
        float4 _DawnCloudColorA;
        float4 _DawnCloudColorB;
        float4 _DawnCloudPhaseParameters;
        float4 _DawnCloudMultiScatterParameters;
        float4 _DawnCloudDiffuseFieldParameters;
        float4 _DawnCloudDiffuseFieldTransportParameters;
        float4 _DawnCloudAmbientSkyColor;
        float4 _DawnCloudAmbientEquatorColor;
        float4 _DawnCloudAmbientGroundColor;
        float4 _DawnCloudHeightProfileParameters;
        float4 _DawnCloudSpeedWarp;
        float4 _DawnCloudBlueNoiseScale;
        float4 _DawnCloudBufferSize;
        float4 _DawnCloudPreviousCameraPosition;
        float4x4 _DawnCloudPreviousViewProjection;

        float _DawnCloudCoverage;
        float _DawnCloudWeatherMapTiling;
        float _DawnCloudShapeTiling;
        float _DawnCloudDetailTiling;
        float _DawnCloudDensityOffset;
        float _DawnCloudDensityMultiplier;
        float _DawnCloudDetailWeights;
        float _DawnCloudDetailNoiseWeight;
        float _DawnCloudHeightWeights;
        float _DawnCloudHeightProfileBlend;
        float _DawnCloudRayStepExponent;
        float _DawnCloudRayStepLength;
        float _DawnCloudRayOffsetStrength;
        float _DawnCloudColorOffset1;
        float _DawnCloudColorOffset2;
        float _DawnCloudPowderEffectIntensity;
        float _DawnCloudAmbientOcclusionStrength;
        float _DawnCloudExtinctionScale;
        float _DawnCloudLightAbsorptionTowardSun;
        float _DawnCloudSelfShadowStrength;
        float _DawnCloudLightAbsorptionThroughCloud;
        float _DawnCloudHistoryValid;
        float _DawnCloudTemporalBlend;
        float _DawnCloudDepthDownsampleScale;
        int _DawnCloudMaxRayMarchSteps;

        // HanPi Volume Cloud's phi_fwd model assumes a highly reflective
        // water cloud. See THIRD_PARTY_NOTICES.md for attribution and license.
        static const float DawnCloudDiffuseFieldAlbedo = 0.999;
        static const float DawnCloudDiffuseFieldKappaOdScale =
            0.05477225575;
        float DawnCloudRemap(
            float value,
            float inputMinimum,
            float inputMaximum,
            float outputMinimum,
            float outputMaximum)
        {
            float inputRange = max(inputMaximum - inputMinimum, 0.00001);
            return outputMinimum + (value - inputMinimum) / inputRange *
                   (outputMaximum - outputMinimum);
        }

        float DawnCloudHenyeyGreenstein(float cosineAngle, float anisotropy)
        {
            float anisotropySquared = anisotropy * anisotropy;
            return (1.0 - anisotropySquared) /
                   (4.0 * PI * pow(
                       max(0.00001, 1.0 + anisotropySquared -
                           2.0 * anisotropy * cosineAngle),
                       1.5));
        }

        float DawnCloudPhase(float cosineAngle)
        {
            float forwardAnisotropy = clamp(
                _DawnCloudPhaseParameters.x,
                0.0,
                0.9);
            float backwardAnisotropy = clamp(
                _DawnCloudPhaseParameters.y,
                -0.75,
                0.0);
            float forward = DawnCloudHenyeyGreenstein(
                cosineAngle,
                forwardAnisotropy);
            float backward = DawnCloudHenyeyGreenstein(
                cosineAngle,
                backwardAnisotropy);
            float directionalPhase = lerp(
                backward,
                forward,
                saturate(_DawnCloudPhaseParameters.z)) *
                max(0.0, _DawnCloudPhaseParameters.w);
            return directionalPhase;
        }

        float3 DawnCloudSafeRayDirection(float3 direction)
        {
            float3 signs = lerp(-1.0, 1.0, step(0.0, direction));
            return signs * max(abs(direction), 0.00001);
        }

        float2 DawnCloudRayBoxDistance(
            float3 boundsMinimum,
            float3 boundsMaximum,
            float3 rayOrigin,
            float3 rayDirection)
        {
            float3 inverseRayDirection = rcp(DawnCloudSafeRayDirection(rayDirection));
            float3 t0 = (boundsMinimum - rayOrigin) * inverseRayDirection;
            float3 t1 = (boundsMaximum - rayOrigin) * inverseRayDirection;
            float3 nearDistance = min(t0, t1);
            float3 farDistance = max(t0, t1);
            float distanceToBox = max(max(nearDistance.x, nearDistance.y), nearDistance.z);
            float distanceOutOfBox = min(min(farDistance.x, farDistance.y), farDistance.z);
            float entryDistance = max(0.0, distanceToBox);
            float distanceInsideBox = max(0.0, distanceOutOfBox - entryDistance);
            return float2(entryDistance, distanceInsideBox);
        }

        float DawnCloudPackedWeatherWeight(float3 weatherSample)
        {
            float channelDifference =
                abs(weatherSample.g - weatherSample.r) +
                abs(weatherSample.b - weatherSample.r);
            return smoothstep(0.01, 0.05, channelDifference);
        }

        float DawnCloudEvaluateTopHeightProxy(float2 worldPosition)
        {
            float3 boundsMinimum = _DawnCloudBoundsMin.xyz;
            float3 boundsMaximum = _DawnCloudBoundsMax.xyz;
            float3 boundsCenter = (boundsMinimum + boundsMaximum) * 0.5;
            float shapeSpeed = _Time.y * _DawnCloudSpeedWarp.x;
            float2 weatherUv =
                (worldPosition - boundsCenter.xz) *
                _DawnCloudWeatherMapTiling + 0.5;
            float maskValue = SAMPLE_TEXTURE2D_LOD(
                _DawnCloudMaskNoise,
                sampler_DawnCloudMaskNoise,
                weatherUv + float2(shapeSpeed * 0.5, 0.0),
                0).r;
            float3 weatherSample = SAMPLE_TEXTURE2D_LOD(
                _DawnCloudWeatherMap,
                sampler_DawnCloudWeatherMap,
                weatherUv + float2(shapeSpeed * 0.4, 0.0),
                0).rgb;
            float packedWeather = DawnCloudPackedWeatherWeight(
                weatherSample);
            float coverageSource = lerp(
                maskValue,
                weatherSample.r,
                packedWeather);
            float cloudType = lerp(
                weatherSample.r,
                weatherSample.g,
                packedWeather);

            float baseSoftness = clamp(
                _DawnCloudHeightProfileParameters.x,
                0.01,
                0.3);
            float bodyHeight = clamp(
                _DawnCloudHeightProfileParameters.y,
                baseSoftness + 0.05,
                0.95);
            float growthPattern = smoothstep(0.15, 0.85, cloudType);
            float localTopHeight = lerp(
                bodyHeight,
                1.0,
                growthPattern *
                saturate(_DawnCloudHeightProfileParameters.z));

            float coverage = saturate(_DawnCloudCoverage);
            float coverageThreshold = 1.0 - coverage;
            const float coverageFeather = 0.12;
            float coverageMask = smoothstep(
                coverageThreshold,
                coverageThreshold + coverageFeather,
                coverageSource);
            coverageMask = lerp(
                coverageMask,
                1.0,
                step(0.9999, coverage));
            return saturate(localTopHeight * coverageMask);
        }

        float DawnCloudEvaluateDiffuseBoundaryLight(
            float3 position,
            float3 lightDirection)
        {
            float boundaryInfluence = saturate(
                _DawnCloudDiffuseFieldParameters.w);
            if (boundaryInfluence <= 0.0)
            {
                return 1.0;
            }

            float weatherPatternSize = rcp(max(
                _DawnCloudWeatherMapTiling,
                0.000001));
            float sampleStep = clamp(
                weatherPatternSize * 0.001,
                25.0,
                200.0);
            float heightLeft = DawnCloudEvaluateTopHeightProxy(
                position.xz - float2(sampleStep, 0.0));
            float heightRight = DawnCloudEvaluateTopHeightProxy(
                position.xz + float2(sampleStep, 0.0));
            float heightDown = DawnCloudEvaluateTopHeightProxy(
                position.xz - float2(0.0, sampleStep));
            float heightUp = DawnCloudEvaluateTopHeightProxy(
                position.xz + float2(0.0, sampleStep));

            float cloudLayerHeight = max(
                _DawnCloudBoundsMax.y - _DawnCloudBoundsMin.y,
                0.01);
            float heightDerivativeX =
                (heightRight - heightLeft) * cloudLayerHeight /
                max(2.0 * sampleStep, 0.01);
            float heightDerivativeZ =
                (heightUp - heightDown) * cloudLayerHeight /
                max(2.0 * sampleStep, 0.01);
            float3 topNormal = normalize(float3(
                -heightDerivativeX,
                1.0,
                -heightDerivativeZ));
            const float lightWrap = 0.5;
            float wrappedBoundaryLight = saturate(
                (dot(topNormal, lightDirection) + lightWrap) /
                (1.0 + lightWrap));
            return lerp(
                1.0,
                wrappedBoundaryLight,
                boundaryInfluence);
        }

        struct DawnCloudProperties
        {
            float density;
            float coarseDensity;
            float height;
            float localHeight;
            float cloudType;
            float precipitation;
        };

        DawnCloudProperties DawnCloudEvaluatePropertiesAtLod(
            float3 rayPosition,
            float shapeNoiseLod)
        {
            DawnCloudProperties properties;
            properties.density = 0.0;
            properties.coarseDensity = 0.0;
            properties.height = 0.0;
            properties.localHeight = 0.0;
            properties.cloudType = 0.0;
            properties.precipitation = 0.0;

            float3 boundsMinimum = _DawnCloudBoundsMin.xyz;
            float3 boundsMaximum = _DawnCloudBoundsMax.xyz;
            float3 boundsSize = max(boundsMaximum - boundsMinimum, 0.01);
            float3 boundsCenter = (boundsMinimum + boundsMaximum) * 0.5;

            float heightPercent =
                (rayPosition.y - boundsMinimum.y) /
                max(boundsSize.y, 0.01);
            properties.height = saturate(heightPercent);
            float shapeSpeed = _Time.y * _DawnCloudSpeedWarp.x;
            float detailSpeed = _Time.y * _DawnCloudSpeedWarp.y;
            float3 shapeUv = rayPosition * _DawnCloudShapeTiling +
                float3(shapeSpeed, shapeSpeed * 0.2, 0.0);
            float3 detailUv = rayPosition * _DawnCloudDetailTiling +
                float3(detailSpeed, detailSpeed * 0.2, 0.0);
            // Keep the horizontal weather pattern at a fixed world scale.
            // Bounds expansion should reveal more clouds, not resize them.
            float2 weatherUv =
                (rayPosition.xz - boundsCenter.xz) *
                _DawnCloudWeatherMapTiling + 0.5;

            float maskValue = SAMPLE_TEXTURE2D_LOD(
                _DawnCloudMaskNoise,
                sampler_DawnCloudMaskNoise,
                weatherUv + float2(shapeSpeed * 0.5, 0.0),
                0).r;
            float3 weatherSample = SAMPLE_TEXTURE2D_LOD(
                _DawnCloudWeatherMap,
                sampler_DawnCloudWeatherMap,
                weatherUv + float2(shapeSpeed * 0.4, 0.0),
                0).rgb;
            float packedWeather = DawnCloudPackedWeatherWeight(
                weatherSample);
            float coverageSource = lerp(
                maskValue,
                weatherSample.r,
                packedWeather);
            float weatherValue = lerp(
                weatherSample.r,
                weatherSample.g,
                packedWeather);
            float precipitation = weatherSample.b * packedWeather;
            properties.cloudType = saturate(weatherValue);
            properties.precipitation = saturate(precipitation);
            float4 shapeNoise = SAMPLE_TEXTURE3D_LOD(
                _DawnCloudShapeNoise,
                sampler_DawnCloudShapeNoise,
                shapeUv + maskValue * _DawnCloudSpeedWarp.z * 0.1,
                max(shapeNoiseLod, 0.0));
            float4 detailNoise = SAMPLE_TEXTURE3D_LOD(
                _DawnCloudDetailNoise,
                sampler_DawnCloudDetailNoise,
                detailUv + shapeNoise.r * _DawnCloudSpeedWarp.w * 0.1,
                max(shapeNoiseLod - 0.5, 0.0));

            float edgeFadeDistance = clamp(
                min(boundsSize.x, boundsSize.z) * 0.05,
                10.0,
                500.0);
            float edgeDistanceX = min(
                rayPosition.x - boundsMinimum.x,
                boundsMaximum.x - rayPosition.x);
            float edgeDistanceZ = min(
                rayPosition.z - boundsMinimum.z,
                boundsMaximum.z - rayPosition.z);
            float edgeWeight = smoothstep(
                0.0,
                edgeFadeDistance,
                min(edgeDistanceX, edgeDistanceZ));

            float gradientMinimum = DawnCloudRemap(weatherValue, 0.0, 1.0, 0.1, 0.6);
            float gradientMaximum = DawnCloudRemap(
                weatherValue,
                0.0,
                1.0,
                gradientMinimum,
                0.9);
            float standardHeightGradient =
                saturate(DawnCloudRemap(heightPercent, 0.0, gradientMinimum, 0.0, 1.0)) *
                saturate(DawnCloudRemap(heightPercent, 1.0, gradientMaximum, 0.0, 1.0));
            float alternateHeightGradient =
                saturate(DawnCloudRemap(heightPercent, 0.0, weatherValue, 1.0, 0.0)) *
                saturate(DawnCloudRemap(heightPercent, 0.0, gradientMinimum, 0.0, 1.0));
            float legacyHeightGradient = lerp(
                standardHeightGradient,
                alternateHeightGradient,
                _DawnCloudHeightWeights);

            float baseSoftness = clamp(
                _DawnCloudHeightProfileParameters.x,
                0.01,
                0.3);
            float bodyHeight = clamp(
                _DawnCloudHeightProfileParameters.y,
                baseSoftness + 0.05,
                0.95);
            float verticalGrowth = saturate(
                _DawnCloudHeightProfileParameters.z);
            float topSoftness = clamp(
                _DawnCloudHeightProfileParameters.w,
                0.02,
                0.5);
            float growthPattern = smoothstep(
                0.15,
                0.85,
                properties.cloudType);
            float localTopHeight = lerp(
                bodyHeight,
                1.0,
                growthPattern * verticalGrowth);
            properties.localHeight = saturate(
                heightPercent / max(localTopHeight, 0.001));
            float topFadeDistance = min(
                topSoftness,
                max(localTopHeight - baseSoftness, 0.02));
            float baseProfile = smoothstep(
                0.0,
                baseSoftness,
                heightPercent);
            float topProfile = 1.0 - smoothstep(
                localTopHeight - topFadeDistance,
                localTopHeight,
                heightPercent);
            float artDirectedHeightGradient = baseProfile * topProfile;
            float heightGradient = lerp(
                legacyHeightGradient,
                artDirectedHeightGradient,
                saturate(_DawnCloudHeightProfileBlend)) * edgeWeight;

            float coverage = saturate(_DawnCloudCoverage);
            float coverageThreshold = 1.0 - coverage;
            const float coverageFeather = 0.12;
            float coverageMask = smoothstep(
                coverageThreshold,
                coverageThreshold + coverageFeather,
                coverageSource);
            coverageMask = lerp(
                coverageMask,
                1.0,
                step(0.9999, coverage));

            float shapeWeightSum = dot(_DawnCloudShapeNoiseWeights, 1.0);
            float4 normalizedShapeWeights = _DawnCloudShapeNoiseWeights /
                                             (abs(shapeWeightSum) < 0.00001
                                                 ? 0.00001
                                                 : shapeWeightSum);
            float shapeFbm = dot(shapeNoise, normalizedShapeWeights) *
                             heightGradient;
            float baseShapeDensity =
                (shapeFbm + _DawnCloudDensityOffset * 0.01) *
                coverageMask;
            float precipitationBody =
                properties.precipitation *
                (1.0 - smoothstep(0.45, 1.0, properties.localHeight));
            baseShapeDensity +=
                precipitationBody * heightGradient * coverageMask * 0.08;
            if (baseShapeDensity <= 0.0)
            {
                return properties;
            }

            float detailFbm = pow(max(detailNoise.r, 0.00001), _DawnCloudDetailWeights);
            float inverseShapeDensity = 1.0 - baseShapeDensity;
            float detailErodeWeight = inverseShapeDensity * inverseShapeDensity *
                                      inverseShapeDensity;
            float heightErosion = lerp(
                0.35,
                1.15,
                smoothstep(0.08, 1.0, properties.localHeight));
            float typeErosion = lerp(
                0.65,
                1.1,
                properties.cloudType);
            float precipitationErosion = lerp(
                1.0,
                0.55,
                precipitationBody);
            float cloudDensity = baseShapeDensity - detailFbm * detailErodeWeight *
                _DawnCloudDetailNoiseWeight * heightErosion *
                typeErosion * precipitationErosion;
            properties.coarseDensity = saturate(
                baseShapeDensity * _DawnCloudDensityMultiplier);
            properties.density = saturate(
                cloudDensity * _DawnCloudDensityMultiplier);
            return properties;
        }

        DawnCloudProperties DawnCloudEvaluateProperties(float3 rayPosition)
        {
            return DawnCloudEvaluatePropertiesAtLod(rayPosition, 0.0);
        }

        float DawnCloudSampleDensity(float3 rayPosition)
        {
            return DawnCloudEvaluateProperties(rayPosition).density;
        }

        float DawnCloudSampleDensity(float3 rayPosition, float shapeNoiseLod)
        {
            return DawnCloudEvaluatePropertiesAtLod(
                rayPosition,
                shapeNoiseLod).density;
        }

        struct DawnCloudLightResult
        {
            float3 direct;
            float3 multiple;
            float diffuseField;
            float opticalDepth;
        };

        float3 DawnCloudEvaluateLight(float transmittance, float3 lightColor)
        {
            // The legacy color ramp remains available as an artistic tint, but
            // it no longer replaces the physical color of the main light.
            float3 cloudTint = lerp(
                _DawnCloudColorA.rgb,
                1.0,
                saturate(transmittance * _DawnCloudColorOffset1));
            cloudTint = lerp(
                _DawnCloudColorB.rgb,
                cloudTint,
                saturate(pow(transmittance * _DawnCloudColorOffset2, 3.0)));
            return transmittance * lightColor * cloudTint;
        }

        float DawnCloudPowderTransmittance(
            float directTransmittance,
            float cosineAngle)
        {
            float beerTransmittance = saturate(directTransmittance);
            float opacity = 1.0 - beerTransmittance;
            // Restrict powder to forward-lit, optically thin silhouettes. It
            // must not lift opaque cores or turn an entire sun-facing side white.
            float thinShellWeight =
                smoothstep(0.04, 0.25, opacity) *
                (1.0 - smoothstep(0.55, 0.9, opacity));
            float forwardWeight = smoothstep(0.55, 0.95, cosineAngle);
            float powderTransmittance = saturate(
                beerTransmittance +
                opacity * thinShellWeight * 0.35);
            return lerp(
                beerTransmittance,
                powderTransmittance,
                forwardWeight *
                saturate(_DawnCloudPowderEffectIntensity));
        }

        float DawnCloudSunExtinction()
        {
            return max(_DawnCloudExtinctionScale, 0.0001) *
                max(_DawnCloudLightAbsorptionTowardSun, 0.0) *
                saturate(_DawnCloudSelfShadowStrength);
        }

        float DawnCloudViewExtinction()
        {
            return max(_DawnCloudExtinctionScale, 0.0001) *
                max(_DawnCloudLightAbsorptionThroughCloud, 0.05);
        }

        DawnCloudLightResult DawnCloudLightMarch(
            float3 position,
            float localHeight,
            float3 lightDirection,
            float3 lightColor,
            float cosineAngle)
        {
            float distanceInsideBox = DawnCloudRayBoxDistance(
                _DawnCloudBoundsMin.xyz,
                _DawnCloudBoundsMax.xyz,
                position,
                lightDirection).y;
            const int maximumLightStepCount = 16;
            const float maximumLightMarchDistance = 6000.0;
            float lightMarchDistance = min(
                distanceInsideBox,
                maximumLightMarchDistance);
            float opticalDepth = 0.0;
            float diffuseOpticalDepth = 0.0;
            float diffuseKappaOpticalDepth = 0.0;
            float diffuseAbsorptionTransmittance = 1.0;
            float diffuseField = 0.0;
            bool evaluateDiffuseField =
                _DawnCloudDiffuseFieldParameters.x > 0.0 &&
                _DawnCloudDiffuseFieldTransportParameters.x > 0.0 &&
                lightMarchDistance > 0.0001;
            float diffuseSourceConfidence = 0.0;
            if (evaluateDiffuseField)
            {
                float bottomDepthPower =
                    _DawnCloudDiffuseFieldParameters.y;
                float bottomConfidence = 1.0;
                if (bottomDepthPower > 0.0)
                {
                    float columnHeight =
                        DawnCloudEvaluateTopHeightProxy(position.xz);
                    float bottomSoftHeight = max(
                        _DawnCloudHeightProfileParameters.x *
                        lerp(1.0, 4.0, columnHeight),
                        0.001);
                    bottomConfidence = 1.0 - exp(
                        -max(
                            localHeight +
                            _DawnCloudDiffuseFieldParameters.z,
                            0.0) /
                        bottomSoftHeight *
                        bottomDepthPower);
                }
                diffuseSourceConfidence =
                    DawnCloudEvaluateDiffuseBoundaryLight(
                        position,
                        lightDirection) *
                    bottomConfidence;
            }
            [loop]
            for (int stepIndex = 0;
                 stepIndex < maximumLightStepCount;
                 stepIndex++)
            {
                float normalizedStart =
                    (float)stepIndex / maximumLightStepCount;
                float normalizedEnd =
                    (float)(stepIndex + 1) / maximumLightStepCount;
                // Quadratic segments retain fine samples near the shaded point
                // without the 5/10/20/... jumps that skipped thin blockers.
                float segmentStart =
                    lightMarchDistance * normalizedStart * normalizedStart;
                float segmentEnd =
                    lightMarchDistance * normalizedEnd * normalizedEnd;
                float stepSize = segmentEnd - segmentStart;
                if (stepSize <= 0.0001)
                {
                    break;
                }

                float sourceDistance = (segmentStart + segmentEnd) * 0.5;
                float3 samplePosition =
                    position + lightDirection * sourceDistance;
                float lightNoiseLod = clamp(
                    log2(max(stepSize / 5.0, 1.0)),
                    0.0,
                    3.0);
                float density = max(
                    0.0,
                    DawnCloudSampleDensity(
                        samplePosition,
                        lightNoiseLod));
                opticalDepth += density * stepSize;
                if (evaluateDiffuseField)
                {
                    // The reference model fixes sigmaT to one and derives the
                    // diffusion transport directly from normalized density.
                    // Direct-shadow absorption remains a separate control.
                    float extinctionCoefficient = density;
                    float localOpticalDepth =
                        extinctionCoefficient * stepSize;
                    float scatterOpticalDepth =
                        localOpticalDepth *
                        DawnCloudDiffuseFieldAlbedo;
                    float kappaStep =
                        localOpticalDepth *
                        DawnCloudDiffuseFieldKappaOdScale;
                    float propagation = exp(
                        -(diffuseKappaOpticalDepth +
                          kappaStep * 0.5));
                    float multipleScatteringBuild =
                        1.0 - exp(
                            -(diffuseOpticalDepth +
                              localOpticalDepth * 0.5) *
                            _DawnCloudDiffuseFieldTransportParameters.x);
                    float inverseDistance = rcp(max(
                        sourceDistance,
                        stepSize * 0.5));
                    diffuseField +=
                        diffuseAbsorptionTransmittance *
                        scatterOpticalDepth *
                        extinctionCoefficient *
                        diffuseSourceConfidence *
                        multipleScatteringBuild *
                        propagation *
                        inverseDistance;
                    diffuseAbsorptionTransmittance *= exp(
                        -localOpticalDepth *
                        (1.0 - DawnCloudDiffuseFieldAlbedo));
                    diffuseOpticalDepth += localOpticalDepth;
                    diffuseKappaOpticalDepth += kappaStep;
                }
            }

            // Density integrates over travelled world distance; the explicit
            // extinction scale below converts that integral into optical depth.
            // Bounds Size therefore changes path length without silently
            // retuning the medium to a historical 50-unit layer.
            float solarOpticalDepth = opticalDepth;
            float sunExtinction = DawnCloudSunExtinction();
            float directOpticalDepth = solarOpticalDepth;
            float directTransmittance = exp(
                -directOpticalDepth *
                sunExtinction);
            directTransmittance = DawnCloudPowderTransmittance(
                directTransmittance,
                cosineAngle);
            float internalOpticalDepth = solarOpticalDepth;
            float multipleTransmittance = exp(
                -internalOpticalDepth *
                sunExtinction *
                clamp(_DawnCloudMultiScatterParameters.x, 0.05, 1.0));

            DawnCloudLightResult result;
            result.direct = DawnCloudEvaluateLight(
                directTransmittance,
                lightColor);
            result.multiple = DawnCloudEvaluateLight(
                multipleTransmittance,
                lightColor);
            result.diffuseField = diffuseField;
            result.opticalDepth = solarOpticalDepth;
            return result;
        }

        float DawnCloudMapDiffuseField(float diffuseField)
        {
            float scaledDiffuseField =
                max(diffuseField, 0.0) *
                max(_DawnCloudDiffuseFieldParameters.x, 0.0);
            float compression = max(
                _DawnCloudDiffuseFieldTransportParameters.y,
                0.0);
            return compression > 0.0
                ? (1.0 - exp(
                    -scaledDiffuseField * compression)) /
                  compression
                : scaledDiffuseField;
        }

        float3 DawnCloudEnvironmentLight(
            DawnCloudProperties properties,
            float lightOpticalDepth)
        {
            float upperHemisphereBlend = smoothstep(
                0.0,
                1.0,
                properties.height);
            float3 upperHemisphere = lerp(
                _DawnCloudAmbientEquatorColor.rgb,
                _DawnCloudAmbientSkyColor.rgb,
                upperHemisphereBlend);
            // Sun optical depth is only a confidence signal here. Post-erosion
            // density keeps real detail silhouettes open to the sky instead of
            // using the same long tangent ray to black out every ambient direction.
            float directOcclusion = 1.0 - exp(
                -max(lightOpticalDepth, 0.0) *
                DawnCloudSunExtinction());
            float relativeFinalDensity = saturate(
                properties.density /
                max(_DawnCloudDensityMultiplier, 0.0001));
            float densityOcclusion = smoothstep(
                0.1,
                0.8,
                relativeFinalDensity);
            float lowerCloudWeight = 1.0 - properties.height;
            float ambientOcclusion = saturate(
                directOcclusion *
                densityOcclusion *
                lerp(1.0, 1.5, lowerCloudWeight));
            float upwardVisibility = lerp(
                1.0,
                0.1,
                ambientOcclusion *
                saturate(_DawnCloudAmbientOcclusionStrength));
            float groundVisibility = 1.0 - properties.height;
            float precipitationDimming = lerp(
                1.0,
                0.55,
                properties.precipitation * groundVisibility);
            return (
                upperHemisphere * upwardVisibility +
                _DawnCloudAmbientGroundColor.rgb * groundVisibility) *
                precipitationDimming;
        }

        float DawnCloudToDeviceDepth(float rawDepth)
        {
#if UNITY_REVERSED_Z
            return rawDepth;
#else
            return lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
#endif
        }

        float4 FragDepthDownsample(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 texelSize =
                0.5 * max(_DawnCloudDepthDownsampleScale, 1.0) *
                rcp(max(_ScaledScreenParams.xy, 1.0));
            float2 uv = input.texcoord;
            float depthCenter = SampleSceneDepth(uv);
            float depth0 = SampleSceneDepth(uv + texelSize * float2(-1.0, -1.0));
            float depth1 = SampleSceneDepth(uv + texelSize * float2(-1.0, 1.0));
            float depth2 = SampleSceneDepth(uv + texelSize * float2(1.0, -1.0));
            float depth3 = SampleSceneDepth(uv + texelSize * float2(1.0, 1.0));
            float depth4 = SampleSceneDepth(uv + texelSize * float2(-1.0, 0.0));
            float depth5 = SampleSceneDepth(uv + texelSize * float2(1.0, 0.0));
            float depth6 = SampleSceneDepth(uv + texelSize * float2(0.0, -1.0));
            float depth7 = SampleSceneDepth(uv + texelSize * float2(0.0, 1.0));
#if UNITY_REVERSED_Z
            float conservativeDepth = max(
                depthCenter,
                max(
                    max(depth0, max(depth1, max(depth2, depth3))),
                    max(depth4, max(depth5, max(depth6, depth7)))));
#else
            float conservativeDepth = min(
                depthCenter,
                min(
                    min(depth0, min(depth1, min(depth2, depth3))),
                    min(depth4, min(depth5, min(depth6, depth7)))));
#endif
            return float4(
                conservativeDepth,
                conservativeDepth,
                conservativeDepth,
                conservativeDepth);
        }

        struct DawnCloudRayMarchOutput
        {
            float4 visibleCloud : SV_Target0;
            float cloudDistance : SV_Target1;
        };

        DawnCloudRayMarchOutput FragRayMarchCloud(Varyings input)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord;
            float rawDepth = SAMPLE_TEXTURE2D_X_LOD(
                _DawnCloudLowDepthTexture,
                sampler_DawnCloudLowDepthTexture,
                uv,
                0).r;
            float3 cameraPosition = _WorldSpaceCameraPos;
            float3 worldPosition = ComputeWorldSpacePosition(
                uv,
                DawnCloudToDeviceDepth(rawDepth),
                UNITY_MATRIX_I_VP);
            float3 cameraToPixel = worldPosition - cameraPosition;
            float sceneDistance = length(cameraToPixel);
            float3 viewDirection = cameraToPixel / max(sceneDistance, 0.00001);
            float2 boxDistance = DawnCloudRayBoxDistance(
                _DawnCloudBoundsMin.xyz,
                _DawnCloudBoundsMax.xyz,
                cameraPosition,
                viewDirection);
            float distanceToBox = boxDistance.x;
            float distanceInsideBox = boxDistance.y;
            float visibleDistance = clamp(
                sceneDistance - distanceToBox,
                0.0,
                distanceInsideBox);

            float blueNoise = SAMPLE_TEXTURE2D_LOD(
                _DawnCloudBlueNoise,
                sampler_DawnCloudBlueNoise,
                uv * _DawnCloudBlueNoiseScale.xy + _DawnCloudBlueNoiseScale.zw,
                0).r;
            Light mainLight = GetMainLight();
            float3 lightDirection = normalize(mainLight.direction);
            float cosineAngle = dot(viewDirection, lightDirection);
            float phase = DawnCloudPhase(cosineAngle);
            float uniformPhase = 0.25 * rcp(PI);
            float multiplePhase = lerp(
                uniformPhase,
                phase,
                saturate(_DawnCloudMultiScatterParameters.z));
            float baseStepSize = exp(_DawnCloudRayStepExponent) *
                                 max(_DawnCloudRayStepLength, 0.00001);
            float adaptiveStepSize = visibleDistance /
                max((float)_DawnCloudMaxRayMarchSteps, 1.0);
            float stepSize = max(baseStepSize, adaptiveStepSize);
            float distanceTravelled = blueNoise * _DawnCloudRayOffsetStrength;
            float viewAbsorption = DawnCloudViewExtinction();
            float visibleTransmittance = 1.0;
            float3 visibleLightEnergy = 0.0;
            float visibleDistanceWeightedOpacity = 0.0;

            [loop]
            for (int stepIndex = 0; stepIndex < 512; stepIndex++)
            {
                if (stepIndex >= _DawnCloudMaxRayMarchSteps ||
                    distanceTravelled >= visibleDistance)
                {
                    break;
                }

                float sampleStepSize = min(
                    stepSize,
                    visibleDistance - distanceTravelled);
                float3 rayPosition = cameraPosition +
                                     viewDirection * (distanceToBox + distanceTravelled);
                DawnCloudProperties cloudProperties =
                    DawnCloudEvaluateProperties(rayPosition);
                if (cloudProperties.density > 0.0)
                {
                    DawnCloudLightResult lightResult = DawnCloudLightMarch(
                        rayPosition,
                        cloudProperties.localHeight,
                        lightDirection,
                        mainLight.color,
                        cosineAngle);
                    float directPhase = phase;
                    float3 incidentLight =
                        lightResult.direct * directPhase +
                        lightResult.multiple * multiplePhase *
                        saturate(_DawnCloudMultiScatterParameters.y);
                    incidentLight += DawnCloudEnvironmentLight(
                        cloudProperties,
                        lightResult.opticalDepth);
                    float3 diffuseFieldLight =
                        DawnCloudMapDiffuseField(
                            lightResult.diffuseField) *
                        mainLight.color;

                    float visibleStepSize = sampleStepSize;
                    if (visibleStepSize > 0.0 &&
                        visibleTransmittance >= 0.01)
                    {
                        float visibleOpticalDepth =
                            cloudProperties.density * visibleStepSize;
                        float visibleStepTransmittance = exp(
                            -visibleOpticalDepth * viewAbsorption);
                        // Unit-albedo volume integration: scattered energy is
                        // bounded by the energy removed from the view ray.
                        float visibleScatteringWeight =
                            1.0 - visibleStepTransmittance;
                        visibleLightEnergy +=
                            visibleScatteringWeight *
                            visibleTransmittance *
                            incidentLight;
                        visibleLightEnergy +=
                            (1.0 - visibleStepTransmittance) *
                            visibleTransmittance *
                            diffuseFieldLight;
                        float visibleOpacityContribution =
                            (1.0 - visibleStepTransmittance) *
                            visibleTransmittance;
                        float visibleSampleDistance =
                            distanceToBox +
                            distanceTravelled +
                            visibleStepSize * 0.5;
                        visibleDistanceWeightedOpacity +=
                            visibleOpacityContribution *
                            max(visibleSampleDistance, 0.0);
                        visibleTransmittance *=
                            visibleStepTransmittance;
                    }

                    if (visibleTransmittance < 0.01)
                    {
                        break;
                    }
                }

                distanceTravelled += sampleStepSize;
            }

            DawnCloudRayMarchOutput output;
            output.visibleCloud = float4(
                visibleLightEnergy,
                saturate(visibleTransmittance));
            output.cloudDistance =
                visibleDistanceWeightedOpacity /
                max(1.0 - visibleTransmittance, 0.00001);
            return output;
        }

        struct DawnCloudReconstructionOutput
        {
            float4 cloud : SV_Target0;
            float cloudDistance : SV_Target1;
        };

        float3 DawnCloudViewDirection(float2 uv)
        {
#if UNITY_REVERSED_Z
            const float farRawDepth = 0.0;
#else
            const float farRawDepth = 1.0;
#endif
            float3 farWorldPosition = ComputeWorldSpacePosition(
                uv,
                DawnCloudToDeviceDepth(farRawDepth),
                UNITY_MATRIX_I_VP);
            return normalize(farWorldPosition - _WorldSpaceCameraPos);
        }

        DawnCloudReconstructionOutput FragTemporalResolve(Varyings input)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord;
            float4 currentCloud = SAMPLE_TEXTURE2D_X_LOD(
                _DawnCloudTexture,
                sampler_DawnCloudTexture,
                uv,
                0);
            float currentDistance = max(
                SAMPLE_TEXTURE2D_X_LOD(
                    _DawnCloudDistanceTexture,
                    sampler_DawnCloudDistanceTexture,
                    uv,
                    0).r,
                0.0);

            DawnCloudReconstructionOutput output;
            output.cloud = currentCloud;
            output.cloudDistance = currentDistance;
            float currentOpacity = 1.0 - currentCloud.a;
            if (_DawnCloudHistoryValid < 0.5 ||
                currentOpacity <= 0.0025 ||
                currentDistance <= 0.0)
            {
                return output;
            }

            float3 currentWorldPosition =
                _WorldSpaceCameraPos +
                DawnCloudViewDirection(uv) * currentDistance;
            float4 previousClip = mul(
                _DawnCloudPreviousViewProjection,
                float4(currentWorldPosition, 1.0));
            if (previousClip.w <= 0.0001)
            {
                return output;
            }

            float2 previousUv =
                previousClip.xy / previousClip.w * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
            previousUv.y = 1.0 - previousUv.y;
#endif
            if (any(previousUv <= 0.0) || any(previousUv >= 1.0))
            {
                return output;
            }

            float4 historyCloud = SAMPLE_TEXTURE2D_X_LOD(
                _DawnCloudHistoryTexture,
                sampler_DawnCloudHistoryTexture,
                previousUv,
                0);
            float historyDistance = max(
                SAMPLE_TEXTURE2D_X_LOD(
                    _DawnCloudHistoryDistanceTexture,
                    sampler_DawnCloudHistoryDistanceTexture,
                    previousUv,
                    0).r,
                0.0);
            float expectedHistoryDistance = distance(
                currentWorldPosition,
                _DawnCloudPreviousCameraPosition.xyz);
            float distanceTolerance = max(
                20.0,
                expectedHistoryDistance * 0.06);
            float distanceConfidence = 1.0 - smoothstep(
                distanceTolerance,
                distanceTolerance * 2.0,
                abs(historyDistance - expectedHistoryDistance));
            float opacityDifference = abs(
                (1.0 - historyCloud.a) - currentOpacity);
            float opacityConfidence = 1.0 - smoothstep(
                0.12,
                0.35,
                opacityDifference);

            float4 neighborhoodMinimum = currentCloud;
            float4 neighborhoodMaximum = currentCloud;
            [unroll]
            for (int y = -1; y <= 1; y++)
            {
                [unroll]
                for (int x = -1; x <= 1; x++)
                {
                    float4 neighbor = SAMPLE_TEXTURE2D_X_LOD(
                        _DawnCloudTexture,
                        sampler_DawnCloudTexture,
                        uv + float2(x, y) * _DawnCloudBufferSize.zw,
                        0);
                    neighborhoodMinimum = min(
                        neighborhoodMinimum,
                        neighbor);
                    neighborhoodMaximum = max(
                        neighborhoodMaximum,
                        neighbor);
                }
            }
            historyCloud = clamp(
                historyCloud,
                neighborhoodMinimum,
                neighborhoodMaximum);
            float historyWeight =
                saturate(_DawnCloudTemporalBlend) *
                distanceConfidence * opacityConfidence *
                smoothstep(0.0025, 0.05, currentOpacity);
            output.cloud = lerp(
                currentCloud,
                historyCloud,
                historyWeight);
            return output;
        }

        bool DawnCloudIsSkyDepth(float rawDepth)
        {
#if UNITY_REVERSED_Z
            return rawDepth <= 0.0001;
#else
            return rawDepth >= 0.9999;
#endif
        }

        DawnCloudReconstructionOutput DawnCloudDepthAwareUpsample(float2 uv)
        {
            float fullRawDepth = SampleSceneDepth(uv);
            bool fullIsSky = DawnCloudIsSkyDepth(fullRawDepth);
            float fullLinearDepth = LinearEyeDepth(
                fullRawDepth,
                _ZBufferParams);
            float3 fullWorldPosition = ComputeWorldSpacePosition(
                uv,
                DawnCloudToDeviceDepth(fullRawDepth),
                UNITY_MATRIX_I_VP);
            float fullSceneDistance = distance(
                fullWorldPosition,
                _WorldSpaceCameraPos);

            float2 lowPixel = uv * _DawnCloudBufferSize.xy - 0.5;
            float2 basePixel = floor(lowPixel);
            float2 fraction = frac(lowPixel);
            float4 cloudSum = 0.0;
            float weightSum = 0.0;
            float distanceSum = 0.0;
            float distanceWeightSum = 0.0;

            [unroll]
            for (int sampleIndex = 0; sampleIndex < 4; sampleIndex++)
            {
                float2 offset = float2(
                    sampleIndex & 1,
                    sampleIndex >> 1);
                float2 sampleUv =
                    (basePixel + offset + 0.5) *
                    _DawnCloudBufferSize.zw;
                float2 axisWeight = 1.0 - abs(offset - fraction);
                float spatialWeight = axisWeight.x * axisWeight.y;
                float4 sampleCloud = SAMPLE_TEXTURE2D_X_LOD(
                    _DawnCloudTexture,
                    sampler_DawnCloudTexture,
                    sampleUv,
                    0);
                float sampleDistance = max(
                    SAMPLE_TEXTURE2D_X_LOD(
                        _DawnCloudDistanceTexture,
                        sampler_DawnCloudDistanceTexture,
                        sampleUv,
                        0).r,
                    0.0);
                float sampleRawDepth = SAMPLE_TEXTURE2D_X_LOD(
                    _DawnCloudLowDepthTexture,
                    sampler_DawnCloudLowDepthTexture,
                    sampleUv,
                    0).r;
                bool sampleIsSky = DawnCloudIsSkyDepth(sampleRawDepth);
                float sampleLinearDepth = LinearEyeDepth(
                    sampleRawDepth,
                    _ZBufferParams);
                float depthTolerance = max(
                    1.0,
                    fullLinearDepth * 0.02);
                float depthWeight = fullIsSky == sampleIsSky
                    ? exp2(
                        -abs(sampleLinearDepth - fullLinearDepth) /
                        depthTolerance)
                    : 0.0;
                float sampleOpacity = 1.0 - sampleCloud.a;
                float cloudInFront = sampleDistance > 0.0
                    ? 1.0 - smoothstep(
                        fullSceneDistance + depthTolerance,
                        fullSceneDistance + depthTolerance * 2.0,
                        sampleDistance)
                    : 0.0;
                // A sky neighbor is still valid over an opaque full-res pixel
                // when its cloud sample is genuinely in front of that surface.
                depthWeight = max(
                    depthWeight,
                    cloudInFront * smoothstep(0.001, 0.02, sampleOpacity));
                float sampleWeight = spatialWeight * depthWeight;
                cloudSum += sampleCloud * sampleWeight;
                weightSum += sampleWeight;
                float opacityWeight =
                    sampleWeight * max(sampleOpacity, 0.0);
                distanceSum += sampleDistance * opacityWeight;
                distanceWeightSum += opacityWeight;
            }

            DawnCloudReconstructionOutput output;
            if (weightSum > 0.0001)
            {
                output.cloud = cloudSum / weightSum;
                output.cloudDistance = distanceSum /
                    max(distanceWeightSum, 0.0001);
            }
            else
            {
                output.cloud = float4(0.0, 0.0, 0.0, 1.0);
                output.cloudDistance = 0.0;
            }
            return output;
        }

        DawnCloudReconstructionOutput FragDepthAwareUpsample(Varyings input)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return DawnCloudDepthAwareUpsample(input.texcoord);
        }

        float4 FragComposite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SAMPLE_TEXTURE2D_X_LOD(
                _DawnCloudTexture,
                sampler_DawnCloudTexture,
                input.texcoord,
                0);
        }

        float FragCloudShadow(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord;
            float3 lightDirection = normalize(
                _DawnCloudShadowLightDirection.xyz);
            float3 rayOrigin =
                _DawnCloudShadowRayOrigin.xyz +
                _DawnCloudShadowRight.xyz * uv.x +
                _DawnCloudShadowUp.xyz * uv.y;
            float2 boxDistance = DawnCloudRayBoxDistance(
                _DawnCloudBoundsMin.xyz,
                _DawnCloudBoundsMax.xyz,
                rayOrigin,
                lightDirection);
            float distanceInsideBox = boxDistance.y;
            if (distanceInsideBox <= 0.0001)
            {
                return 1.0;
            }

            const int shadowStepCount = 48;
            float stepLength = distanceInsideBox / shadowStepCount;
            float3 samplePosition = rayOrigin + lightDirection *
                (boxDistance.x + stepLength * 0.5);
            float opticalDepth = 0.0;
            float shadowNoiseLod = clamp(
                log2(max(stepLength / 5.0, 1.0)),
                0.0,
                3.0);

            [loop]
            for (int stepIndex = 0;
                 stepIndex < shadowStepCount;
                 stepIndex++)
            {
                opticalDepth +=
                    DawnCloudSampleDensity(
                        samplePosition,
                        shadowNoiseLod) * stepLength;
                samplePosition += lightDirection * stepLength;
            }

            return saturate(exp(
                -opticalDepth * DawnCloudSunExtinction()));
        }
        ENDHLSL

        Pass
        {
            Name "Dawn TOD Cloud Depth Downsample"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepthDownsample
            ENDHLSL
        }

        Pass
        {
            Name "Dawn TOD Volumetric Cloud"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragRayMarchCloud
            ENDHLSL
        }

        Pass
        {
            Name "Dawn TOD Volumetric Cloud Composite"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One SrcAlpha
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }

        Pass
        {
            Name "Dawn TOD World Cloud Shadow"
            ZTest Always
            ZWrite Off
            Cull Off
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCloudShadow
            ENDHLSL
        }

        Pass
        {
            Name "Dawn TOD Cloud Temporal Resolve"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTemporalResolve
            ENDHLSL
        }

        Pass
        {
            Name "Dawn TOD Cloud Depth-Aware Upsample"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepthAwareUpsample
            ENDHLSL
        }
    }

    Fallback Off
}
