using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace NubisStorm.Editor
{
    public static class NubisCirrusAuthoring
    {
        const string Path="Assets/NubisCloud/Generated/Data/CirrusData.asset";
        const string ShapeReference="Packages/com.tencent.dawn.tod/Editor/URP/Nubis/Editor/Reference/CirrusShapes-P65.png";
        const int ShapeVersion=1;
        public static void Ensure(NubisCloudData shared)
        {
            if(shared.cirrus && shared.cirrus.IsReady && shared.cirrus.shapeVersion==ShapeVersion) return;
            var data=shared.cirrus ? shared.cirrus : AssetDatabase.LoadAssetAtPath<NubisCirrusData>(Path);
            if(!data)
            {
                data=ScriptableObject.CreateInstance<NubisCirrusData>(); data.Initialize();
                AssetDatabase.CreateAsset(data,Path);
            }
            if(!data.weather || !data.shapes) Generate(data);
            else if(data.shapeVersion!=ShapeVersion) GenerateShapes(data);
            Save(data); shared.cirrus=data; EditorUtility.SetDirty(shared); AssetDatabase.SaveAssets();
        }
        [MenuItem("Tools/Nubis Clouds/Restore P65 cirrus shapes")]
        public static void RestoreReferenceShapes()
        {
            var data=AssetDatabase.LoadAssetAtPath<NubisCirrusData>(Path);
            if(!data) throw new System.InvalidOperationException("Build the shared cloud data first.");
            GenerateShapes(data); Save(data);
        }
        static void GenerateShapes(NubisCirrusData data)
        {
            // P65 embedded images packed as R streaky / G wispy / B round.
            // Decode as linear data: retain the document's gray values without sRGB conversion.
            var reference=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try
            {
                var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(ShapeReference);
                string referencePath=package==null ? ShapeReference : System.IO.Path.Combine(package.resolvedPath,
                    ShapeReference.Substring(("Packages/"+package.name+"/").Length));
                if(!ImageConversion.LoadImage(reference,File.ReadAllBytes(referencePath)))
                    throw new System.IO.InvalidDataException("Cannot decode the P65 cirrus shape reference.");
                int w=reference.width,h=reference.height;
                if(!data.shapes) data.shapes=new Texture2D(w,h,TextureFormat.RGBAHalf,true,true) {name="Cirrus streaky-wispy-round"};
                else if(data.shapes.width!=w || data.shapes.height!=h || data.shapes.format!=TextureFormat.RGBAHalf)
                    data.shapes.Reinitialize(w,h,TextureFormat.RGBAHalf,true);
                data.shapes.wrapMode=TextureWrapMode.Repeat;
                data.shapes.filterMode=FilterMode.Trilinear;
                data.shapes.SetPixels(reference.GetPixels()); data.shapes.Apply(true,false);
                data.shapeVersion=ShapeVersion;
            }
            finally {Object.DestroyImmediate(reference);}
        }
        public static void Save(NubisCirrusData data)
        {
            if(!data.IsReady || NubisNdfAuthoring.SourceChanged(data,data.source,data.resolution)) data.Upload();
            foreach(var t in new[]{data.weather,data.shapes,data.authored}) if(t)
            {
                if(!AssetDatabase.Contains(t)) AssetDatabase.AddObjectToAsset(t,data);
                EditorUtility.SetDirty(t);
            }
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            NubisNdfAuthoring.RememberSource(data,data.source,data.resolution);
            NubisNdfAuthoring.RefreshViews();
        }
        public static void TrackUndo(NubisCirrusData data)
        {
            if(data && data.IsReady) NubisNdfAuthoring.TrackUndo(data,data.source,data.resolution);
        }
        public static void RestoreAfterUndo(NubisCirrusData data)
        {
            if(!data || !AssetDatabase.Contains(data) || data.source==null) return;
            if(!NubisNdfAuthoring.HasTrackedSource(data)) {TrackUndo(data); return;}
            if(NubisNdfAuthoring.SourceChanged(data,data.source,data.resolution)) Save(data);
        }
        static float Noise(float u,float v,int frequency,uint seed)
            => NubisCloudAssetBuilder.Perlin(u,v,.317f,frequency,seed);
        static float Fbm(float u,float v,int frequency,uint seed)
            => Noise(u,v,frequency,seed)+.5f*Noise(u,v,frequency*2,seed+127)+.25f*Noise(u,v,frequency*4,seed+347);
        public static void Generate(NubisCirrusData data)
        {
            const int n=256; uint seed=unchecked((uint)data.seed); int frequency=Mathf.Clamp(data.frequency,2,12);
            var weather=new Color[n*n];
            Parallel.For(0,n,y=>
            {
                for(int x=0;x<n;x++)
                {
                    float u=(x+.5f)/n,v=(y+.5f)/n;
                    float coverage=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.32f,.38f,Fbm(u,v,frequency,seed)));
                    float type=Mathf.Clamp01(.45f+Fbm(u+.23f,v+.41f,frequency,seed+13)*.8f);
                    weather[x+y*n]=new Color(coverage,type,0,1);
                }
            });
            GenerateShapes(data);
            if(!data.weather) data.weather=new Texture2D(n,n,TextureFormat.RGHalf,false,true) {name="Cirrus NDF coverage-type"};
            data.weather.wrapMode=TextureWrapMode.Repeat;
            data.weather.filterMode=FilterMode.Bilinear;
            data.weather.SetPixels(weather); data.weather.Apply(false,false);
            // Only generated data changes; authored source is deliberately retained.
            Save(data);
        }
        public static void Paint(NubisCirrusData data,Vector2 uv,Vector2 radius,int channel,float value,float opacity,float hardness,bool reveal)
        {
            int n=data.resolution; radius=Vector2.Max(radius,Vector2.one*.0001f);
            int x0=Mathf.Max(0,Mathf.FloorToInt((uv.x-radius.x)*n)),x1=Mathf.Min(n-1,Mathf.CeilToInt((uv.x+radius.x)*n));
            int y0=Mathf.Max(0,Mathf.FloorToInt((uv.y-radius.y)*n)),y1=Mathf.Min(n-1,Mathf.CeilToInt((uv.y+radius.y)*n));
            for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
            {
                float distance=new Vector2(((x+.5f)/n-uv.x)/radius.x,((y+.5f)/n-uv.y)/radius.y).magnitude;
                if(distance>=1) continue;
                float weight=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Min(.999f,hardness),1,distance)))*opacity;
                int i=x+y*n; data.Set(i,channel,Mathf.Lerp(data.Get(i,channel),value,weight));
                if(reveal && channel!=2) data.Set(i,2,Mathf.Max(data.Get(i,2),weight));
            }
        }
    }
}
