#ifndef NUBIS_CLOUD_GEOMETRY_INCLUDED
#define NUBIS_CLOUD_GEOMETRY_INCLUDED

// Planet xyz = centre; w = ground radius. Sphere x enables curved geometry.
float4 _CloudPlanet, _CloudSphere;

float CloudArcAngle(float horizontal, float vertical)
{
    // HLSL atan2 on D3D11 can lose metres when its angular error is scaled by Earth radius.
    // Reduce to |x| <= tan(pi/8), then evaluate atan's odd series in full float precision.
    float v=abs(vertical), ratio=min(horizontal,v)/max(1e-12,max(horizontal,v));
    bool reduce=ratio>0.41421356237;
    float x=reduce ? (ratio-1)/(ratio+1) : ratio;
    float x2=x*x;
    float p=-1.0/19;
    p=1.0/17+x2*p; p=-1.0/15+x2*p; p=1.0/13+x2*p; p=-1.0/11+x2*p;
    p=1.0/9+x2*p; p=-1.0/7+x2*p; p=1.0/5+x2*p; p=-1.0/3+x2*p;
    float angle=x*(1+x2*p)+(reduce ? 0.785398163397 : 0);
    if (horizontal>v) angle=1.570796326795-angle;
    return vertical<0 ? 3.14159265359-angle : angle;
}

float CloudAltitude(float3 world)
{
    float3 q = world-_CloudPlanet.xyz;
    float groundY = _CloudPlanet.y+_CloudPlanet.w;
    float h = world.y-groundY;
    // Factored difference of squares retains precision near a 6371 km planet surface.
    return (dot(q.xz,q.xz)+h*(2*_CloudPlanet.w+h))/(length(q)+_CloudPlanet.w);
}
float3 CloudModelPosition(float3 world)
{
    if (_CloudSphere.x < 0.5) return world;
    float3 q = world-_CloudPlanet.xyz;
    float horizontal = length(q.xz);
    float angle = CloudArcAngle(horizontal,q.y);
    return float3(_CloudField.x+q.x*angle*_CloudPlanet.w/max(horizontal,1e-5),
        CloudAltitude(world), _CloudField.y+q.z*angle*_CloudPlanet.w/max(horizontal,1e-5));
}
float3 CloudWorldPosition(float3 model)
{
    if (_CloudSphere.x < 0.5) return model;
    float2 offset = model.xz-_CloudField.xy;
    float arc = length(offset), angle = arc/_CloudPlanet.w;
    float radius = _CloudPlanet.w+model.y;
    float2 horizontal = offset/max(arc,1e-5)*sin(angle)*radius;
    float y = _CloudPlanet.y+_CloudPlanet.w + model.y*cos(angle)
        -2*_CloudPlanet.w*pow(sin(angle*0.5),2);
    return float3(_CloudPlanet.x+horizontal.x,y,_CloudPlanet.z+horizontal.y);
}

float2 CloudSphereRoots(float3 origin, float3 ray, float altitude)
{
    float3 q = origin-_CloudPlanet.xyz;
    float radius = _CloudPlanet.w+altitude;
    float h = origin.y-(_CloudPlanet.y+_CloudPlanet.w)-altitude;
    float c = dot(q.xz,q.xz)+h*(2*radius+h);
    float b = dot(q,ray);
    float discriminant = b*b-c;
    if (discriminant < 0) return float2(1,-1);
    float root = sqrt(discriminant);
    // Stable quadratic solution for near-ground roots; direction is normalized.
    float farRoot = -b-(b >= 0 ? root : -root);
    float other = abs(farRoot)>1e-5 ? c/farRoot : -b+root;
    return float2(min(farRoot,other),max(farRoot,other));
}
float2 CloudBoxInterval(float3 origin, float3 ray)
{
    float3 safeRay = (step(0,ray)*2-1)*max(abs(ray),1e-7);
    float3 lower = float3(_CloudField.x-_CloudField.z*0.5,_CloudField.w,_CloudField.y-_CloudField.z*0.5);
    float3 upper = lower+float3(_CloudField.z,_StormSize.y,_CloudField.z);
    float3 a = (lower-origin)/safeRay, b = (upper-origin)/safeRay;
    return float2(max(max(min(a,b).x,min(a,b).y),min(a,b).z),
        min(min(max(a,b).x,max(a,b).y),max(a,b).z));
}
// Up to two disjoint shell segments. Do not integrate the empty inner sphere.
float4 CloudRayIntervals(float3 origin, float3 ray, float sceneDistance)
{
    float limit = min(sceneDistance,_CloudSphere.y);
    if (_CloudSphere.x < 0.5)
    {
        float2 box = CloudBoxInterval(origin,ray);
        float start = max(0,box.x), end = min(limit,box.y);
        return float4(start,max(start,end),0,0);
    }
    if (CloudAltitude(origin)<-0.1) return 0;
    float2 ground = CloudSphereRoots(origin,ray,0);
    if (ground.y >= ground.x && ground.x >= 0) limit = min(limit,ground.x);
    float2 outer = CloudSphereRoots(origin,ray,_CloudField.w+_StormSize.y);
    if (outer.y < outer.x) return 0;
    float start = max(0,outer.x), end = min(limit,outer.y);
    if (end <= start) return 0;
    float2 inner = CloudSphereRoots(origin,ray,_CloudField.w);
    if (inner.y < inner.x || inner.y <= start || inner.x >= end) return float4(start,end,0,0);
    float firstEnd = clamp(inner.x,start,end), secondStart = clamp(inner.y,start,end);
    return float4(start,firstEnd,secondStart,end);
}
#endif
