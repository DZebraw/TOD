using System;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace NubisStorm.Editor
{
    public static class NubisNdfAuthoring
    {
        sealed class SourceSnapshot
        {
            public byte[] source;
            public int resolution;
        }
        static readonly ConditionalWeakTable<UnityEngine.Object,SourceSnapshot> uploadedSources =
            new ConditionalWeakTable<UnityEngine.Object,SourceSnapshot>();

        internal static void TrackUndo(UnityEngine.Object asset,byte[] source,int resolution)
        {
            if(asset && source!=null && !uploadedSources.TryGetValue(asset,out _))
                RememberSource(asset,source,resolution);
        }
        internal static void RememberSource(UnityEngine.Object asset,byte[] source,int resolution)
        {
            uploadedSources.Remove(asset);
            uploadedSources.Add(asset,new SourceSnapshot {source=(byte[])source.Clone(),resolution=resolution});
        }
        internal static bool SourceChanged(UnityEngine.Object asset,byte[] source,int resolution)
            => source==null || !uploadedSources.TryGetValue(asset,out var previous) ||
                previous.resolution!=resolution || !source.SequenceEqual(previous.source);
        internal static bool HasTrackedSource(UnityEngine.Object asset)
            => uploadedSources.TryGetValue(asset,out _);

        public static void TrackUndo(NubisInfluenceData asset)
        {
            if(asset && asset.IsReady) TrackUndo(asset,asset.source,asset.resolution);
        }
        public static void RestoreAfterUndo(NubisInfluenceData asset)
        {
            if(!asset || !AssetDatabase.Contains(asset) || asset.source==null) return;
            if(!uploadedSources.TryGetValue(asset,out _)) {TrackUndo(asset); return;}
            if(SourceChanged(asset,asset.source,asset.resolution)) Save(asset);
        }
        public static void RefreshViews()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        public static NubisInfluenceData Create(string path,bool storm=false)
        {
            var existing=AssetDatabase.LoadAssetAtPath<NubisInfluenceData>(path);
            if(existing) return existing;
            var asset=ScriptableObject.CreateInstance<NubisInfluenceData>();
            asset.name=System.IO.Path.GetFileNameWithoutExtension(path);
            asset.Initialize();
            if(storm) StormPreset(asset,0.19f);
            AssetDatabase.CreateAsset(asset,path);
            Save(asset);
            return asset;
        }
        public static void Save(NubisInfluenceData asset)
        {
            if(!asset.IsReady || SourceChanged(asset,asset.source,asset.resolution)) asset.Upload();
            if(!AssetDatabase.Contains(asset.fields)) AssetDatabase.AddObjectToAsset(asset.fields,asset);
            if(!AssetDatabase.Contains(asset.bottomMask)) AssetDatabase.AddObjectToAsset(asset.bottomMask,asset);
            EditorUtility.SetDirty(asset); EditorUtility.SetDirty(asset.fields); EditorUtility.SetDirty(asset.bottomMask);
            AssetDatabase.SaveAssetIfDirty(asset);
            RememberSource(asset,asset.source,asset.resolution);
            RefreshViews();
        }
        public static void Paint(NubisInfluenceData asset,Vector2 uv,float radius,NubisNdfChannel channel,float value,float opacity,float hardness,bool reveal)
            => Paint(asset,uv,Vector2.one*radius,channel,value,opacity,hardness,reveal);

        // An ellipse in texture UV is a circular brush in metres on non-square world regions.
        public static void Paint(NubisInfluenceData asset,Vector2 uv,Vector2 radius,NubisNdfChannel channel,float value,float opacity,float hardness,bool reveal)
        {
            int n=asset.resolution;
            radius=Vector2.Max(radius,Vector2.one*0.0001f);
            int x0=Mathf.Max(0,Mathf.FloorToInt((uv.x-radius.x)*n)),x1=Mathf.Min(n-1,Mathf.CeilToInt((uv.x+radius.x)*n));
            int y0=Mathf.Max(0,Mathf.FloorToInt((uv.y-radius.y)*n)),y1=Mathf.Min(n-1,Mathf.CeilToInt((uv.y+radius.y)*n));
            for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
            {
                Vector2 delta=new Vector2((x+0.5f)/n,(y+0.5f)/n)-uv;
                float distance=new Vector2(delta.x/radius.x,delta.y/radius.y).magnitude;
                if(distance>=1) continue;
                float falloff=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Min(hardness,0.999f),1,distance));
                int pixel=x+y*n;
                asset.SetHeightSafe(pixel,(int)channel,Mathf.Lerp(asset.Get(pixel,(int)channel),value,falloff*opacity));
                if(reveal && channel!=NubisNdfChannel.InfluenceMask)
                    asset.Set(pixel,5,Mathf.Max(asset.Get(pixel,5),falloff*opacity));
            }
        }
        public static void StormPreset(NubisInfluenceData asset,float eye)
        {
            int n=asset.resolution;
            for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                var p=new Vector2((x+0.5f)/n-0.5f,(y+0.5f)/n-0.5f)*2.32f;
                float r=p.magnitude,wall=Mathf.Exp(-Mathf.Pow((r-0.42f)*5,2));
                float cavity=Mathf.Exp(-r*r/Mathf.Max(0.002f,eye*eye));
                float variation=NubisCloudAssetBuilder.Perlin(p.x*0.2f+0.5f,p.y*0.2f+0.5f,0.371f,5,877);
                int i=x+y*n;
                asset.Set(i,0,0.075f+cavity*0.27f-wall*0.055f+variation*0.035f);
                asset.Set(i,1,Mathf.Lerp(0.83f,1,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.18f,0.72f,r))));
                asset.Set(i,2,Mathf.Lerp(0.8f,1,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.4f,1.05f,r))));
                asset.Set(i,3,Mathf.Lerp(0.52f,0.98f,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.3f,0.88f,r))));
                asset.Set(i,4,Mathf.Lerp(0.23f,0.42f,wall));
                asset.Set(i,5,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.72f,1.16f,r)));
            }
        }
        public static void CloudPreset(NubisInfluenceData asset)
        {
            int n=asset.resolution;
            for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                float u=(x+0.5f)/n, v=(y+0.5f)/n;
                float a=new Vector2((u-0.57f)/0.21f,(v-0.65f)/0.12f).magnitude;
                float b=new Vector2((u-0.35f)/0.09f,(v-0.54f)/0.14f).magnitude;
                float mask=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.45f,1,Mathf.Min(a,b)));
                int i=x+y*n;
                asset.Set(i,0,0.04f); asset.Set(i,1,0.82f); asset.Set(i,2,0.98f);
                asset.Set(i,3,0.85f); asset.Set(i,4,0.1f); asset.Set(i,5,mask);
            }
        }
        public static Color[] Preview(NubisInfluenceData asset,int channel)
        {
            int n=asset.resolution; var pixels=new Color[n*n];
            for(int i=0;i<pixels.Length;i++)
            {
                float mask=asset.Get(i,5);
                if(channel>=0) {float v=asset.Get(i,channel); pixels[i]=new Color(v,v,v,1);}
                else {float coverage=asset.Get(i,2)*mask; pixels[i]=new Color(coverage,asset.Get(i,3)*coverage,asset.Get(i,4)*coverage,1);}
            }
            return pixels;
        }
    }
}
