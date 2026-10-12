using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace NubisStorm.Editor
{
    public static class NubisCloudAssetBuilder
    {
        const string Root = "Assets/NubisCloud/Generated";
        public const int Version = 4;

        [MenuItem("Tools/Nubis Clouds/Rebuild NDF, profiles and noise")]
        public static void Rebuild() => Build(true);

        [MenuItem("Tools/Nubis Clouds/Export model data previews")]
        public static void ExportPreviews()
        {
            var data = Build();
            string output = Path.GetFullPath(Application.dataPath + "/../CloudCaptures");
            Directory.CreateDirectory(output);
            Color[] fields = data.weather.GetPixels();
            var bottomPixels=data.bottomType.GetPixels();
            var composite=new Color[fields.Length];
            for(int i=0;i<composite.Length;i++) composite[i]=new Color(fields[i].b,fields[i].a*fields[i].b,bottomPixels[i].r*fields[i].b,1);
            SavePreview(output+"/NDF-Composite.png",data.weather.width,data.weather.height,composite);
            string[] names = { "MinHeight", "MaxHeight", "Coverage", "TopType" };
            for (int channel = 0; channel < 4; channel++)
            {
                var pixels = new Color[fields.Length];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(fields[i][channel],fields[i][channel],fields[i][channel],1);
                SavePreview(output+"/NDF-"+names[channel]+".png",256,256,pixels);
            }
            foreach (var pair in new[] { ("BottomType",data.bottomType), ("TopProfile",data.topProfile), ("BottomProfile",data.bottomProfile) })
            {
                var pixels = pair.Item2.GetPixels();
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(pixels[i].r,pixels[i].r,pixels[i].r,1);
                SavePreview(output+"/NDF-"+pair.Item1+".png",256,256,pixels);
            }
            var noise = data.shapeNoise.GetPixels();
            var noisePanels = new Color[512*128];
            for (int channel = 0; channel < 4; channel++)
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float value = noise[x+128*(y+128*64)][channel];
                noisePanels[x+channel*128+y*512] = new Color(value,value,value,1);
            }
            SavePreview(output+"/Noise-RGBA-Slice.png",512,128,noisePanels);
        }
        static void SavePreview(string path, int width, int height, Color[] pixels)
        {
            var texture = new Texture2D(width,height,TextureFormat.RGB24,false);
            texture.SetPixels(pixels); texture.Apply();
            File.WriteAllBytes(path,texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
        public static NubisCloudData Build(bool force = false)
        {
            Directory.CreateDirectory(Root);
            foreach (var folder in new[] { "Data", "NDF", "Noise", "Profiles", "Materials", "Rendering" })
                Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            var data = AssetDatabase.LoadAssetAtPath<NubisCloudData>(Root + "/Data/SharedCloudData.asset");
            if (data && data.IsReady && data.generationVersion == Version && !force) { EnsureAuthoring(data); return data; }
            if (!data)
            {
                data = ScriptableObject.CreateInstance<NubisCloudData>();
                AssetDatabase.CreateAsset(data, Root + "/Data/SharedCloudData.asset");
            }
            data.shapeNoise = Save(CreateNoise(), "Noise/PerlinWorley128.asset");
            BuildNdf(data,out var weather, out var bottomType);
            data.weather = Save(weather, "NDF/SkyNDF.asset");
            data.bottomType = Save(bottomType, "NDF/SkyBottomType.asset");
            data.topProfile = Save(CreateProfile(true), "Profiles/TopProfileLUT.asset");
            data.bottomProfile = Save(CreateProfile(false), "Profiles/BottomProfileLUT.asset");
            data.fieldSize = 16000;
            data.generationVersion = Version;
            EnsureAuthoring(data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("Nubis: built 128^3 Perlin-Worley RGBA, five NDF fields and two profile LUTs.");
            return data;
        }

        static void EnsureAuthoring(NubisCloudData data)
        {
            NubisCirrusAuthoring.Ensure(data);
            if(data.authoredNdf && data.superstormNdf) return;
            if(!data.authoredNdf) data.authoredNdf=NubisNdfAuthoring.Create(Root+"/NDF/AuthoredNDF.asset");
            if(!data.superstormNdf) data.superstormNdf=NubisNdfAuthoring.Create(Root+"/NDF/SuperstormNDF.asset",true);
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
        }
        public static void RegenerateWeather(NubisCloudData data)
        {
            BuildNdf(data,out var weather,out var bottom);
            // Only regenerate procedural fields. Authored and storm sources are separate assets.
            if(data.weather) {EditorUtility.CopySerialized(weather,data.weather); UnityEngine.Object.DestroyImmediate(weather); data.weather.Apply(false,false); EditorUtility.SetDirty(data.weather);}
            else data.weather=Save(weather,"NDF/SkyNDF.asset");
            if(data.bottomType) {EditorUtility.CopySerialized(bottom,data.bottomType); UnityEngine.Object.DestroyImmediate(bottom); data.bottomType.Apply(false,false); EditorUtility.SetDirty(data.bottomType);}
            else data.bottomType=Save(bottom,"NDF/SkyBottomType.asset");
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets(); SceneView.RepaintAll();
        }

        static T Save<T>(T generated, string name) where T : UnityEngine.Object
        {
            string path = Root + "/" + name;
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(generated, existing);
                UnityEngine.Object.DestroyImmediate(generated);
                if (existing is Texture3D volume) volume.Apply(true,false);
                if (existing is Texture2D texture) texture.Apply(false,false);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }

        static uint Hash(int x, int y, int z, uint seed)
        {
            unchecked
            {
                uint h = (uint)x*374761393u + (uint)y*668265263u + (uint)z*2246822519u + seed;
                h = (h ^ (h >> 13))*1274126177u;
                return h ^ (h >> 16);
            }
        }
        static int Wrap(int x, int period) => (x % period + period) % period;
        static float Random01(uint x) => (x & 0x00ffffff)/16777216f;
        static float Fade(float x) => x*x*x*(x*(x*6-15)+10);
        static float Lerp(float a, float b, float t) => a + (b-a)*t;
        static float Gradient(uint hash, float x, float y, float z)
        {
            int h = (int)(hash & 15);
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
        // Periodic gradient Perlin, not value noise. Each lattice corner carries a gradient.
        public static float Perlin(float x, float y, float z, int period, uint seed)
        {
            x *= period; y *= period; z *= period;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
            x -= ix; y -= iy; z -= iz;
            float sum = 0;
            float fx = Fade(x), fy = Fade(y), fz = Fade(z);
            for (int k = 0; k < 2; k++)
            for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                float weight = (i == 0 ? 1-fx : fx)*(j == 0 ? 1-fy : fy)*(k == 0 ? 1-fz : fz);
                sum += weight*Gradient(Hash(Wrap(ix+i,period),Wrap(iy+j,period),Wrap(iz+k,period),seed), x-i,y-j,z-k);
            }
            return sum;
        }
        public static float InvertedWorley(float x, float y, float z, int period, uint seed)
        {
            x *= period; y *= period; z *= period;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
            float distance = 4;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = ix+dx, cy = iy+dy, cz = iz+dz;
                int wx = Wrap(cx,period), wy = Wrap(cy,period), wz = Wrap(cz,period);
                float px = cx+Random01(Hash(wx,wy,wz,seed))-x;
                float py = cy+Random01(Hash(wx,wy,wz,seed+379))-y;
                float pz = cz+Random01(Hash(wx,wy,wz,seed+719))-z;
                distance = Math.Min(distance,px*px+py*py+pz*pz);
            }
            return 1-Mathf.Clamp01((float)Math.Sqrt(distance));
        }
        public static Color NoiseAt(float x, float y, float z)
        {
            float p = (Perlin(x,y,z,4,113) + 0.5f*Perlin(x,y,z,8,227) +
                0.25f*Perlin(x,y,z,16,337) + 0.125f*Perlin(x,y,z,32,449))/1.875f;
            p = Mathf.Clamp01(0.5f + p*1.5f);
            float w0 = InvertedWorley(x,y,z,4,557);
            float w1 = InvertedWorley(x,y,z,8,719);
            float w2 = InvertedWorley(x,y,z,16,997);
            // Authored approximation to slide 32, not an unpublished Guerrilla formula.
            float billows = 0.625f*w0 + 0.25f*w1 + 0.125f*w2;
            float perlinWorley = Lerp(billows,1,p);
            return new Color(perlinWorley,w0,w1,w2);
        }
        static Texture3D CreateNoise()
        {
            const int size = 128;
            var pixels = new Color32[size*size*size];
            Parallel.For(0,size,z =>
            {
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[x+size*(y+size*z)] = NoiseAt((x+0.5f)/size,(y+0.5f)/size,(z+0.5f)/size);
            });
            var texture = new Texture3D(size,size,size,UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain)
            { name = "Perlin-Worley + inverted Worley 4,8,16", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            texture.SetPixels32(pixels);
            texture.Apply(true,false);
            return texture;
        }

        static float Weather(float u, float v, uint seed,int frequency)
        {
            return Perlin(u,v,0.371f,frequency,seed)*0.62f + Perlin(u,v,0.173f,frequency*2-1,seed+197)*0.28f +
                Perlin(u,v,0.719f,frequency*3,seed+389)*0.10f;
        }
        static void BuildNdf(NubisCloudData data,out Texture2D weather, out Texture2D bottomType)
            => BuildNdf(data.weatherSeed,data.weatherFrequency,data.weatherCoverageBias,out weather,out bottomType);

        internal static void BuildNdf(int weatherSeed,int weatherFrequency,float weatherCoverageBias,out Texture2D weather,out Texture2D bottomType)
        {
            const int size = 256;
            var fields = new Color[size*size];
            var types = new Color[size*size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x+0.5f)/size, v = (y+0.5f)/size;
                uint seed=unchecked((uint)weatherSeed);
                int frequency=Mathf.Clamp(weatherFrequency,2,24);
                float large = Weather(u,v,seed,frequency)+weatherCoverageBias;
                float small = Weather(u+0.37f,v+0.18f,seed+684,frequency);
                // Clear gaps and soft cloudy regions, rather than scaling opacity everywhere.
                float coverage = Mathf.SmoothStep(0,1,Mathf.InverseLerp(-0.11f,0.22f,large));
                float minimum = 0.025f + small*0.025f;
                float maximum = 0.39f + Mathf.Clamp01(large+0.35f)*0.25f;
                float top = Mathf.Lerp(0.38f,0.8f,Mathf.Clamp01(small*2+0.5f));
                fields[x+y*size] = new Color(minimum, maximum, coverage*0.96f, top);
                types[x+y*size] = new Color(0.06f+Mathf.Clamp01(small+0.3f)*0.12f,0,0,1);
            }
            weather = new Texture2D(size,size,TextureFormat.RGBAHalf,false,true)
                { name = "Sky NDF · min, max, coverage, top type", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            bottomType = new Texture2D(size,size,TextureFormat.RHalf,false,true)
                { name = "Sky NDF · bottom type", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            weather.SetPixels(fields); weather.Apply(false,false);
            bottomType.SetPixels(types); bottomType.Apply(false,false);
        }
        // Continuous hand fit of slides 22 / 26. These curves are explicitly documented as approximations.
        public static float Profile(float type, float height, bool top)
        {
            if (height <= 0 || height >= 1) return 0;
            if (!top)
                return Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.008f,Mathf.Lerp(0.065f,0.82f,type*type),height));
            float peak = 0.19f + 0.25f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.10f,0.38f,type)) +
                0.55f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.38f,0.90f,type));
            // Both ends must rise together so increasing TopType cannot shrink existing cloud support.
            float shoulder = peak * 0.55f;
            return 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(shoulder,peak,height));
        }
        static Texture2D CreateProfile(bool top)
        {
            const int size = 256;
            var pixels = new Color[size*size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[x+y*size] = new Color(Profile(x/(float)(size-1),y/(float)(size-1),top),0,0,1);
            var texture = new Texture2D(size,size,TextureFormat.RHalf,false,true)
                { name = top ? "Top Profile (type, height)" : "Bottom Profile (type, height)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels(pixels); texture.Apply(false,false);
            return texture;
        }
    }
}
