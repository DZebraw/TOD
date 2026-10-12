using System;
using UnityEngine;

namespace NubisStorm
{
    public enum NubisNdfChannel { MinHeight, MaxHeight, Coverage, TopType, BottomType, InfluenceMask }

    [CreateAssetMenu(menuName="Nubis/Influence NDF",fileName="InfluenceNDF")]
    public sealed class NubisInfluenceData : ScriptableObject
    {
        [Tooltip("World-space XZ extent in metres; the renderer clamps each dimension to at least 100 m.")]
        public Vector2 worldSize = new Vector2(20000,20000);
        public Vector2 center;
        [HideInInspector] public int resolution = 256;
        [HideInInspector] public int revision;
        // Canonical UNorm16 source is serialized on the asset, so Undo restores the actual paint.
        [HideInInspector] public byte[] source;
        [HideInInspector] public Texture2D fields;
        [HideInInspector] public Texture2D bottomMask;
        public bool IsReady => fields && bottomMask && source != null && source.Length == resolution*resolution*12;

        public void Initialize(int size=256)
        {
            resolution=Mathf.Clamp(size,32,1024);
            source=new byte[resolution*resolution*12];
            for(int i=0;i<resolution*resolution;i++)
            {
                Set(i,0,0.025f); Set(i,1,0.6f); Set(i,2,0.9f); Set(i,3,0.72f); Set(i,4,0.12f);
            }
        }
        public float Get(int pixel,int channel)
        {
            int index=(pixel*6+channel)*2;
            return (source[index] | source[index+1]<<8)/65535f;
        }
        public void Set(int pixel,int channel,float value)
        {
            int index=(pixel*6+channel)*2, packed=Mathf.RoundToInt(Mathf.Clamp01(value)*65535);
            source[index]=(byte)(packed&255); source[index+1]=(byte)(packed>>8);
        }
        public void SetHeightSafe(int pixel,int channel,float value)
        {
            if(channel==0) value=Mathf.Min(value,Get(pixel,1)-0.001f);
            if(channel==1) value=Mathf.Max(value,Get(pixel,0)+0.001f);
            Set(pixel,channel,value);
        }
        public void Upload()
        {
            if(source==null || source.Length!=resolution*resolution*12) Initialize(resolution);
            if(!fields) fields=new Texture2D(resolution,resolution,TextureFormat.RGBAHalf,false,true) {name="NDF min-max-coverage-top"};
            if(!bottomMask) bottomMask=new Texture2D(resolution,resolution,TextureFormat.RGHalf,false,true) {name="NDF bottom-mask"};
            if(fields.width!=resolution) fields.Reinitialize(resolution,resolution,TextureFormat.RGBAHalf,false);
            if(bottomMask.width!=resolution) bottomMask.Reinitialize(resolution,resolution,TextureFormat.RGHalf,false);
            fields.wrapMode=bottomMask.wrapMode=TextureWrapMode.Clamp;
            fields.filterMode=bottomMask.filterMode=FilterMode.Bilinear;
            var a=new Color[resolution*resolution]; var b=new Color[a.Length];
            for(int i=0;i<a.Length;i++) { a[i]=new Color(Get(i,0),Get(i,1),Get(i,2),Get(i,3)); b[i]=new Color(Get(i,4),Get(i,5),0,0); }
            fields.SetPixels(a); bottomMask.SetPixels(b); fields.Apply(false,false); bottomMask.Apply(false,false);
            revision++;
        }
        public int Signature()
        {
            unchecked { return GetInstanceID()*31+revision*397+center.GetHashCode()+worldSize.GetHashCode(); }
        }
    }
}
