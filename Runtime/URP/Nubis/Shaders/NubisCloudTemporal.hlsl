#ifndef NUBIS_CLOUD_TEMPORAL_INCLUDED
#define NUBIS_CLOUD_TEMPORAL_INCLUDED

bool CloudProjectPrevious(float3 world, out float2 uv)
{
    float4 clip=mul(_CloudPreviousVP,float4(world,1));
    #if UNITY_UV_STARTS_AT_TOP
        clip.y=-clip.y;
    #endif
    uv=clip.xy/max(1e-6,clip.w)*0.5+0.5;
    return clip.w>0 && all(uv>=0) && all(uv<1);
}
bool SceneDepthCompatible(float previous, float expected)
{
    bool a=previous<_CloudSphere.y*0.999, b=expected<_CloudSphere.y*0.999;
    return a==b && (!a || abs(previous-expected)<max(4,expected*0.025));
}
CloudOutput ResolveCloud(Varyings input)
{
    int2 pixel=(int2)input.positionCS.xy;
    float2 uv=(pixel+0.5)*_CloudTargetSize.zw;
    float3 origin,ray; CloudRay(uv,origin,ray);
    float sceneDistance=CloudSceneDistance(uv,origin);
    bool fresh=all((pixel%4)==(int2)_CloudPhase.xy);
    int2 work=pixel/4;
    CloudOutput current=(CloudOutput)0;
    if (fresh)
    {
        current.color=LOAD_TEXTURE2D(_CloudCurrent,work);
        current.meta=LOAD_TEXTURE2D(_CloudCurrentMeta,work);
    }
    else
    {
        // Sparse neighbours can see entirely different clouds at the horizon. Seed motion
        // reprojection with this pixel's history depth, not their interpolated distance.
        current.color=LOAD_TEXTURE2D(_CloudHistory,pixel);
        current.meta=LOAD_TEXTURE2D(_CloudHistoryMeta,pixel);
    }
    float4 neighborhoodMin=current.color, neighborhoodMax=current.color;
    float maskMin=current.meta.w, maskMax=current.meta.w;
    if (fresh)
    {
        [unroll] for(int y=-1;y<=1;y++)
        [unroll] for(int x=-1;x<=1;x++)
        {
            int2 cell=clamp(work+int2(x,y),0,(int2)_CloudWorkSize.xy-1);
            float4 c=LOAD_TEXTURE2D(_CloudCurrent,cell);
            float4 m=LOAD_TEXTURE2D(_CloudCurrentMeta,cell);
            if (m.x>sceneDistance+max(2,sceneDistance*0.005)) continue;
            if (!SceneDepthCompatible(m.y,sceneDistance)) continue;
            neighborhoodMin=min(neighborhoodMin,c); neighborhoodMax=max(neighborhoodMax,c);
            maskMin=min(maskMin,m.w); maskMax=max(maskMax,m.w);
        }
    }
    float distance=current.meta.x;
    float reprojectionDistance=distance>0 ? distance : min(30000,_CloudSphere.y);
    float3 world=origin+ray*reprojectionDistance;
    float motionConfidence;
    float3 previousWorld=PreviousCloudPosition(world,_CloudTemporal.x,motionConfidence);
    float2 previousUV;
    bool valid=CloudProjectPrevious(previousWorld,previousUV);
    int2 previousPixel=clamp((int2)(previousUV*_CloudTargetSize.xy),0,(int2)_CloudTargetSize.xy-1);
    float4 oldMeta=LOAD_TEXTURE2D(_CloudHistoryMeta,previousPixel);
    float4 oldColor=0;
    float2 texelPosition=previousUV*_CloudTargetSize.xy-0.5;
    int2 texelBase=(int2)floor(texelPosition);
    float2 fraction=frac(texelPosition);
    float historySamples=0;
    [unroll] for(int hy=0;hy<2;hy++)
    [unroll] for(int hx=0;hx<2;hx++)
    {
        int2 hp=clamp(texelBase+int2(hx,hy),0,(int2)_CloudTargetSize.xy-1);
        float4 hm=LOAD_TEXTURE2D(_CloudHistoryMeta,hp);
        float weight=(hx==0 ? 1-fraction.x : fraction.x)*(hy==0 ? 1-fraction.y : fraction.y);
        bool sameLayer=(hm.x==0)==(oldMeta.x==0) && abs(hm.x-oldMeta.x)<max(180,oldMeta.x*0.15);
        if (sameLayer && SceneDepthCompatible(hm.y,oldMeta.y))
        {
            oldColor+=LOAD_TEXTURE2D(_CloudHistory,hp)*weight;
            historySamples+=weight;
        }
    }
    oldColor=historySamples>1e-5 ? oldColor/historySamples : LOAD_TEXTURE2D(_CloudHistory,previousPixel);
    float expectedDistance=length(previousWorld-_CloudPreviousCameraPosition);
    valid=valid && oldMeta.z>0 && _CloudTemporal.w>0;
    if (distance>0)
        valid=valid && oldMeta.x>0 && abs(oldMeta.x-expectedDistance)<max(180,expectedDistance*0.15);
    else valid=valid && oldColor.a>0.98;

    // Test previous opaque depth at the *scene* reprojection, independent of cloud advection.
    float2 sceneUV=previousUV;
    float expectedScene=_CloudSphere.y;
    if (sceneDistance<_CloudSphere.y*0.999)
    {
        float3 sceneWorld=origin+ray*sceneDistance;
        valid=CloudProjectPrevious(sceneWorld,sceneUV) && valid;
        expectedScene=length(sceneWorld-_CloudPreviousCameraPosition);
    }
    int2 scenePixel=clamp((int2)(sceneUV*_CloudTargetSize.xy),0,(int2)_CloudTargetSize.xy-1);
    float previousScene=LOAD_TEXTURE2D(_CloudHistoryMeta,scenePixel).y;
    valid=valid && SceneDepthCompatible(previousScene,expectedScene);
    valid=valid && (oldMeta.x==0 || oldMeta.x<length(previousWorld-_CloudPreviousCameraPosition)+max(180,distance*0.15));

    if (!fresh && !valid)
    {
        // A disocclusion needs a real ray. Interpolating sparse neighbours here creates
        // phase-dependent cloud/sky holes that alternate over the sixteen-frame cycle.
        current=IntegrateCloud(uv);
        distance=current.meta.x;
        neighborhoodMin=neighborhoodMax=current.color;
        maskMin=maskMax=current.meta.w;
        fresh=true;
    }
    // Only a newly traced ray can constrain history. The changing sparse lattice is not
    // evidence that an untraced pixel's cloud has changed. Clamp color and opacity together.
    float4 padding=float4(0.015,0.015,0.015,0.01);
    if (fresh) oldColor=clamp(oldColor,neighborhoodMin-padding,neighborhoodMax+padding);
    oldColor.rgb=max(0,oldColor.rgb); oldColor.a=saturate(oldColor.a);
    // Each ray is refreshed once per 4x4 cycle. Apply the configured retention over that
    // interval, and transport valid history unchanged between real observations.
    float historyWeight=valid ? (fresh ? pow(_CloudTemporal.y,16)*motionConfidence : 1) : 0;
    CloudOutput output;
    output.color=lerp(current.color,oldColor,historyWeight);
    float currentDistance=distance;
    if (valid && oldMeta.x>0 && distance>0)
    {
        // History distance remains a current-ray quantity, never a stale previous-camera distance.
        currentDistance=lerp(distance,max(0,distance+oldMeta.x-expectedDistance),historyWeight*0.5);
    }
    output.meta=float4(currentDistance,sceneDistance,fresh ? 1 : (valid ? oldMeta.z : 0.25),
        lerp(current.meta.w,fresh ? clamp(oldMeta.w,maskMin,maskMax) : oldMeta.w,historyWeight));
    output.debug=float4(TemporalDiagnostic(uv,currentDistance,previousUV,fresh,!valid),valid ? 0 : 1);
    return output;
}
#endif
