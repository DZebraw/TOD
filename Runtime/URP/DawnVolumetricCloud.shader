Shader "Hidden/DawnTOD/VolumetricCloud"
{
    SubShader
    {
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
        float4 _DawnCloudAmbientSkyColor;
        float4 _DawnCloudAmbientGroundColor;
        float4 _DawnCloudHeightProfileParameters;
        float4 _DawnCloudSpeedWarp;
        float4 _DawnCloudBlueNoiseScale;

        float _DawnCloudCoverage;
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
        float _DawnCloudPhaseMinimum;
        float _DawnCloudLightAbsorptionTowardSun;
        float _DawnCloudLightAbsorptionThroughCloud;
        int _DawnCloudMaxRayMarchSteps;

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
            return max(saturate(_DawnCloudPhaseMinimum), directionalPhase);
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

        float DawnCloudSampleDensity(float3 rayPosition)
        {
            float3 boundsMinimum = _DawnCloudBoundsMin.xyz;
            float3 boundsMaximum = _DawnCloudBoundsMax.xyz;
            float3 boundsSize = max(boundsMaximum - boundsMinimum, 0.01);
            float3 boundsCenter = (boundsMinimum + boundsMaximum) * 0.5;

            float shapeSpeed = _Time.y * _DawnCloudSpeedWarp.x;
            float detailSpeed = _Time.y * _DawnCloudSpeedWarp.y;
            float3 shapeUv = rayPosition * _DawnCloudShapeTiling +
                             float3(shapeSpeed, shapeSpeed * 0.2, 0.0);
            float3 detailUv = rayPosition * _DawnCloudDetailTiling +
                              float3(detailSpeed, detailSpeed * 0.2, 0.0);
            float2 weatherUv =
                (boundsSize.xz * 0.5 + (rayPosition.xz - boundsCenter.xz)) /
                max(boundsSize.x, boundsSize.z);

            float maskValue = SAMPLE_TEXTURE2D_LOD(
                _DawnCloudMaskNoise,
                sampler_DawnCloudMaskNoise,
                weatherUv + float2(shapeSpeed * 0.5, 0.0),
                0).r;
            float weatherValue = SAMPLE_TEXTURE2D_LOD(
                _DawnCloudWeatherMap,
                sampler_DawnCloudWeatherMap,
                weatherUv + float2(shapeSpeed * 0.4, 0.0),
                0).r;
            float4 shapeNoise = SAMPLE_TEXTURE3D_LOD(
                _DawnCloudShapeNoise,
                sampler_DawnCloudShapeNoise,
                shapeUv + maskValue * _DawnCloudSpeedWarp.z * 0.1,
                0);
            float4 detailNoise = SAMPLE_TEXTURE3D_LOD(
                _DawnCloudDetailNoise,
                sampler_DawnCloudDetailNoise,
                detailUv + shapeNoise.r * _DawnCloudSpeedWarp.w * 0.1,
                0);

            float edgeDistanceX = min(
                10.0,
                min(rayPosition.x - boundsMinimum.x, boundsMaximum.x - rayPosition.x));
            float edgeDistanceZ = min(
                10.0,
                min(rayPosition.z - boundsMinimum.z, boundsMaximum.z - rayPosition.z));
            float edgeWeight = saturate(min(edgeDistanceX, edgeDistanceZ) / 10.0);

            float gradientMinimum = DawnCloudRemap(weatherValue, 0.0, 1.0, 0.1, 0.6);
            float gradientMaximum = DawnCloudRemap(
                weatherValue,
                0.0,
                1.0,
                gradientMinimum,
                0.9);
            float heightPercent =
                (rayPosition.y - boundsMinimum.y) / max(boundsSize.y, 0.01);
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
            float growthPattern = smoothstep(0.15, 0.85, weatherValue);
            float localTopHeight = lerp(
                bodyHeight,
                1.0,
                growthPattern * verticalGrowth);
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
                maskValue);
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
                (shapeFbm + _DawnCloudDensityOffset * 0.01) * coverageMask;
            if (baseShapeDensity <= 0.0)
            {
                return 0.0;
            }

            float detailFbm = pow(max(detailNoise.r, 0.00001), _DawnCloudDetailWeights);
            float inverseShapeDensity = 1.0 - baseShapeDensity;
            float detailErodeWeight = inverseShapeDensity * inverseShapeDensity *
                                      inverseShapeDensity;
            float cloudDensity = baseShapeDensity - detailFbm * detailErodeWeight *
                                 _DawnCloudDetailNoiseWeight;
            return saturate(cloudDensity * _DawnCloudDensityMultiplier);
        }

        struct DawnCloudLightResult
        {
            float3 direct;
            float3 multiple;
        };

        float3 DawnCloudEvaluateLight(float transmittance, float3 lightColor)
        {
            float3 cloudColor = lerp(
                _DawnCloudColorA.rgb,
                lightColor,
                saturate(transmittance * _DawnCloudColorOffset1));
            cloudColor = lerp(
                _DawnCloudColorB.rgb,
                cloudColor,
                saturate(pow(transmittance * _DawnCloudColorOffset2, 3.0)));
            return transmittance * cloudColor;
        }

        DawnCloudLightResult DawnCloudLightMarch(
            float3 position,
            float3 lightDirection,
            float3 lightColor)
        {
            float distanceInsideBox = DawnCloudRayBoxDistance(
                _DawnCloudBoundsMin.xyz,
                _DawnCloudBoundsMax.xyz,
                position,
                lightDirection).y;
            float cloudLayerHeight = max(
                _DawnCloudBoundsMax.y - _DawnCloudBoundsMin.y,
                0.0001);
            float horizontalDirectionLength = length(lightDirection.xz);
            float maximumNoiseTiling = max(
                _DawnCloudShapeTiling,
                _DawnCloudDetailTiling);
            // Keep shadow samples dense enough to resolve both horizontal 3D
            // noise and the vertical density profile as the sun angle changes.
            float horizontalStepLength = 0.5 / max(
                maximumNoiseTiling * horizontalDirectionLength,
                0.0001);
            float verticalStepLength = cloudLayerHeight * 0.25 / max(
                abs(lightDirection.y),
                0.0001);
            float targetStepLength = max(
                min(horizontalStepLength, verticalStepLength),
                0.0001);
            const int minimumLightStepCount = 4;
            const int maximumLightStepCount = 16;
            int lightStepCount = clamp(
                (int)ceil(distanceInsideBox / targetStepLength),
                minimumLightStepCount,
                maximumLightStepCount);
            float lightStepSize = distanceInsideBox / lightStepCount;
            float opticalDepth = 0.0;
            // Midpoints retain nearby volume detail and avoid spending the last
            // sample on the zero-density cloud-box boundary.
            position += lightDirection * (lightStepSize * 0.5);

            [loop]
            for (int stepIndex = 0;
                 stepIndex < maximumLightStepCount;
                 stepIndex++)
            {
                if (stepIndex >= lightStepCount)
                {
                    break;
                }

                float density = max(0.0, DawnCloudSampleDensity(position));
                opticalDepth += density * lightStepSize;
                position += lightDirection * lightStepSize;
            }

            float directTransmittance = exp(
                -opticalDepth * _DawnCloudLightAbsorptionTowardSun);
            float multipleTransmittance = exp(
                -opticalDepth * _DawnCloudLightAbsorptionTowardSun *
                clamp(_DawnCloudMultiScatterParameters.x, 0.05, 1.0));

            DawnCloudLightResult result;
            result.direct = DawnCloudEvaluateLight(
                directTransmittance,
                lightColor);
            result.multiple = DawnCloudEvaluateLight(
                multipleTransmittance,
                lightColor);
            return result;
        }

        float3 DawnCloudEnvironmentLight(float3 position)
        {
            float cloudHeight = max(
                _DawnCloudBoundsMax.y - _DawnCloudBoundsMin.y,
                0.0001);
            float heightPercent = saturate(
                (position.y - _DawnCloudBoundsMin.y) / cloudHeight);
            float verticalBlend = smoothstep(0.0, 1.0, heightPercent);
            float skyVisibility = lerp(0.35, 1.0, verticalBlend);
            float groundVisibility = 1.0 - verticalBlend;
            return _DawnCloudAmbientSkyColor.rgb * skyVisibility +
                   _DawnCloudAmbientGroundColor.rgb * groundVisibility;
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

            float2 texelSize = 0.5 * rcp(max(_ScaledScreenParams.xy, 1.0));
            float2 uv = input.texcoord;
            float depth0 = SampleSceneDepth(uv + texelSize * float2(-1.0, -1.0));
            float depth1 = SampleSceneDepth(uv + texelSize * float2(-1.0, 1.0));
            float depth2 = SampleSceneDepth(uv + texelSize * float2(1.0, -1.0));
            float depth3 = SampleSceneDepth(uv + texelSize * float2(1.0, 1.0));
#if UNITY_REVERSED_Z
            float conservativeDepth = max(depth0, max(depth1, max(depth2, depth3)));
#else
            float conservativeDepth = min(depth0, min(depth1, min(depth2, depth3)));
#endif
            return float4(
                conservativeDepth,
                conservativeDepth,
                conservativeDepth,
                conservativeDepth);
        }

        float4 FragRayMarchCloud(Varyings input) : SV_Target
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
            float maxDistance = min(sceneDistance - distanceToBox, distanceInsideBox);

            float blueNoise = SAMPLE_TEXTURE2D_LOD(
                _DawnCloudBlueNoise,
                sampler_DawnCloudBlueNoise,
                uv * _DawnCloudBlueNoiseScale.xy + _DawnCloudBlueNoiseScale.zw,
                0).r;
            Light mainLight = GetMainLight();
            float3 lightDirection = normalize(mainLight.direction);
            float phase = DawnCloudPhase(dot(viewDirection, lightDirection));
            float uniformPhase = 0.25 * rcp(PI);
            float multiplePhase = lerp(
                uniformPhase,
                phase,
                saturate(_DawnCloudMultiScatterParameters.z));
            float stepSize = exp(_DawnCloudRayStepExponent) *
                             max(_DawnCloudRayStepLength, 0.00001);
            float distanceTravelled = blueNoise * _DawnCloudRayOffsetStrength;
            float transmittance = 1.0;
            float3 lightEnergy = 0.0;

            [loop]
            for (int stepIndex = 0; stepIndex < 512; stepIndex++)
            {
                if (stepIndex >= _DawnCloudMaxRayMarchSteps ||
                    distanceTravelled >= maxDistance)
                {
                    break;
                }

                float3 rayPosition = cameraPosition +
                                     viewDirection * (distanceToBox + distanceTravelled);
                float density = DawnCloudSampleDensity(rayPosition);
                if (density > 0.0)
                {
                    DawnCloudLightResult lightResult = DawnCloudLightMarch(
                        rayPosition,
                        lightDirection,
                        mainLight.color);
                    float3 incidentLight = lightResult.direct * phase +
                        lightResult.multiple * multiplePhase *
                        saturate(_DawnCloudMultiScatterParameters.y);
                    incidentLight += DawnCloudEnvironmentLight(rayPosition);
                    lightEnergy += density * stepSize * transmittance *
                                   incidentLight;
                    transmittance *= exp(
                        -density * stepSize * _DawnCloudLightAbsorptionThroughCloud);
                    if (transmittance < 0.01)
                    {
                        break;
                    }
                }

                distanceTravelled += stepSize;
            }

            return float4(lightEnergy, saturate(transmittance));
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

            const int shadowStepCount = 32;
            float stepLength = distanceInsideBox / shadowStepCount;
            float3 samplePosition = rayOrigin + lightDirection *
                (boxDistance.x + stepLength * 0.5);
            float opticalDepth = 0.0;

            [loop]
            for (int stepIndex = 0;
                 stepIndex < shadowStepCount;
                 stepIndex++)
            {
                opticalDepth +=
                    DawnCloudSampleDensity(samplePosition) * stepLength;
                samplePosition += lightDirection * stepLength;
            }

            return saturate(exp(
                -opticalDepth * _DawnCloudLightAbsorptionTowardSun));
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
    }

    Fallback Off
}
