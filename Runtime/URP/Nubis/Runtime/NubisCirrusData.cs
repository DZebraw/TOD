using UnityEngine;

namespace NubisStorm
{
    [CreateAssetMenu(menuName="Nubis/Cirrus data",fileName="CirrusData")]
    public sealed class NubisCirrusData : ScriptableObject
    {
        [Tooltip("Independent 2.5D NDF: R coverage, G type.")]
        public Texture2D weather;
        [Tooltip("P65 reference density textures: R streaky, G wispy, B round.")]
        public Texture2D shapes;
        [HideInInspector] public int shapeVersion;
        [Min(1000)] public float fieldSize=48000;
        [Min(1000)] public float shapeSize=18000;
        public int seed=572;
        [Range(2,12)] public int frequency=3;
        public Vector2 authoredCenter;
        public Vector2 authoredSize=new Vector2(24000,24000);
        [HideInInspector] public int resolution=256,revision;
        // UNorm16 coverage/type/influence. Kept separate from the five-field vertical-profile NDF.
        [HideInInspector] public byte[] source;
        [HideInInspector] public Texture2D authored;
        public bool IsReady => weather && shapes && authored && source!=null && source.Length==resolution*resolution*6;
        public void Initialize()
        {
            source=new byte[resolution*resolution*6];
            for(int i=0;i<resolution*resolution;i++) {Set(i,0,.8f); Set(i,1,.25f);}
        }
        public float Get(int i,int channel)
        {
            int at=(i*3+channel)*2; return (source[at]|source[at+1]<<8)/65535f;
        }
        public void Set(int i,int channel,float value)
        {
            int at=(i*3+channel)*2,q=Mathf.RoundToInt(Mathf.Clamp01(value)*65535);
            source[at]=(byte)q; source[at+1]=(byte)(q>>8);
        }
        public void Upload()
        {
            if(source==null || source.Length!=resolution*resolution*6) Initialize();
            if(!authored) authored=new Texture2D(resolution,resolution,TextureFormat.RGBAHalf,false,true) {name="Cirrus authored coverage-type-mask"};
            authored.wrapMode=TextureWrapMode.Clamp; authored.filterMode=FilterMode.Bilinear;
            var pixels=new Color[resolution*resolution];
            for(int i=0;i<pixels.Length;i++) pixels[i]=new Color(Get(i,0),Get(i,1),Get(i,2),1);
            authored.SetPixels(pixels); authored.Apply(false,false); revision++;
        }
        public int Signature()
        {
            unchecked
            {
                int hash=GetInstanceID()*397+revision;
                hash=hash*31+authoredCenter.GetHashCode(); hash=hash*31+authoredSize.GetHashCode();
                hash=hash*31+fieldSize.GetHashCode(); hash=hash*31+shapeSize.GetHashCode();
                foreach(var t in new[]{weather,shapes,authored}) if(t) hash=hash*31+t.GetInstanceID()+(int)t.updateCount;
                return hash;
            }
        }
    }
}
