Shader "Hidden/NubisStorm/Volume"
{
    Properties
    {
        [HideInInspector] _CloudWeather("NDF", 2D) = "black" {}
        [HideInInspector] _CloudBottomType("Bottom type", 2D) = "black" {}
        [HideInInspector] _CloudWeatherTo("Destination NDF", 2D) = "black" {}
        [HideInInspector] _CloudBottomTypeTo("Destination bottom type", 2D) = "black" {}
        [HideInInspector] _CloudTopProfile("Top profile", 2D) = "black" {}
        [HideInInspector] _CloudBottomProfile("Bottom profile", 2D) = "black" {}
        [HideInInspector] _StormNoise("Shared noise", 3D) = "white" {}
        [HideInInspector] _AuthoredFields("Authored NDF", 2D) = "black" {}
        [HideInInspector] _AuthoredBottomMask("Authored bottom and mask", 2D) = "black" {}
        [HideInInspector] _StormFields("Storm NDF", 2D) = "black" {}
        [HideInInspector] _StormBottomMask("Storm bottom and mask", 2D) = "black" {}
        [HideInInspector] _CirrusWeather("Cirrus NDF", 2D) = "black" {}
        [HideInInspector] _CirrusWeatherTo("Destination cirrus NDF", 2D) = "black" {}
        [HideInInspector] _CirrusShapes("Cirrus shapes", 2D) = "black" {}
        [HideInInspector] _CirrusAuthored("Cirrus influence", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "NubisCloudDensity.hlsl"
        #include "NubisCloudCirrus.hlsl"
        #include "NubisCloudLighting.hlsl"
        #include "../../DawnExponentialHeightFog.hlsl"
        float4x4 _StormInvVP, _CloudPreviousVP;
        float3 _StormCameraPosition, _StormCameraForward, _CloudPreviousCameraPosition;
        float4 _StormQuality, _StormEmission;
        float3 _StormSunDirection, _StormSunColor, _StormMoonDirection, _StormMoonColor, _StormAmbient, _StormCoreColor;
        float _StormOrthographic;
        float _CirrusPixelSize;
        float4 _CloudTargetSize, _CloudWorkSize, _CloudPhase, _CloudTemporal, _CloudFootprint;
        TEXTURE2D(_CloudCurrent);
        TEXTURE2D(_CloudCurrentMeta);
        TEXTURE2D(_CloudHistory);
        TEXTURE2D(_CloudHistoryMeta);
        TEXTURE2D(_CloudResolved);
        TEXTURE2D(_CloudResolvedMeta);
        TEXTURE2D(_CloudDiagnostic);

        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        struct CloudOutput
        {
            float4 color : SV_Target0; // Premultiplied radiance RGB, transmittance A.
            float4 meta : SV_Target1;  // Representative ray distance, scene distance, confidence, stable flash mask.
            float4 debug : SV_Target2;
        };
        Varyings Vert(uint id : SV_VertexID)
        {
            Varyings o;
            o.positionCS = GetFullScreenTriangleVertexPosition(id);
            o.uv = GetFullScreenTriangleTexCoord(id);
            return o;
        }
        void CloudRay(float2 uv, out float3 origin, out float3 ray)
        {
            #if UNITY_REVERSED_Z
                float farDepth=0.000001, nearDepth=1;
            #else
                float farDepth=0.999999, nearDepth=UNITY_NEAR_CLIP_VALUE;
            #endif
            float3 farPoint=ComputeWorldSpacePosition(uv,farDepth,_StormInvVP);
            origin=_StormCameraPosition;
            ray=normalize(farPoint-origin);
            if (_StormOrthographic>0.5)
            {
                origin=ComputeWorldSpacePosition(uv,nearDepth,_StormInvVP);
                ray=_StormCameraForward;
            }
        }
        float CloudSceneDistance(float2 uv, float3 origin)
        {
            float depth=SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                if (depth<=0.0000001) return _CloudSphere.y;
            #else
                if (depth>=0.9999999) return _CloudSphere.y;
                depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
            #endif
            float3 world=ComputeWorldSpacePosition(uv,depth,_StormInvVP);
            return min(length(world-origin),_CloudSphere.y);
        }
        float SunOpticalDepth(float3 p, float footprint, float3 lightDirection)
        {
            float optical=0, previous=0;
            [loop] for(int i=0;i<(int)_StormQuality.y;i++)
            {
                float fraction=(i+1)/_StormQuality.y;
                float next=_CloudLightingStrength.w*fraction*fraction;
                CloudSample lightSample=SampleCloud(p+lightDirection*((previous+next)*0.5),footprint);
                optical+=lightSample.density*lerp(1,_CloudStormLighting.x,lightSample.stormMask)*(next-previous);
                previous=next;
            }
            return optical*0.008*_StormQuality.z;
        }
        void CloudDirectionalLighting(float3 p, float3 ray, CloudSample sample, float footprint,
            float3 direction, float3 radiance, out float3 direct, out float3 multiple)
        {
            direct=multiple=0;
            if (max(radiance.r,max(radiance.g,radiance.b))<=0) return;
            float3 incident=CloudAtmosphereLight(p,direction,radiance);
            if (max(incident.r,max(incident.g,incident.b))<1e-7) return;
            float optical=SunOpticalDepth(p,footprint,direction);
            float mu=dot(ray,direction);
            float transmittance=exp(-optical);
            direct=incident*transmittance*CloudPrimaryPhase(mu)*_CloudLightingStrength.x;
            multiple=incident*CloudMultipleProbability(sample.dimensionalProfile,sample.coverage,sample.topType,
                transmittance,sample.height)*CloudHG(mu,_CloudPhaseParameters.w)*_CloudLightingStrength.y;
        }
        float3 Lighting(float3 p, float3 ray, CloudSample sample, float footprint, out float flashMask)
        {
            float3 directSun,multipleSun,directMoon,multipleMoon;
            CloudDirectionalLighting(p,ray,sample,footprint,_StormSunDirection,_StormSunColor,directSun,multipleSun);
            CloudDirectionalLighting(p,ray,sample,footprint,_StormMoonDirection,_StormMoonColor,directMoon,multipleMoon);
            float3 direct=directSun+directMoon, multiple=multipleSun+multipleMoon;
            float3 ambient=CloudSkyAmbient(p,_StormAmbient)*CloudAmbientProbability(sample.dimensionalProfile)*_CloudLightingStrength.z;
            ambient*=1-_CloudStormLighting.y*sample.stormMask*(1-sample.height);
            float3 q=(CloudModelPosition(p)-_StormCenter)/float3(_StormSize.x,_StormSize.y,_StormSize.x);
            float3 toCore=(q-float3(0,0.46,0))*float3(4,2.5,4);
            float glow=exp(-dot(toCore,toCore)*1.8);
            float3 internalLight=_StormCoreColor*glow*_StormEmission.x*sample.stormMask;
            float3 flashP=(q-float3(0.35,0.32,-0.12))*float3(5,3,5);
            flashMask=exp(-dot(flashP,flashP))*sample.stormMask;
            int debug=(int)_CloudLightingDebug;
            if(debug==1) return direct;
            if(debug==2) return CloudPrimaryPhase(dot(ray,_StormSunDirection)).xxx;
            if(debug==3) return multiple;
            if(debug==4) return ambient;
            if(debug==5) return internalLight;
            return direct+multiple+ambient+internalLight;
        }
        float3 Diagnostic(CloudSample sample)
        {
            int mode=(int)_StormInfluence.z;
            if (mode==1) return sample.dimensionalProfile.xxx;
            if (mode==2) return sample.density.xxx;
            if (mode==3) return sample.coverage.xxx;
            if (mode==4) return float3(sample.topType,1-sample.topType,0.15);
            if (mode==5) return float3(0.1,sample.bottomType,1-sample.bottomType);
            if (mode==6) return float3((sample.heights-_CloudField.w)/_StormSize.y,0);
            if (mode==7) return float3(sample.stormMask,0.1,1-sample.stormMask);
            if (mode==9) return sample.authoredMask.xxx;
            return sample.noiseComposite.xxx;
        }
        float3 TemporalDiagnostic(float2 uv, float distance, float2 historyUV, bool fresh, bool rejected)
        {
            int mode=(int)_CloudTemporal.z;
            if (mode==1) return fresh ? float3(1,1,1) : float3(0,0,0);
            if (mode==2) return saturate(log2(1+distance)/18).xxx;
            if (mode==3) return float3((historyUV-uv)*20+0.5,0.5);
            if (mode==4) return float3(saturate(historyUV),0);
            if (mode==5) return rejected ? float3(1,0.05,0.02) : float3(0.02,0.65,0.08);
            return 0;
        }
        float StepIntegral(float start, float end)
        {
            const float slope=60.0/16384;
            float cap=max(3,_CloudSphere.z), split=(cap-3)/slope;
            float curvedEnd=max(start,min(end,split));
            return log((3+slope*curvedEnd)/(3+slope*start))/slope + max(0,end-max(start,split))/cap;
        }
        float StepDistance(float start, float parameter)
        {
            const float slope=60.0/16384;
            float cap=max(3,_CloudSphere.z), split=(cap-3)/slope;
            float curved=max(0,log(cap/(3+slope*start))/slope);
            float t=((3+slope*start)*exp(slope*min(curved,parameter))-3)/slope;
            return t+max(0,parameter-curved)*cap;
        }
        CloudOutput IntegrateCloud(float2 uv)
        {
            CloudOutput output=(CloudOutput)0;
            output.color.a=1;
            float3 origin,ray;
            CloudRay(uv,origin,ray);
            float sceneDistance=CloudSceneDistance(uv,origin);
            output.meta=float4(0,sceneDistance,1,0);
            float4 intervals=CloudRayIntervals(origin,ray,sceneDistance);
            float firstLength=max(0,intervals.y-intervals.x);
            float total=firstLength+max(0,intervals.w-intervals.z);
            if (total<=0)
            {
                if(_CloudLightingDebug>0) output.color=float4((_CloudLightingDebug==6 ? 1.0 : 0.0).xxx,0);
                return output;
            }
            uint2 pixel=(uint2)(uv*_CloudTargetSize.xy);
            uint seed=pixel.x*1973u+pixel.y*9277u+89173u;
            seed=(seed^(seed>>16u))*2246822519u;
            seed=(seed^(seed>>13u))*3266489917u;
            // Keep a ray's integration lattice stable between sparse updates. A frame-varying
            // offset moves distant samples by hundreds of metres and makes thin clouds pop.
            float jitter=(seed&65535u)/65536.0;
            float firstSteps=StepIntegral(intervals.x,intervals.y);
            float secondSteps=StepIntegral(intervals.z,intervals.w);
            int count=min((int)_StormQuality.x,max(1,(int)ceil(firstSteps+secondSteps)));
            if (firstSteps>0 && secondSteps>0) count=max(2,count);
            int firstCount=firstLength>0 ? max(1,(int)round(count*firstSteps/max(1e-5,firstSteps+secondSteps))) : 0;
            if (secondSteps>0) firstCount=min(firstCount,count-1);
            float transmittance=1, distanceSum=0, flash=0;
            float3 scattering=0;
            [loop] for(int i=0;i<count;i++)
            {
                bool second=i>=firstCount;
                int segmentCount=second ? count-firstCount : firstCount;
                int index=second ? i-firstCount : i;
                float start=second ? intervals.z : intervals.x;
                float end=second ? intervals.w : intervals.y;
                float parameter=(second ? secondSteps : firstSteps)/max(1,segmentCount);
                // Integrate the slide-39 step law analytically, then distribute the finite budget.
                // Near samples stay short even when the shell extends hundreds of kilometres.
                float t=StepDistance(start,index*parameter);
                float next=index==segmentCount-1 ? end : min(end,StepDistance(start,(index+1)*parameter));
                float stepSize=max(0,next-t);
                float sampleDistance=t+stepSize*jitter;
                float3 p=origin+ray*sampleDistance;
                float footprint=max(_CloudFootprint.x*(_StormOrthographic>0.5 ? 1 : sampleDistance),stepSize*0.15);
                CloudSample sample=SampleCloud(p,footprint);
                float d=_StormInfluence.z==1 ? sample.dimensionalProfile*_StormSize.z : sample.density;
                if (d>0.0001)
                {
                    float alpha=1-exp(-d*stepSize*0.008*_StormQuality.z);
                    float weight=transmittance*alpha;
                    float flashMask=0;
                    float3 light=_StormInfluence.z>0 ? Diagnostic(sample) : Lighting(p,ray,sample,footprint,flashMask);
                    scattering+=weight*light;
                    distanceSum+=weight*sampleDistance;
                    flash+=weight*flashMask;
                    transmittance*=1-alpha;
                    if (transmittance<0.006) break;
                }
            }
            output.color=float4(scattering,transmittance);
            output.meta.x=transmittance<0.9999 ? distanceSum/max(1e-5,1-transmittance) : 0;
            output.meta.w=flash;
            if(_CloudLightingDebug>0) output.color.a=0;
            if(_CloudLightingDebug==6) output.color=float4(transmittance.xxx,0);
            if(_CloudLightingDebug==7) output.color=float4(flash.xxx,0);
            return output;
        }
        CloudOutput Raymarch(Varyings input)
        {
            float2 fullPixel=floor(input.positionCS.xy);
            if (_CloudPhase.w>1) fullPixel=fullPixel*4+_CloudPhase.xy;
            fullPixel=min(fullPixel,_CloudTargetSize.xy-1);
            float2 uv=(fullPixel+0.5)*_CloudTargetSize.zw;
            CloudOutput output=IntegrateCloud(uv);
            output.debug=float4(TemporalDiagnostic(uv,output.meta.x,uv,true,true),1);
            return output;
        }
        #include "NubisCloudTemporal.hlsl"

        float3 CirrusDirectionalLighting(float3 p,float3 ray,float3 normal,float footprint,float3 direction,float3 radiance)
        {
            if(max(radiance.r,max(radiance.g,radiance.b))<=0) return 0;
            float3 incident=CloudAtmosphereLight(p,direction,radiance);
            if(max(incident.r,max(incident.g,incident.b))<1e-7) return 0;
            float optical=0;
            [unroll] for(int k=0;k<4;k++)
            {
                float t=(k+.5)*_CirrusLayer.w/(4*max(.12,abs(dot(normal,direction))));
                float3 lightP=p+direction*t;
                optical+=CirrusDensity(CloudModelPosition(lightP).xz,footprint)*_CirrusLayer.z/(4*max(.12,abs(dot(normal,direction))));
            }
            float mu=dot(ray,direction);
            return incident*(CloudPrimaryPhase(mu)*exp(-optical)*_CloudLightingStrength.x
                +.18*exp(-optical*_CloudMultipleScattering.y)*CloudHG(mu,_CloudPhaseParameters.w)*_CloudLightingStrength.y);
        }
        float4 ShadeCirrus(float3 origin,float3 ray,float distance)
        {
            if(distance<=0) return float4(0,0,0,1);
            float3 p=origin+ray*distance;
            float3 normal=_CloudSphere.x>.5 ? normalize(p-_CloudPlanet.xyz) : float3(0,1,0);
            float incidence=max(.08,abs(dot(normal,ray)));
            float footprint=_CirrusPixelSize*(_StormOrthographic>.5 ? 1 : distance)/incidence;
            float2 model=CloudModelPosition(p).xz;
            float d=CirrusDensity(model,footprint);
            float alpha=1-exp(-d*_CirrusLayer.z/incidence);
            float3 light=CirrusDirectionalLighting(p,ray,normal,footprint,_StormSunDirection,_StormSunColor)
                +CirrusDirectionalLighting(p,ray,normal,footprint,_StormMoonDirection,_StormMoonColor)
                +CloudSkyAmbient(p,_StormAmbient)*(.8+.2*(1-d))*_CloudLightingStrength.z;
            return float4(light*alpha,1-alpha);
        }
        float4 Composite(Varyings input) : SV_Target
        {
            int2 pixel=clamp((int2)(input.uv*_CloudTargetSize.xy),0,(int2)_CloudTargetSize.xy-1);
            if (_CloudTemporal.z>0)
                return float4(LOAD_TEXTURE2D(_CloudDiagnostic,pixel).rgb,0);
            float4 color=LOAD_TEXTURE2D(_CloudResolved,pixel);
            float4 meta=LOAD_TEXTURE2D(_CloudResolvedMeta,pixel);
            if(_CloudLightingDebug>0) return color;
            // Fast temporal intensity never enters the cloud history. The stable spatial mask does.
            float cycle=frac(_StormMotion.x/9)*9;
            float flash=exp(-pow((cycle-2)*12,2))+0.6*exp(-pow((cycle-2.22)*18,2));
            if (_StormInfluence.z==0) color.rgb+=float3(0.68,0.76,1)*meta.w*flash*_StormEmission.y;
            // Full-resolution depth remains authoritative even when the output scale is reduced.
            float3 origin,ray; CloudRay(input.uv,origin,ray);
            float sceneDistance=CloudSceneDistance(input.uv,origin);
            if (meta.x>sceneDistance+max(1,sceneDistance*0.002)) color=float4(0,0,0,1);
            if(_StormInfluence.z>0 && _StormInfluence.z<10) return color;
            float2 hits=CirrusIntersections(origin,ray,sceneDistance);
            if(_StormInfluence.z>=10)
            {
                float t=hits.x>0 ? hits.x : hits.y;
                if(t<=0) return float4(0,0,0,0);
                float2 model=CloudModelPosition(origin+ray*t).xz;
                float3 ndf=ReadCirrusNdf(model);
                float d=_StormInfluence.z<10.5 ? ndf.x : _StormInfluence.z<11.5 ? ndf.y : CirrusDensity(model,_CirrusPixelSize*t);
                return float4(d.xxx,0);
            }
            // Apply fog after temporal resolve, once per layer, with UE coverage weighting.
            color = DawnFogCloudLayer(color, origin, ray, meta.x);
            // Cirrus is evaluated every frame at final output resolution, independent of the
            // lower layer's motion history. Nesting guarantees depth order even above both layers.
            float4 nearCirrus=ShadeCirrus(origin,ray,hits.x),farCirrus=ShadeCirrus(origin,ray,hits.y);
            nearCirrus = DawnFogCloudLayer(nearCirrus, origin, ray, hits.x);
            farCirrus = DawnFogCloudLayer(farCirrus, origin, ray, hits.y);
            if(hits.x>0)
            {
                bool before=_CloudSphere.x>.5 || origin.y>_CirrusLayer.y;
                if(before) color=float4(nearCirrus.rgb+nearCirrus.a*color.rgb,nearCirrus.a*color.a);
                else color=float4(color.rgb+color.a*nearCirrus.rgb,color.a*nearCirrus.a);
            }
            color=float4(color.rgb+color.a*farCirrus.rgb,color.a*farCirrus.a);
            return color;
        }
        ENDHLSL
        Pass
        {
            Name "Shell cloud integration"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Raymarch
            ENDHLSL
        }
        Pass
        {
            Name "Motion compensated temporal reconstruction"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment ResolveCloud
            ENDHLSL
        }
        Pass
        {
            Name "Composite transmittance and instantaneous flash"
            ZWrite Off ZTest Always Cull Off
            Blend One SrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }
    }
}
